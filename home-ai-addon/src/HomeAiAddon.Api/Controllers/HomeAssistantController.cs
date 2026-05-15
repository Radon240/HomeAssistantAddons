using HomeAiAddon.Api.HomeAssistant;
using Microsoft.AspNetCore.Mvc;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/homeassistant")]
public sealed class HomeAssistantController(
    HomeAssistantConnectionState connectionState,
    HomeAssistantConnectionResolver connectionResolver) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<HomeAssistantStatusResponse> GetStatus()
    {
        var resolved = connectionResolver.TryResolve(out var endpoints);
        var authSource = resolved ? endpoints.AuthSource : "none";
        var usesSupervisor = resolved && endpoints.UsesSupervisorProxy;
        var snapshot = connectionState.GetSnapshot(
            integrationReady: resolved,
            usesSupervisorProxy: usesSupervisor,
            authSource: authSource);

        var recent = snapshot.RecentStateChanges.Select(e => new HomeAssistantStateChangeDto(
            e.EntityId,
            e.NewState,
            e.OldState,
            e.FriendlyName,
            e.TimeFiredUtc,
            e.ReceivedAtUtc)).ToList();

        return Ok(new HomeAssistantStatusResponse(
            snapshot.IntegrationConfigured,
            usesSupervisor,
            authSource,
            snapshot.AccessTokenConfigured,
            snapshot.WebSocketConnected,
            snapshot.StateChangeEventsReceived,
            snapshot.LastEventReceivedAtUtc,
            snapshot.LastConnectedAtUtc,
            snapshot.LastDisconnectAtUtc,
            snapshot.LastError,
            recent));
    }
}

public sealed record HomeAssistantStatusResponse(
    bool IntegrationConfigured,
    bool UsesSupervisorProxy,
    string AuthSource,
    bool AccessTokenConfigured,
    bool WebSocketConnected,
    long StateChangeEventsReceived,
    DateTimeOffset? LastEventReceivedAtUtc,
    DateTimeOffset? LastConnectedAtUtc,
    DateTimeOffset? LastDisconnectAtUtc,
    string? LastError,
    IReadOnlyList<HomeAssistantStateChangeDto> RecentStateChanges);

public sealed record HomeAssistantStateChangeDto(
    string EntityId,
    string? NewState,
    string? OldState,
    string? FriendlyName,
    DateTimeOffset TimeFiredUtc,
    DateTimeOffset ReceivedAtUtc);
