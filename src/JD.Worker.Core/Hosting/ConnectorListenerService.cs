using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JD.Worker.Core;

public sealed class ConnectorListenerService : BackgroundService
{
    private readonly WorkerConfig _config;
    private readonly IEnumerable<IConnectorFactory> _factories;
    private readonly IJobService _jobService;
    private readonly IConnectorRegistry _registry;
    private readonly ILogger<ConnectorListenerService> _logger;
    private readonly List<ICncConnector> _connectors = new();

    public ConnectorListenerService(
        WorkerConfig config,
        IEnumerable<IConnectorFactory> factories,
        IJobService jobService,
        IConnectorRegistry registry,
        ILogger<ConnectorListenerService> logger)
    {
        _config = config;
        _factories = factories;
        _jobService = jobService;
        _registry = registry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var cnc in _config.Cnc)
        {
            var factory = _factories.FirstOrDefault(f =>
                string.Equals(f.TypeName, cnc.Type, StringComparison.OrdinalIgnoreCase));
            if (factory is null)
            {
                _logger.LogWarning("No connector factory registered for type {Type}.", cnc.Type);
                continue;
            }

            var settings = new Dictionary<string, object?>(cnc.Settings ?? new Dictionary<string, object?>())
            {
                ["name"] = cnc.Name
            };
            var connector = factory.Create(settings);
            _connectors.Add(connector);
            _registry.Register(connector);

            if (connector.Capabilities.SupportsPush)
            {
                _ = connector.StartListeningAsync(
                    envelope => HandleEnvelopeAsync(connector, envelope, stoppingToken),
                    stoppingToken);
            }

            if (connector.Capabilities.SupportsPull)
            {
                _ = Task.Run(() => PollAsync(connector, stoppingToken), stoppingToken);
            }
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var connector in _connectors)
        {
            await connector.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task PollAsync(ICncConnector connector, CancellationToken stoppingToken)
    {
        await foreach (var envelope in connector.PollAsync(stoppingToken))
        {
            await HandleEnvelopeAsync(connector, envelope, stoppingToken);
        }
    }

    private async Task HandleEnvelopeAsync(ICncConnector connector, IJobEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope is not JobEnvelope typedEnvelope)
        {
            _logger.LogWarning("Connector {Connector} returned unsupported envelope type {Type}.", connector.Name, envelope.GetType().Name);
            return;
        }

        if (!CanAccept(typedEnvelope, out var rejectionReason))
        {
            _logger.LogInformation(
                "Skipping job {JobId} from connector {Connector}: {Reason}.",
                typedEnvelope.Envelope.JobId,
                connector.Name,
                rejectionReason);

            if (!connector.Capabilities.SupportsLeases)
            {
                await TryRequeueAsync(connector, typedEnvelope.Envelope.JobId, cancellationToken);
            }

            return;
        }

        LeaseHandle? lease = null;
        if (connector.Capabilities.SupportsLeases)
        {
            var leaseDuration = connector.Capabilities.DefaultVisibilityTimeout ?? TimeSpan.FromMinutes(5);
            lease = await connector.TryAcquireLeaseAsync(envelope.JobId, leaseDuration, cancellationToken);
            if (lease is null)
            {
                return;
            }
        }

        try
        {
            await _jobService.SubmitAsync(typedEnvelope, connector.Name, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to submit job {JobId} from connector {Connector}.", typedEnvelope.Envelope.JobId, connector.Name);
            if (lease is not null)
            {
                await TryReleaseLeaseAsync(connector, lease, cancellationToken);
            }
            else
            {
                await TryRequeueAsync(connector, typedEnvelope.Envelope.JobId, cancellationToken);
            }
        }
    }

    private bool CanAccept(JobEnvelope envelope, out string? reason)
    {
        if (!MatchesPool(envelope.Payload.RequestedPool, out reason))
        {
            return false;
        }

        var requirements = envelope.Payload.Requirements;
        if (requirements is null)
        {
            reason = null;
            return true;
        }

        if (requirements.Labels is { Count: > 0 } && !MatchesLabels(requirements.Labels, out reason))
        {
            return false;
        }

        if (!MatchesOs(requirements.Os, out reason))
        {
            return false;
        }

        if (!MatchesArch(requirements.Arch, out reason))
        {
            return false;
        }

        if (!MatchesDockerRequirement(requirements.DockerRequired, out reason))
        {
            return false;
        }

        reason = null;
        return true;
    }

    private bool MatchesPool(string? requestedPool, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(requestedPool))
        {
            reason = null;
            return true;
        }

        if (_config.Worker.Pools.Count == 0)
        {
            if (string.Equals(requestedPool, "default", StringComparison.OrdinalIgnoreCase))
            {
                reason = null;
                return true;
            }

            reason = $"requires pool '{requestedPool}'";
            return false;
        }

        if (_config.Worker.Pools.Any(pool => string.Equals(pool.Name, requestedPool, StringComparison.OrdinalIgnoreCase)))
        {
            reason = null;
            return true;
        }

        reason = $"requires pool '{requestedPool}'";
        return false;
    }

