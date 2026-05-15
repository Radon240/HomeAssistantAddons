using System.Text.Json;
using HomeAiAddon.Api.Options;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.BehaviorAnalysis;

public interface IAnalysisExclusionStore
{
    AnalysisExclusionsSnapshot GetSnapshot();

    Task<AnalysisExclusionsSnapshot> SaveUiExclusionsAsync(
        IReadOnlyList<string> excludeEntities,
        IReadOnlyList<string> excludeDomains,
        CancellationToken cancellationToken = default);
}

public sealed class AnalysisExclusionStore(
    IOptionsMonitor<AddonOptions> addonOptions,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<AnalysisExclusionStore> logger) : IAnalysisExclusionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _lock = new();
    private AnalysisExclusionsSnapshot? _cache;

    public AnalysisExclusionsSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return _cache ??= BuildSnapshot();
        }
    }

    public async Task<AnalysisExclusionsSnapshot> SaveUiExclusionsAsync(
        IReadOnlyList<string> excludeEntities,
        IReadOnlyList<string> excludeDomains,
        CancellationToken cancellationToken = default)
    {
        var normalized = new UiExclusionsFile
        {
            ExcludeEntities = NormalizePatterns(excludeEntities),
            ExcludeDomains = NormalizePatterns(excludeDomains),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        var path = ResolveFilePath();
        EnsureDirectory(path);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken);

        lock (_lock)
        {
            _cache = BuildSnapshot(normalized);
            logger.LogInformation(
                "Saved UI analysis exclusions to {Path}: {EntityCount} entities, {DomainCount} domains",
                path,
                normalized.ExcludeEntities.Count,
                normalized.ExcludeDomains.Count);
            return _cache;
        }
    }

    private AnalysisExclusionsSnapshot BuildSnapshot(UiExclusionsFile? uiOverride = null)
    {
        var config = addonOptions.CurrentValue;
        var ui = uiOverride ?? LoadUiFile();
        var configEntities = NormalizePatterns(config.AnalysisExcludeEntities);
        var configDomains = NormalizePatterns(config.AnalysisExcludeDomains);

        return new AnalysisExclusionsSnapshot(
            ui.ExcludeEntities,
            ui.ExcludeDomains,
            configEntities,
            configDomains,
            Merge(ui.ExcludeEntities, configEntities),
            Merge(ui.ExcludeDomains, configDomains));
    }

    private UiExclusionsFile LoadUiFile()
    {
        var path = ResolveFilePath();
        if (!File.Exists(path))
        {
            return new UiExclusionsFile();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UiExclusionsFile>(json, JsonOptions) ?? new UiExclusionsFile();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read analysis exclusions from {Path}", path);
            return new UiExclusionsFile();
        }
    }

    private string ResolveFilePath()
    {
        var configured = configuration["AnalysisExclusions:FilePath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (environment.IsProduction())
        {
            return "/data/analysis-exclusions.json";
        }

        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "data", "analysis-exclusions.json"));
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static List<string> NormalizePatterns(IReadOnlyList<string> patterns)
    {
        return patterns
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
    }

    private static List<string> Merge(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        return a.Concat(b)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class UiExclusionsFile
    {
        public List<string> ExcludeEntities { get; set; } = [];

        public List<string> ExcludeDomains { get; set; } = [];

        public DateTimeOffset? UpdatedAtUtc { get; set; }
    }
}

public sealed record AnalysisExclusionsSnapshot(
    IReadOnlyList<string> UiExcludeEntities,
    IReadOnlyList<string> UiExcludeDomains,
    IReadOnlyList<string> ConfigExcludeEntities,
    IReadOnlyList<string> ConfigExcludeDomains,
    IReadOnlyList<string> EffectiveExcludeEntities,
    IReadOnlyList<string> EffectiveExcludeDomains)
{
    public bool HasExclusions =>
        EffectiveExcludeEntities.Count > 0 || EffectiveExcludeDomains.Count > 0;
}
