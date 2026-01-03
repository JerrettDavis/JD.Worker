using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using Microsoft.Extensions.Logging;

namespace JD.Worker.Connectors.Http;

public sealed class HttpCncConnector : ICncConnector, IJobStatusReporter, IJobLogPublisher
{
    private readonly HttpConnectorOptions _options;
    private readonly HttpClient _client;
    private readonly ILogger<HttpCncConnector> _logger;
    private readonly ConnectorCapabilities _capabilities;

    internal HttpCncConnector(HttpConnectorOptions options, HttpClient client, ILogger<HttpCncConnector> logger)
    {
        _options = options;
        _client = client;
        _logger = logger;
        _capabilities = new ConnectorCapabilities
        {
            SupportsPull = true,
            SupportsLeases = true,
            SupportsAckNack = true,
            SupportsHeartbeat = true,
            DefaultVisibilityTimeout = _options.LeaseDuration
        };
    }

    public string Name => _options.Name;

    public ConnectorCapabilities Capabilities => _capabilities;

    public async IAsyncEnumerable<IJobEnvelope> PollAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var response = await _client.GetAsync($"v1/jobs/available?limit={_options.BatchSize}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                await Task.Delay(_options.PollInterval, cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
            var envelopes = await response.Content.ReadFromJsonAsync(
                ConfigSerializerContext.Default.ListJobEnvelope,
                cancellationToken);

            if (envelopes is not null)
            {
                foreach (var envelope in envelopes)
                {
                    yield return envelope;
                }
            }

            await Task.Delay(_options.PollInterval, cancellationToken);
        }
    }

    public Task StartListeningAsync(Func<IJobEnvelope, Task> handler, CancellationToken cancellationToken)
    {
        _logger.LogInformation("HTTP connector does not support push listeners.");
        return Task.CompletedTask;
    }

    public async Task<LeaseHandle?> TryAcquireLeaseAsync(string jobId, TimeSpan duration, CancellationToken cancellationToken)
    {
        var request = new LeaseRequest(_options.WorkerId ?? _options.Name, (int)duration.TotalSeconds);
        using var content = JsonContent.Create(request, HttpConnectorSerializerContext.Default.LeaseRequest);
        using var response = await _client.PostAsync($"v1/jobs/{jobId}/lease", content, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(HttpConnectorSerializerContext.Default.LeaseHandle, cancellationToken);
    }

    public async Task RenewLeaseAsync(LeaseHandle lease, TimeSpan extension, CancellationToken cancellationToken)
    {
        var request = new LeaseRenewRequest(lease.LeaseId, (int)extension.TotalSeconds);
        using var content = JsonContent.Create(request, HttpConnectorSerializerContext.Default.LeaseRenewRequest);
        using var response = await _client.PostAsync($"v1/jobs/{lease.JobId}/lease/renew", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReleaseLeaseAsync(LeaseHandle lease, CancellationToken cancellationToken)
    {
        var request = new LeaseReleaseRequest(lease.LeaseId);
        using var content = JsonContent.Create(request, HttpConnectorSerializerContext.Default.LeaseReleaseRequest);
        using var response = await _client.PostAsync($"v1/jobs/{lease.JobId}/lease/release", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task AcknowledgeAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsync($"v1/jobs/{jobId}/ack", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReportJobStartedAsync(string jobId, string? workerId, CancellationToken cancellationToken)
    {
        var request = new JobStartRequest(workerId);
        using var content = JsonContent.Create(request, HttpConnectorSerializerContext.Default.JobStartRequest);
        using var response = await _client.PostAsync($"v1/jobs/{jobId}/start", content, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task NegativeAcknowledgeAsync(string jobId, bool requeue, CancellationToken cancellationToken)
    {
        var request = new NegativeAckRequest(requeue);
        using var content = JsonContent.Create(request, HttpConnectorSerializerContext.Default.NegativeAckRequest);
        using var response = await _client.PostAsync($"v1/jobs/{jobId}/nack", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task PublishLogAsync(JobLogEntry entry, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(entry, HttpConnectorSerializerContext.Default.JobLogEntry);
        using var response = await _client.PostAsync($"v1/jobs/{entry.JobId}/logs", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task PublishResultAsync(IJobResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("HTTP connector result publishing is not implemented yet.");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
