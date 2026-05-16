using System.Text.Json;

namespace HomeAiAddon.Api.Semantic;

public sealed class SemanticOverrideStore(IConfiguration configuration, ILogger<SemanticOverrideStore> logger)
    : ISemanticOverrideStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim sync = new(1, 1);

    private string FilePath =>
        configuration["SemanticOverrides:FilePath"] ?? "/data/semantic-overrides.json";

    public async Task<IReadOnlyList<SemanticOverrideEntry>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await sync.WaitAsync(cancellationToken);
        try
        {
            return ToResponse(await ReadFileAsync(cancellationToken));
        }
        finally
        {
            sync.Release();
        }
    }

    public async Task<IReadOnlyList<SemanticOverrideEntry>> UpsertAsync(
        UpsertSemanticOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        var entityId = NormalizeEntityId(request.EntityId);
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("entityId is required", nameof(request));
        }

        await sync.WaitAsync(cancellationToken);
        try
        {
            var file = await ReadFileAsync(cancellationToken);
            file.Entities[entityId] = new SemanticOverrideFileEntry(
                NormalizeEnum(request.Role),
                NormalizeEnum(request.Intent),
                request.CanTrigger,
                request.CanAction,
                request.Noisy,
                request.Significant,
                request.SystemEvent,
                string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim());

            await WriteFileAsync(file, cancellationToken);
            return ToResponse(file);
        }
        finally
        {
            sync.Release();
        }
    }

    public async Task<IReadOnlyList<SemanticOverrideEntry>> DeleteAsync(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEntityId(entityId);

        await sync.WaitAsync(cancellationToken);
        try
        {
            var file = await ReadFileAsync(cancellationToken);
            file.Entities.Remove(normalized);
            await WriteFileAsync(file, cancellationToken);
            return ToResponse(file);
        }
        finally
        {
            sync.Release();
        }
    }

    private async Task<SemanticOverrideFile> ReadFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new SemanticOverrideFile(new Dictionary<string, SemanticOverrideFileEntry>(StringComparer.OrdinalIgnoreCase));
            }

            await using var stream = File.OpenRead(FilePath);
            var file = await JsonSerializer.DeserializeAsync<SemanticOverrideFile>(
                stream,
                JsonOptions,
                cancellationToken);

            return file ?? new SemanticOverrideFile(new Dictionary<string, SemanticOverrideFileEntry>(StringComparer.OrdinalIgnoreCase));
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Semantic overrides file is invalid, using empty overrides");
            return new SemanticOverrideFile(new Dictionary<string, SemanticOverrideFileEntry>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private async Task WriteFileAsync(SemanticOverrideFile file, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, file, JsonOptions, cancellationToken);
    }

    private static IReadOnlyList<SemanticOverrideEntry> ToResponse(SemanticOverrideFile file) =>
        file.Entities
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new SemanticOverrideEntry(
                kv.Key,
                kv.Value.Role,
                kv.Value.Intent,
                kv.Value.CanTrigger,
                kv.Value.CanAction,
                kv.Value.Noisy,
                kv.Value.Significant,
                kv.Value.SystemEvent,
                kv.Value.Reason))
            .ToList();

    private static string NormalizeEntityId(string entityId) => entityId.Trim().ToLowerInvariant();

    private static string? NormalizeEnum(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
