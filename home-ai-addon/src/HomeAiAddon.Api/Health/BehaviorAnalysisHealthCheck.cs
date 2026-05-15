using HomeAiAddon.Api.BehaviorAnalysis;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HomeAiAddon.Api.Health;

public sealed class BehaviorAnalysisHealthCheck(IBehaviorAnalysisClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var healthy = await client.IsHealthyAsync(cancellationToken);
        if (healthy)
        {
            return HealthCheckResult.Healthy("Behavior analysis ML service is reachable.");
        }

        return HealthCheckResult.Degraded("Behavior analysis ML service is not reachable.");
    }
}
