namespace Swan.Services;

public interface IRunDueJobsHandler
{
    Task RunAsync(CancellationToken cancellationToken);
}

public sealed class NoOpRunDueJobsHandler : IRunDueJobsHandler
{
    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
