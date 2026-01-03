using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class ProcessStepRunner : IStepRunner
{
    public IReadOnlySet<StepType> SupportedTypes { get; } = new HashSet<StepType> { StepType.Process };

    public Task<IStepResult> ExecuteAsync(
        IStepDefinition step,
        IWorkspaceContext context,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = step.Command,
            WorkingDirectory = ShellStepRunner.ResolveWorkingDirectory(step, context),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (step.Arguments is { Count: > 0 })
        {
            foreach (var arg in step.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        ShellStepRunner.ApplyEnvironment(startInfo, step);
        return ShellStepRunner.RunProcessAsync(step, context, startInfo, cancellationToken);
    }
}
