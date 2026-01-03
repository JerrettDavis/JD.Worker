using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JD.Worker.Core;

public sealed class NoopJobExecutor : IJobExecutor
{
    private readonly ILogger<NoopJobExecutor> _logger;

    public NoopJobExecutor(ILogger<NoopJobExecutor> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(string jobId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Job {JobId} execution is not implemented yet.", jobId);
        return Task.CompletedTask;
    }
}
