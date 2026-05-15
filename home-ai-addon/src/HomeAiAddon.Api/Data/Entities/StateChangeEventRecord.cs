namespace HomeAiAddon.Api.Data.Entities;

public sealed class StateChangeEventRecord
{
    public long Id { get; set; }

    public string EntityId { get; set; } = string.Empty;

    public string? OldState { get; set; }

    public string? NewState { get; set; }

    public string? FriendlyName { get; set; }

    public DateTimeOffset TimeFiredUtc { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }
}
