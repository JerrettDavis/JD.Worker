using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class DockerStepRunner : IStepRunner
{
    private const string ContainerWorkspacePath = "/workspace";

    public IReadOnlySet<StepType> SupportedTypes { get; } = new HashSet<StepType>
    {
        StepType.Docker
    };

    public async Task<IStepResult> ExecuteAsync(
        IStepDefinition step,
        IWorkspaceContext context,
        CancellationToken cancellationToken)
    {
        var image = Normalize(step.Image);
        var buildRequired = RequiresBuild(step);
        var removeImageAfterRun = false;

        if (buildRequired)
        {
            if (string.IsNullOrWhiteSpace(image))
            {
                image = BuildTemporaryImageTag(context, step);
                removeImageAfterRun = true;
            }

            var buildResult = await BuildImageAsync(step, context, image, cancellationToken);
            if (!buildResult.Success)
            {
                return buildResult;
            }
        }

        if (string.IsNullOrWhiteSpace(image))
        {
            return new StepResult(
                step.Name,
                false,
                null,
                "Docker step requires an image or Dockerfile.",
                TimeSpan.Zero,
                null);
        }

        await context.LogSink.WriteAsync(
            LogLevel.Information,
            $"Starting docker run for image {image}.",
            cancellationToken);

        var runInfo = CreateRunStartInfo(step, context, image);
        var runResult = await ShellStepRunner.RunProcessAsync(
            step,
            context,
            runInfo,
            cancellationToken);

        if (removeImageAfterRun)
        {
            await TryRemoveImageAsync(step, image, context, cancellationToken);
        }

        return runResult;
    }

    private static bool RequiresBuild(IStepDefinition step) =>
        !string.IsNullOrWhiteSpace(step.Dockerfile) ||
        !string.IsNullOrWhiteSpace(step.BuildContext) ||
        step.BuildArgs is { Count: > 0 };

    private static async Task<IStepResult> BuildImageAsync(
        IStepDefinition step,
        IWorkspaceContext context,
        string image,
        CancellationToken cancellationToken)
    {
        var buildContext = ResolveBuildContext(step, context);
        if (string.IsNullOrWhiteSpace(buildContext) || !Directory.Exists(buildContext))
        {
            return new StepResult(
                step.Name,
                false,
                null,
                $"Docker build context not found: {buildContext ?? "unknown"}.",
                TimeSpan.Zero,
                null);
        }

        var dockerfile = ResolveDockerfile(step, buildContext);
        if (!string.IsNullOrWhiteSpace(dockerfile) && !File.Exists(dockerfile))
        {
            return new StepResult(
                step.Name,
                false,
                null,
                $"Dockerfile not found: {dockerfile}.",
                TimeSpan.Zero,
                null);
        }

        await context.LogSink.WriteAsync(
            LogLevel.Information,
            $"Building docker image {image}.",
            cancellationToken);

        var startInfo = CreateBuildStartInfo(image, buildContext, dockerfile, step.BuildArgs);
        return await ShellStepRunner.RunProcessAsync(step, context, startInfo, cancellationToken);
    }

    private static ProcessStartInfo CreateBuildStartInfo(
        string image,
        string buildContext,
        string? dockerfile,
        IReadOnlyDictionary<string, string>? buildArgs)
    {
        var startInfo = CreateDockerStartInfo(buildContext);
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(image);

        if (!string.IsNullOrWhiteSpace(dockerfile))
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(dockerfile);
        }

        if (buildArgs is not null)
        {
            foreach (var entry in buildArgs)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                startInfo.ArgumentList.Add("--build-arg");
                startInfo.ArgumentList.Add($"{entry.Key}={entry.Value}");
            }
        }

        startInfo.ArgumentList.Add(buildContext);
        return startInfo;
    }

    private static ProcessStartInfo CreateRunStartInfo(
        IStepDefinition step,
        IWorkspaceContext context,
        string image)
    {
        var startInfo = CreateDockerStartInfo(context.WorkDir);
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--rm");

        if (step.RunArgs is not null)
        {
            foreach (var arg in step.RunArgs.Where(arg => !string.IsNullOrWhiteSpace(arg)))
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        var mountPath = BuildMountPath(context.WorkDir);
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add($"{mountPath}:{ContainerWorkspacePath}");

        var containerWorkDir = BuildContainerWorkDir(step);
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add(containerWorkDir);

        if (step.Environment is not null)
        {
            foreach (var entry in step.Environment)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add($"{entry.Key}={entry.Value}");
            }
        }

        startInfo.ArgumentList.Add(image);

        foreach (var segment in BuildCommandSegments(step))
        {
            startInfo.ArgumentList.Add(segment);
        }

        return startInfo;
    }

    private static ProcessStartInfo CreateDockerStartInfo(string workingDirectory) => new()
    {
        FileName = "docker",
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    private static string BuildContainerWorkDir(IStepDefinition step)
    {
        if (string.IsNullOrWhiteSpace(step.WorkingDirectory))
        {
            return ContainerWorkspacePath;
        }

        var trimmed = step.WorkingDirectory.Trim();
        trimmed = trimmed.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        trimmed = trimmed.Replace('\\', '/');
        return $"{ContainerWorkspacePath}/{trimmed}";
    }

    private static string BuildMountPath(string workspacePath)
    {
        var fullPath = Path.GetFullPath(workspacePath);
        if (OperatingSystem.IsWindows())
        {
            fullPath = fullPath.Replace('\\', '/');
        }

        return fullPath;
    }

    private static IEnumerable<string> BuildCommandSegments(IStepDefinition step)
    {
        var command = string.IsNullOrWhiteSpace(step.Command) ? null : step.Command.Trim();
        var args = step.Arguments?
            .Where(arg => !string.IsNullOrWhiteSpace(arg))
            .Select(arg => arg.Trim())
            .ToList();

        if (string.IsNullOrWhiteSpace(command) && (args is null || args.Count == 0))
        {
            return Array.Empty<string>();
        }

        var commandLine = BuildCommandLine(command, args);
        var requiresShell = string.IsNullOrWhiteSpace(command) || command.Contains(' ') || (args is not null && args.Count > 0);

        if (!requiresShell)
        {
            var segments = new List<string> { command! };
            if (args is not null)
            {
                segments.AddRange(args);
            }

            return segments;
        }

        return new[] { "/bin/sh", "-c", commandLine };
    }

    private static string BuildCommandLine(string? command, List<string>? args)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return args is null ? string.Empty : string.Join(' ', args);
        }

        if (args is null || args.Count == 0)
        {
            return command;
        }

        return $"{command} {string.Join(' ', args)}";
    }

    private static string ResolveBuildContext(IStepDefinition step, IWorkspaceContext context)
    {
        if (string.IsNullOrWhiteSpace(step.BuildContext))
        {
            return context.WorkDir;
        }

        var buildContext = step.BuildContext.Trim();
        if (!Path.IsPathRooted(buildContext))
        {
            buildContext = Path.Combine(context.WorkDir, buildContext);
        }

        return Path.GetFullPath(buildContext);
    }

    private static string? ResolveDockerfile(IStepDefinition step, string buildContext)
    {
        if (string.IsNullOrWhiteSpace(step.Dockerfile))
        {
            return null;
        }

        var dockerfile = step.Dockerfile.Trim();
        if (!Path.IsPathRooted(dockerfile))
        {
            dockerfile = Path.Combine(buildContext, dockerfile);
        }

        return Path.GetFullPath(dockerfile);
    }

    private static string BuildTemporaryImageTag(IWorkspaceContext context, IStepDefinition step)
    {
        var name = string.IsNullOrWhiteSpace(step.Name) ? "step" : step.Name;
        var jobSegment = SanitizeTagSegment(context.JobId, "job");
        var stepSegment = SanitizeTagSegment(name, "step");
        return $"jd-worker/{jobSegment}-{context.Attempt}-{stepSegment}";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SanitizeTagSegment(string value, string fallback)
    {
        var normalized = new string(value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        normalized = normalized.Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static async Task TryRemoveImageAsync(
        IStepDefinition step,
        string image,
        IWorkspaceContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = CreateDockerStartInfo(context.WorkDir);
            startInfo.ArgumentList.Add("rmi");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(image);
            await ShellStepRunner.RunProcessAsync(
                step,
                context,
                startInfo,
                cancellationToken);
        }
        catch
        {
            await context.LogSink.WriteAsync(
                LogLevel.Warning,
                $"Failed to remove docker image {image}.",
                cancellationToken);
        }
    }
}
