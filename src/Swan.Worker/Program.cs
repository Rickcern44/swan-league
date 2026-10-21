using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Swan.Services;
using Swan.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton<IRunDueJobsHandler, QueuedEmailRunDueJobsHandler>();
builder.Services.AddHostedService<DueJobsWorker>();

using var host = builder.Build();

if (!await WorkerRuntime.TryRunOnceAsync(
        args,
        host.Services.GetRequiredService<IRunDueJobsHandler>(),
        CancellationToken.None))
{
    await host.RunAsync();
}

public partial class Program;
