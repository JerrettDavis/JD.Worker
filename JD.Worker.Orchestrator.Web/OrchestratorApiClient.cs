using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using JD.Worker.Core;

namespace JD.Worker.Orchestrator.Web;

public sealed class OrchestratorApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<WorkerRegistration>> GetWorkersAsync(CancellationToken cancellationToken = default)
    {
        var workers = await httpClient.GetFromJsonAsync(
            "v1/workers",
            OrchestratorApiJsonContext.Default.ListWorkerRegistration,
            cancellationToken);

        return workers ?? new List<WorkerRegistration>();
    }

    public async Task<IReadOnlyList<JobRecord>> GetJobsAsync(JobState? state = null, CancellationToken cancellationToken = default)
    {
        var url = state is null ? "v1/jobs" : $"v1/jobs?state={state}";
        var jobs = await httpClient.GetFromJsonAsync(
            url,
            OrchestratorApiJsonContext.Default.ListJobRecord,
            cancellationToken);

        return jobs ?? new List<JobRecord>();
    }

    public async Task<JobRecord?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"v1/jobs/{jobId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(
            OrchestratorApiJsonContext.Default.JobRecord,
            cancellationToken);
    }

    public async Task<JobRecord> SubmitJobAsync(JobEnvelope envelope, CancellationToken cancellationToken = default)
    {
        using var content = JsonContent.Create(envelope, OrchestratorApiJsonContext.Default.JobEnvelope);
        using var response = await httpClient.PostAsync("v1/jobs", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var job = await response.Content.ReadFromJsonAsync(
            OrchestratorApiJsonContext.Default.JobRecord,
            cancellationToken);

        return job ?? throw new InvalidOperationException("Job submission returned no payload.");
    }

    public async Task<IReadOnlyList<JobLogEntry>> GetJobLogsAsync(
        string jobId,
        int take = 200,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"v1/jobs/{jobId}/logs?take={take}";
        if (since.HasValue)
        {
            url += $"&since={Uri.EscapeDataString(since.Value.ToString("O"))}";
        }

        var logs = await httpClient.GetFromJsonAsync(
            url,
            OrchestratorApiJsonContext.Default.ListJobLogEntry,
            cancellationToken);

        return logs ?? new List<JobLogEntry>();
    }

    public async Task<IReadOnlyList<JobLogEntry>> GetLogsAsync(
        string? jobId = null,
        string? workerId = null,
        int take = 200,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"v1/logs?take={take}";
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            url += $"&jobId={Uri.EscapeDataString(jobId)}";
        }

        if (!string.IsNullOrWhiteSpace(workerId))
        {
            url += $"&workerId={Uri.EscapeDataString(workerId)}";
        }

        if (since.HasValue)
        {
            url += $"&since={Uri.EscapeDataString(since.Value.ToString("O"))}";
        }

        var logs = await httpClient.GetFromJsonAsync(
            url,
            OrchestratorApiJsonContext.Default.ListJobLogEntry,
            cancellationToken);

        return logs ?? new List<JobLogEntry>();
    }
}
