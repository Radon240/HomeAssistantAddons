namespace HomeAiAddon.Api.HomeAssistant;

public sealed record NormalizedStateChangedEvent(
    string EntityId,
    string? NewState,
    string? OldState,
    string? FriendlyName,
    DateTimeOffset TimeFiredUtc,
    DateTimeOffset ReceivedAtUtc,
    string? ContextId,
    string? ContextUserId,
    string? ContextParentId);
