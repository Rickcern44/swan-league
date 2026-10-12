using Swan.Services;
using Swan.Worker;
using Xunit;

namespace Swan.Worker.Tests;

public sealed class WorkerRuntimeTests
{
    [Fact]
    public async Task RunOnceExecutesDueJobsAndExits()
    {
        var handler = new RecordingRunDueJobsHandler();

        var ranOnce = await WorkerRuntime.TryRunOnceAsync(["--once"], handler, CancellationToken.None);

        Assert.True(ranOnce);
        Assert.Equal(1, handler.RunCount);
    }

    private sealed class RecordingRunDueJobsHandler : IRunDueJobsHandler
    {
        public int RunCount { get; private set; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            RunCount++;
            return Task.CompletedTask;
        }
    }
}
