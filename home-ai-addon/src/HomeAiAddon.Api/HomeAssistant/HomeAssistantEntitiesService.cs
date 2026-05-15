using System.Text.Json;

namespace HomeAiAddon.Api.HomeAssistant;

public sealed class HomeAssistantEntitiesService(
    IHttpClientFactory httpClientFactory,
    HomeAssistantConnectionResolver connectionResolver,
    ILogger<HomeAssistantEntitiesService> logger)
{
    public async Task<IReadOnlyList<HomeAssistantEntityDto>> GetEntitiesAsync(CancellationToken cancellationToken = default)
    {
        if (!connectionResolver.TryResolve(out var endpoints))
        {
            return [];
        }

        var statesUri = HomeAssistantUriHelper.CombineRestPath(endpoints.RestApiBase, "states");
        var client = httpClientFactory.CreateClient("HomeAssistant");

        using var response = await client.GetAsync(statesUri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Не удалось получить states: {Status} {Uri}",
                (int)response.StatusCode,
                statesUri);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<HomeAssistantEntityDto>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("entity_id", out var idProp) || idProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var entityId = idProp.GetString() ?? string.Empty;
            string? state = null;
            string? friendlyName = null;

            if (item.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String)
            {
                state = stateProp.GetString();
            }

            if (item.TryGetProperty("attributes", out var attrs) &&
                attrs.ValueKind == JsonValueKind.Object &&
                attrs.TryGetProperty("friendly_name", out var fn) &&
                fn.ValueKind == JsonValueKind.String)
            {
                friendlyName = fn.GetString();
            }

            var domain = entityId.Contains('.', StringComparison.Ordinal)
                ? entityId[..entityId.IndexOf('.')]
                : entityId;

            list.Add(new HomeAssistantEntityDto(entityId, domain, state, friendlyName));
        }

        return list.OrderBy(e => e.EntityId, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed record HomeAssistantEntityDto(
    string EntityId,
    string Domain,
    string? State,
    string? FriendlyName);
