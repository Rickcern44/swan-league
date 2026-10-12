using Swan.Services;

namespace Swan.Worker;

public static class WorkerRuntime
{
    public static bool IsRunOnce(string[] args) => args.Contains("--once", StringComparer.Ordinal);

    public static async Task<bool> TryRunOnceAsync(
        string[] args,
        IRunDueJobsHandler runDueJobsHandler,
        CancellationToken cancellationToken)
    {
        if (!IsRunOnce(args))
        {
            return false;
        }

        await runDueJobsHandler.RunAsync(cancellationToken);
        return true;
    }
}
