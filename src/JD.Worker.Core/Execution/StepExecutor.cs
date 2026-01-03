using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class StepExecutor
{
    private readonly IReadOnlyList<IStepRunner> _runners;

    public StepExecutor(IEnumerable<IStepRunner> runners)
    {
        _runners = runners.ToList();
    }

    public Task<IStepResult> ExecuteAsync(IStepDefinition step, IWorkspaceContext context, CancellationToken cancellationToken)
    {
        var runner = _runners.FirstOrDefault(r => r.SupportedTypes.Contains(step.Type));
        if (runner is null)
        {
            throw new InvalidOperationException($"No runner registered for step type {step.Type}.");
        }

        return runner.ExecuteAsync(step, context, cancellationToken);
    }
}
