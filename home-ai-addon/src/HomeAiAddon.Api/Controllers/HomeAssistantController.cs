using HomeAiAddon.Api.HomeAssistant;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/homeassistant")]
public sealed class HomeAssistantController(
    HomeAssistantConnectionState connectionState,
    IOptionsMonitor<HomeAssistantIntegrationOptions> options,
    IHomeAssistantAccessTokenProvider tokens) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<HomeAssistantStatusResponse> GetStatus()
    {
        var baseConfigured = HomeAssistantUriHelper.TryGetHttpOrigin(options.CurrentValue.BaseUrl, out _);
        var tokenConfigured = !string.IsNullOrEmpty(tokens.GetAccessToken());
        var snapshot = connectionState.GetSnapshot(baseConfigured, tokenConfigured);

        var recent = snapshot.RecentStateChanges.Select(e => new HomeAssistantStateChangeDto(
            e.EntityId,
            e.NewState,
            e.OldState,
            e.FriendlyName,
            e.TimeFiredUtc,
            e.ReceivedAtUtc)).ToList();

        return Ok(new HomeAssistantStatusResponse(
            snapshot.IntegrationConfigured,
            snapshot.BaseUrlConfigured,
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
    bool BaseUrlConfigured,
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
