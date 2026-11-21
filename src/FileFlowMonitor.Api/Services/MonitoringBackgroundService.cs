using FileFlowMonitor.Infrastructure.Monitoring;

namespace FileFlowMonitor.Api.Services;

/// <summary>
/// Runs monitoring cycles independently from HTTP requests. A fresh scope is
/// created for each cycle so EF Core contexts are short-lived.
/// </summary>
public sealed class MonitoringBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MonitoringBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Clamp(
            configuration.GetValue<int?>("Monitoring:PollIntervalSeconds") ?? 30, 5, 3600);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var cycle = scope.ServiceProvider.GetRequiredService<MonitoringEngineCycle>();
                await cycle.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error while running a monitoring cycle.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
