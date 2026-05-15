using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.HomeAssistant;

/// <summary>
/// Разрешает URL и режим подключения по документации аддонов HA:
/// http://supervisor/core/api и ws://supervisor/core/websocket + SUPERVISOR_TOKEN.
/// </summary>
public sealed class HomeAssistantConnectionResolver(
    IOptionsMonitor<HomeAssistantIntegrationOptions> options,
    IHomeAssistantAccessTokenProvider tokenProvider)
{
    public static readonly Uri SupervisorRestApiBase = new("http://supervisor/core/api");
    public static readonly Uri SupervisorWebSocketUri = new("ws://supervisor/core/websocket");

    public bool TryResolve(out HomeAssistantEndpoints endpoints)
    {
        endpoints = default!;
        var token = tokenProvider.GetAccessToken();
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var authSource = tokenProvider.GetAuthSource();
        var custom = options.CurrentValue.BaseUrl?.Trim();

        if (string.IsNullOrEmpty(custom))
        {
            endpoints = CreateSupervisorEndpoints(authSource);
            return true;
        }

        if (!HomeAssistantUriHelper.TryGetHttpOrigin(custom, out var origin))
        {
            return false;
        }

        if (IsSupervisorHost(origin))
        {
            endpoints = CreateSupervisorEndpoints(authSource);
            return true;
        }

        var restBase = new Uri(origin, "/api/");
        endpoints = new HomeAssistantEndpoints(
            restBase,
            HomeAssistantUriHelper.BuildWebSocketUri(origin),
            "config",
            authSource,
            UsesSupervisorProxy: false);

        return true;
    }

    private static HomeAssistantEndpoints CreateSupervisorEndpoints(string authSource) =>
        new(
            SupervisorRestApiBase,
            SupervisorWebSocketUri,
            "config",
            authSource,
            UsesSupervisorProxy: true);

    private static bool IsSupervisorHost(Uri origin) =>
        origin.Host.Equals("supervisor", StringComparison.OrdinalIgnoreCase);
}