    private bool MatchesLabels(IReadOnlyList<string> requiredLabels, out string? reason)
    {
        var labels = _config.Worker.Labels;
        if (labels is null || labels.Count == 0)
        {
            reason = "requires labels";
            return false;
        }

        foreach (var label in requiredLabels)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var (key, expected) = ParseLabel(label);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!TryGetLabelValue(labels, key, out var actual))
            {
                reason = $"missing label '{key}'";
                return false;
            }

            if (expected is not null && !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"label '{key}' does not match '{expected}'";
                return false;
            }
        }

        reason = null;
        return true;
    }

    private static (string key, string? expected) ParseLabel(string raw)
    {
        var trimmed = raw.Trim();
        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex < 0)
        {
            separatorIndex = trimmed.IndexOf(':');
        }

        if (separatorIndex <= 0)
        {
            return (trimmed, null);
        }

        var key = trimmed[..separatorIndex].Trim();
        var expected = trimmed[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(expected))
        {
            expected = null;
        }

        return (key, expected);
    }

    private static bool TryGetLabelValue(IReadOnlyDictionary<string, object> labels, string key, out string? value)
    {
        if (labels.TryGetValue(key, out var raw))
        {
            value = raw?.ToString();
            return true;
        }

        foreach (var entry in labels)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value?.ToString();
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool MatchesOs(string? requirement, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(requirement))
        {
            reason = null;
            return true;
        }

        var normalized = requirement.Trim().ToLowerInvariant();
        var matches = normalized switch
        {
            "windows" or "win" => RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "linux" => RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "osx" or "mac" or "macos" => RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
            _ => false
        };

        if (!matches)
        {
            reason = $"requires OS '{requirement}'";
        }
        else
        {
            reason = null;
        }

        return matches;
    }

    private static bool MatchesArch(string? requirement, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(requirement))
        {
            reason = null;
            return true;
        }

        var normalized = requirement.Trim().ToLowerInvariant();
        var arch = RuntimeInformation.OSArchitecture;
        var matches = normalized switch
        {
            "x64" or "amd64" or "x86_64" => arch == Architecture.X64,
            "x86" or "i386" or "i686" => arch == Architecture.X86,
            "arm64" or "aarch64" => arch == Architecture.Arm64,
            "arm" or "armv7" => arch == Architecture.Arm,
            _ => false
        };

        if (!matches)
        {
            reason = $"requires arch '{requirement}'";
        }
        else
        {
            reason = null;
        }

        return matches;
    }

    private bool MatchesDockerRequirement(bool required, out string? reason)
    {
        if (!required)
        {
            reason = null;
            return true;
        }

        if (_config.Worker.Sandbox == SandboxMode.Container)
        {
            reason = null;
            return true;
        }

        var labels = _config.Worker.Labels;
        if (labels is not null)
        {
            if (TryGetLabelValue(labels, "execution", out var execution)
                && IsDockerLabel(execution))
            {
                reason = null;
                return true;
            }

            if (TryGetLabelValue(labels, "runtime", out var runtime)
                && IsContainerLabel(runtime))
            {
                reason = null;
                return true;
            }

            if (TryGetLabelValue(labels, "docker", out var docker)
                && IsTruthy(docker))
            {
                reason = null;
                return true;
            }
        }

        reason = "requires docker";
        return false;
    }

    private static bool IsDockerLabel(string? value) =>
        string.Equals(value, "docker", StringComparison.OrdinalIgnoreCase);

    private static bool IsContainerLabel(string? value) =>
        string.Equals(value, "container", StringComparison.OrdinalIgnoreCase);

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

    private async Task TryReleaseLeaseAsync(ICncConnector connector, LeaseHandle lease, CancellationToken cancellationToken)
    {
        try
        {
            await connector.ReleaseLeaseAsync(lease, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release lease for job {JobId} via connector {Connector}.", lease.JobId, connector.Name);
        }
    }

    private async Task TryRequeueAsync(ICncConnector connector, string jobId, CancellationToken cancellationToken)
    {
        if (!connector.Capabilities.SupportsAckNack)
        {
            return;
        }

        try
        {
            await connector.NegativeAcknowledgeAsync(jobId, requeue: true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to requeue job {JobId} via connector {Connector}.", jobId, connector.Name);
        }
    }
}
