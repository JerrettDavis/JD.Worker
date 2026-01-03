using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class ShellStepRunner : IStepRunner
{
    public IReadOnlySet<StepType> SupportedTypes { get; } = new HashSet<StepType> { StepType.Shell };

    public async Task<IStepResult> ExecuteAsync(
        IStepDefinition step,
        IWorkspaceContext context,
        CancellationToken cancellationToken)
    {
        var (fileName, args) = BuildShellCommand(step.Command);
        var startInfo = CreateStartInfo(fileName, args, step, context);
        return await RunProcessAsync(step, context, startInfo, cancellationToken);
    }

    private static (string fileName, string args) BuildShellCommand(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("cmd.exe", $"/c {command}");
        }

        return ("/bin/bash", $"-c \"{command}\"");
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        string args,
        IStepDefinition step,
        IWorkspaceContext context)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = ResolveWorkingDirectory(step, context),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ApplyEnvironment(startInfo, step);
        return startInfo;
    }

    internal static void ApplyEnvironment(ProcessStartInfo startInfo, IStepDefinition step)
    {
        if (step.Environment is null)
        {
            return;
        }

        foreach (var entry in step.Environment)
        {
            startInfo.Environment[entry.Key] = entry.Value;
        }
    }

    internal static string ResolveWorkingDirectory(IStepDefinition step, IWorkspaceContext context)
    {
        if (string.IsNullOrWhiteSpace(step.WorkingDirectory))
        {
            return context.WorkDir;
        }

        return Path.Combine(context.WorkDir, step.WorkingDirectory);
    }

    internal static async Task<IStepResult> RunProcessAsync(
        IStepDefinition step,
        IWorkspaceContext context,
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;
        using var timeoutCts = CreateTimeoutToken(step.Timeout, cancellationToken);
        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return new StepResult(step.Name, false, null, "Process failed to start.", TimeSpan.Zero, null);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                await context.LogSink.WriteStdOutAsync(stdout, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                await context.LogSink.WriteStdErrAsync(stderr, cancellationToken);
            }

            var duration = DateTimeOffset.UtcNow - start;
            var success = process.ExitCode == 0;
            var error = success ? null : $"Exit code {process.ExitCode}.";

            return new StepResult(step.Name, success, process.ExitCode, error, duration, null);
        }
        catch (OperationCanceledException)
        {
            return new StepResult(step.Name, false, null, "Step canceled or timed out.", DateTimeOffset.UtcNow - start, null);
        }
        catch (Exception ex)
        {
            return new StepResult(step.Name, false, null, ex.Message, DateTimeOffset.UtcNow - start, null);
        }
    }

    private static CancellationTokenSource CreateTimeoutToken(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout.HasValue)
        {
            cts.CancelAfter(timeout.Value);
        }

        return cts;
    }
}
