using HomeAiAddon.Api.Data;
using HomeAiAddon.Api.HomeAssistant;

namespace HomeAiAddon.Api.BehaviorAnalysis;

public static class AnalyzeEventPayloadFactory
{
    public static async Task<IReadOnlyDictionary<string, HomeAssistantEntityDto>> LoadEntityMetadataAsync(
        HomeAssistantEntitiesService entitiesService,
        CancellationToken cancellationToken)
    {
        var entities = await entitiesService.GetEntitiesAsync(cancellationToken);
        return entities.ToDictionary(e => e.EntityId, StringComparer.OrdinalIgnoreCase);
    }

    public static AnalyzeEventPayload ToPayload(
        StateChangeEventDto e,
        IReadOnlyDictionary<string, HomeAssistantEntityDto> metadata)
    {
        metadata.TryGetValue(e.EntityId, out var meta);
        return new AnalyzeEventPayload(
            e.Id,
            e.EntityId,
            e.OldState,
            e.NewState,
            e.FriendlyName ?? meta?.FriendlyName,
            e.TimeFiredUtc,
            e.ReceivedAtUtc,
            meta?.Domain,
            meta?.DeviceClass,
            meta?.UnitOfMeasurement,
            meta?.EntityCategory,
            meta?.SupportedFeatures,
            meta?.AreaId,
            meta?.AreaName);
    }
}
