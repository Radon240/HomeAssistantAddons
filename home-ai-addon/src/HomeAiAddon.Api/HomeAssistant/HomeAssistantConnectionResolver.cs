using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.HomeAssistant;

/// <summary>
/// Разрешает URL и режим подключения по документации аддонов HA:
/// http://supervisor/core/api и ws://supervisor/core/websocket + SUPERVISOR_TOKEN.
/// </summary>
public sealed class HomeAssistantConnectionResolver(
    IOptionsMonitor<HomeAssistantIntegrationOptions> options,
    IHomeAssistantAccessTokenProvider tokenProvider,
    ILogger<HomeAssistantConnectionResolver> logger)
{
    public static readonly Uri SupervisorRestApiBase = new("http://supervisor/core/api/");
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

        // SUPERVISOR_TOKEN действует только через прокси Supervisor, не на homeassistant:8123.
        if (authSource == "supervisor")
        {
            var custom = options.CurrentValue.BaseUrl?.Trim();
            if (!string.IsNullOrEmpty(custom) &&
                HomeAssistantUriHelper.TryGetHttpOrigin(custom, out var origin) &&
                !IsSupervisorHost(origin))
            {
                logger.LogWarning(
                    "home_assistant_base_url ({BaseUrl}) игнорируется при auth=supervisor. "
                    + "Используется прокси Supervisor. Очистите поле в настройках аддона.",
                    custom);
            }

            endpoints = CreateSupervisorEndpoints(authSource);
            return true;
        }

        var manualBase = options.CurrentValue.BaseUrl?.Trim();
        if (string.IsNullOrEmpty(manualBase))
        {
            return false;
        }

        if (!HomeAssistantUriHelper.TryGetHttpOrigin(manualBase, out var manualOrigin))
        {
            return false;
        }

        if (IsSupervisorHost(manualOrigin))
        {
            endpoints = CreateSupervisorEndpoints(authSource);
            return true;
        }

        var restBase = new Uri(manualOrigin, "/api/");
        endpoints = new HomeAssistantEndpoints(
            restBase,
            HomeAssistantUriHelper.BuildWebSocketUri(manualOrigin),
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
