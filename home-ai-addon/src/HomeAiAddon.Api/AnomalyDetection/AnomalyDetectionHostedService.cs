using Microsoft.Extensions.Options;
using HomeAiAddon.Api.Options;

namespace HomeAiAddon.Api.AnomalyDetection;

public sealed class AnomalyDetectionHostedService(
    IServiceProvider serviceProvider,
    IOptions<AnomalyDetectionOptions> options,
    ILogger<AnomalyDetectionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(options.Value.IntervalMinutes, 5, 1440));
        logger.LogInformation(
            "Anomaly detection hosted service started (interval {IntervalMinutes} min)",
            interval.TotalMinutes);

        using var timer = new PeriodicTimer(interval);
        await RunCycleAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<AnomalyDetectionService>();
            var result = await service.RunDetectionAsync(cancellationToken);

            if (result.Message is not null)
            {
                logger.LogInformation("Anomaly detection cycle: {Message}", result.Message);
            }
            else
            {
                logger.LogInformation(
                    "Anomaly detection cycle complete: analyzed={Analyzed}, persisted={Persisted}",
                    result.AnalyzedEventCount,
                    result.PersistedCount);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Anomaly detection cycle failed");
        }
    }
}
