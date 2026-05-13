using HomeAiAddon.Api.HomeAssistant;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.Health;

public sealed class HomeAssistantIntegrationHealthCheck(
    IOptionsMonitor<HomeAssistantIntegrationOptions> options,
    IHomeAssistantAccessTokenProvider tokens,
    HomeAssistantConnectionState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var baseConfigured = HomeAssistantUriHelper.TryGetHttpOrigin(options.CurrentValue.BaseUrl, out _);
        var tokenConfigured = !string.IsNullOrEmpty(tokens.GetAccessToken());

        if (!baseConfigured && !tokenConfigured)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy("Интеграция Home Assistant не настроена (URL и токен не заданы)."));
        }

        if (!baseConfigured || !tokenConfigured)
        {
            return Task.FromResult(
                HealthCheckResult.Degraded(
                    "Заданы не все параметры интеграции Home Assistant (нужны BaseUrl и переменная HOME_ASSISTANT_ACCESS_TOKEN)."));
        }

        if (state.IsWebSocketConnected)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy("WebSocket Home Assistant подключён, подписка на state_changed активна."));
        }

        return Task.FromResult(
            HealthCheckResult.Degraded(
                "Параметры заданы, но WebSocket сейчас не подключён (повторное подключение или ошибка)."));
    }
}
