using Microsoft.Extensions.Hosting;
using Swan.Services;

namespace Swan.Worker;

public sealed class DueJobsWorker(IRunDueJobsHandler runDueJobsHandler) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await runDueJobsHandler.RunAsync(stoppingToken);
            await Task.Delay(PollingInterval, stoppingToken);
        }
    }
}
