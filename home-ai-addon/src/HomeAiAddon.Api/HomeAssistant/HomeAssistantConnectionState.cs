using System.Collections.Immutable;

namespace HomeAiAddon.Api.HomeAssistant;

public sealed class HomeAssistantConnectionState
{
    private readonly object _gate = new();
    private readonly List<NormalizedStateChangedEvent> _recent = new(capacity: 64);
    private long _stateChangeEventsReceived;
    private int _webSocketConnected;
    private string? _lastError;
    private DateTimeOffset? _lastConnectedAtUtc;
    private DateTimeOffset? _lastEventReceivedAtUtc;
    private DateTimeOffset? _lastDisconnectAtUtc;

    public bool IsWebSocketConnected => Volatile.Read(ref _webSocketConnected) != 0;

    public long StateChangeEventsReceived => Interlocked.Read(ref _stateChangeEventsReceived);

    public void SetWebSocketConnected(bool connected)
    {
        Interlocked.Exchange(ref _webSocketConnected, connected ? 1 : 0);
        if (connected)
        {
            lock (_gate)
            {
                _lastConnectedAtUtc = DateTimeOffset.UtcNow;
                _lastError = null;
            }
        }
        else
        {
            lock (_gate)
            {
                _lastDisconnectAtUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    public void RecordError(string? message)
    {
        lock (_gate)
        {
            _lastError = message;
        }
    }

    public void ClearLastError()
    {
        lock (_gate)
        {
            _lastError = null;
        }
    }

    public void AppendStateChange(NormalizedStateChangedEvent evt)
    {
        Interlocked.Increment(ref _stateChangeEventsReceived);
        lock (_gate)
        {
            _lastEventReceivedAtUtc = evt.ReceivedAtUtc;
            _recent.Add(evt);
            const int max = 50;
            if (_recent.Count > max)
            {
                _recent.RemoveRange(0, _recent.Count - max);
            }
        }
    }

    public HomeAssistantIntegrationSnapshot GetSnapshot(bool baseUrlConfigured, bool accessTokenConfigured)
    {
        lock (_gate)
        {
            var integrationConfigured = baseUrlConfigured && accessTokenConfigured;
            return new HomeAssistantIntegrationSnapshot(
                integrationConfigured,
                baseUrlConfigured,
                accessTokenConfigured,
                IsWebSocketConnected,
                Interlocked.Read(ref _stateChangeEventsReceived),
                _lastEventReceivedAtUtc,
                _lastConnectedAtUtc,
                _lastDisconnectAtUtc,
                _lastError,
                _recent.ToImmutableArray());
        }
    }
}

public sealed record HomeAssistantIntegrationSnapshot(
    bool IntegrationConfigured,
    bool BaseUrlConfigured,
    bool AccessTokenConfigured,
    bool WebSocketConnected,
    long StateChangeEventsReceived,
    DateTimeOffset? LastEventReceivedAtUtc,
    DateTimeOffset? LastConnectedAtUtc,
    DateTimeOffset? LastDisconnectAtUtc,
    string? LastError,
    ImmutableArray<NormalizedStateChangedEvent> RecentStateChanges);
