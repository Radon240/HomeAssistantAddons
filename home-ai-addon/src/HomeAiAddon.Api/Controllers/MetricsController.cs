using HomeAiAddon.Api.HomeAssistant;
using Microsoft.AspNetCore.Mvc;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MetricsController(
    RuntimeMetrics runtimeMetrics,
    HomeAssistantConnectionState connectionState) : ControllerBase
{
    [HttpGet]
    public ActionResult<MetricsResponse> Get()
    {
        var lastEvent = connectionState.LastEventReceivedAtUtc;
        var lastEventAgeSeconds = lastEvent.HasValue
            ? (DateTimeOffset.UtcNow - lastEvent.Value).TotalSeconds
            : (double?)null;

        return Ok(new MetricsResponse(
            connectionState.IsWebSocketConnected,
            connectionState.StateChangeEventsReceived,
            runtimeMetrics.PersistedEvents,
            runtimeMetrics.FilteredEvents,
            runtimeMetrics.ReconnectCount,
            lastEvent,
            lastEventAgeSeconds,
            connectionState.LastError));
    }
}

public sealed record MetricsResponse(
    bool WebSocketConnected,
    long EventsReceivedInMemory,
    long EventsPersisted,
    long EventsFiltered,
    long ReconnectCount,
    DateTimeOffset? LastEventReceivedAtUtc,
    double? LastEventAgeSeconds,
    string? LastError);
