namespace HomeAiAddon.Api.HomeAssistant;

public sealed class RuntimeMetrics
{
    private long _reconnectCount;
    private long _persistedEvents;
    private long _filteredEvents;

    public long ReconnectCount => Interlocked.Read(ref _reconnectCount);

    public long PersistedEvents => Interlocked.Read(ref _persistedEvents);

    public long FilteredEvents => Interlocked.Read(ref _filteredEvents);

    public void IncrementReconnect() => Interlocked.Increment(ref _reconnectCount);

    public void IncrementPersisted() => Interlocked.Increment(ref _persistedEvents);

    public void IncrementFiltered() => Interlocked.Increment(ref _filteredEvents);
}
