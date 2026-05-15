using HomeAiAddon.Api.HomeAssistant;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HomeAiAddon.Api.Health;

public sealed class HomeAssistantIntegrationHealthCheck(
    HomeAssistantConnectionResolver connectionResolver,
    HomeAssistantConnectionState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!connectionResolver.TryResolve(out var endpoints))
        {
            return Task.FromResult(
                HealthCheckResult.Degraded(
                    "Нет SUPERVISOR_TOKEN (запустите как аддон с homeassistant_api: true) "
                    + "или HOME_ASSISTANT_ACCESS_TOKEN для локальной отладки."));
        }

        if (state.IsWebSocketConnected)
        {
            var via = endpoints.UsesSupervisorProxy ? "Supervisor" : "прямое подключение";
            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"WebSocket Home Assistant подключён ({via}, auth: {endpoints.AuthSource})."));
        }

        return Task.FromResult(
            HealthCheckResult.Degraded(
                "Параметры доступа есть, но WebSocket сейчас не подключён (повторное подключение или ошибка)."));
    }
}
