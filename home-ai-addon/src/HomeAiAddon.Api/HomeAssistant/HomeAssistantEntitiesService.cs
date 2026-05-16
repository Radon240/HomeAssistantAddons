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
        var entityRegistry = await LoadEntityRegistryAsync(client, endpoints.RestApiBase, cancellationToken);
        var deviceAreas = await LoadDeviceAreasAsync(client, endpoints.RestApiBase, cancellationToken);
        var areaNames = await LoadAreaNamesAsync(client, endpoints.RestApiBase, cancellationToken);

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
            string? deviceClass = null;
            string? unitOfMeasurement = null;
            string? entityCategory = null;
            long? supportedFeatures = null;

            if (item.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String)
            {
                state = stateProp.GetString();
            }

            if (item.TryGetProperty("attributes", out var attrs) &&
                attrs.ValueKind == JsonValueKind.Object)
            {
                friendlyName = ReadString(attrs, "friendly_name");
                deviceClass = ReadString(attrs, "device_class");
                unitOfMeasurement = ReadString(attrs, "unit_of_measurement");
                entityCategory = ReadString(attrs, "entity_category");
                if (attrs.TryGetProperty("supported_features", out var sf) &&
                    sf.ValueKind == JsonValueKind.Number &&
                    sf.TryGetInt64(out var value))
                {
                    supportedFeatures = value;
                }
            }

            var domain = entityId.Contains('.', StringComparison.Ordinal)
                ? entityId[..entityId.IndexOf('.')]
                : entityId;
            entityRegistry.TryGetValue(entityId, out var registryItem);
            var areaId = registryItem?.AreaId;
            if (string.IsNullOrWhiteSpace(areaId) &&
                !string.IsNullOrWhiteSpace(registryItem?.DeviceId) &&
                deviceAreas.TryGetValue(registryItem.DeviceId, out var deviceAreaId))
            {
                areaId = deviceAreaId;
            }

            var areaName = !string.IsNullOrWhiteSpace(areaId) && areaNames.TryGetValue(areaId, out var name)
                ? name
                : areaId;

            list.Add(new HomeAssistantEntityDto(
                entityId,
                domain,
                state,
                friendlyName,
                deviceClass,
                unitOfMeasurement,
                entityCategory,
                supportedFeatures,
                areaId,
                areaName));
        }

        return list.OrderBy(e => e.EntityId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ReadString(JsonElement attrs, string name)
    {
        return attrs.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private async Task<IReadOnlyDictionary<string, EntityRegistryItem>> LoadEntityRegistryAsync(
        HttpClient client,
        Uri restApiBase,
        CancellationToken cancellationToken)
    {
        var uri = HomeAssistantUriHelper.CombineRestPath(restApiBase, "config/entity_registry");
        using var doc = await TryGetJsonArrayAsync(client, uri, cancellationToken);
        if (doc is null)
        {
            return new Dictionary<string, EntityRegistryItem>();
        }

        var map = new Dictionary<string, EntityRegistryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var entityId = ReadString(item, "entity_id");
            if (string.IsNullOrWhiteSpace(entityId))
            {
                continue;
            }

            map[entityId] = new EntityRegistryItem(ReadString(item, "area_id"), ReadString(item, "device_id"));
        }

        return map;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadDeviceAreasAsync(
        HttpClient client,
        Uri restApiBase,
        CancellationToken cancellationToken)
    {
        var uri = HomeAssistantUriHelper.CombineRestPath(restApiBase, "config/device_registry");
        using var doc = await TryGetJsonArrayAsync(client, uri, cancellationToken);
        if (doc is null)
        {
            return new Dictionary<string, string>();
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var areaId = ReadString(item, "area_id");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(areaId))
            {
                map[id] = areaId;
            }
        }

        return map;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadAreaNamesAsync(
        HttpClient client,
        Uri restApiBase,
        CancellationToken cancellationToken)
    {
        var uri = HomeAssistantUriHelper.CombineRestPath(restApiBase, "config/area_registry");
        using var doc = await TryGetJsonArrayAsync(client, uri, cancellationToken);
        if (doc is null)
        {
            return new Dictionary<string, string>();
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = ReadString(item, "area_id") ?? ReadString(item, "id");
            var name = ReadString(item, "name");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
            {
                map[id] = name;
            }
        }

        return map;
    }

    private async Task<JsonDocument?> TryGetJsonArrayAsync(
        HttpClient client,
        Uri uri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Registry endpoint unavailable: {Status} {Uri}", (int)response.StatusCode, uri);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogDebug(ex, "Registry endpoint failed: {Uri}", uri);
            return null;
        }
    }
}

public sealed record HomeAssistantEntityDto(
    string EntityId,
    string Domain,
    string? State,
    string? FriendlyName,
    string? DeviceClass,
    string? UnitOfMeasurement,
    string? EntityCategory,
    long? SupportedFeatures,
    string? AreaId,
    string? AreaName);

internal sealed record EntityRegistryItem(string? AreaId, string? DeviceId);
