using HomeAiAddon.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HomeAiAddon.Api.Health;

public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT 1;", cancellationToken);
            return HealthCheckResult.Healthy("SQLite доступен.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Ошибка SQLite.", ex);
        }
    }
}
