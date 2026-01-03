using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using JD.Worker.Core;
using JD.Worker.Orchestrator.ApiService;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddWorkerCore();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "JD.Worker Orchestrator API is running.");

app.MapGet("/v1/workers", async (IWorkerRegistry registry, CancellationToken ct) =>
    await registry.ListAsync(ct));

app.MapPost("/v1/workers/heartbeat", async (WorkerRegistration registration, IWorkerRegistry registry, CancellationToken ct) =>
{
    var updated = registration with { LastHeartbeatUtc = DateTimeOffset.UtcNow };
    await registry.RegisterAsync(updated, ct);
    return Results.Accepted();
});

app.MapGet("/v1/jobs", async (string? state, IJobService jobs, CancellationToken ct) =>
{
    JobState? parsedState = null;
    if (!string.IsNullOrWhiteSpace(state))
    {
        if (!Enum.TryParse(state, true, out JobState parsed))
        {
            return Results.BadRequest($"Unknown state '{state}'.");
        }

        parsedState = parsed;
    }

    var results = await jobs.ListAsync(parsedState, ct);
    return Results.Ok(results);
});

app.MapGet("/v1/jobs/available", async (int? limit, IJobStore store, CancellationToken ct) =>
{
    var jobs = await store.ListAsync(JobState.Accepted, ct);
    if (limit.HasValue && limit.Value > 0)
    {
        jobs = jobs.Take(limit.Value).ToList();
    }

    var envelopes = jobs.Select(job => job.Envelope).ToList();
    return envelopes.Count == 0 ? Results.NoContent() : Results.Ok(envelopes);
});

app.MapGet("/v1/jobs/{jobId}", async (string jobId, IJobStore store, CancellationToken ct) =>
{
    var job = await store.GetAsync(jobId, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapPost("/v1/jobs", async (JobEnvelope envelope, IJobService jobs, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(envelope.Envelope.JobId))
    {
        return Results.BadRequest("JobId is required.");
    }

    var record = await jobs.SubmitAsync(envelope, source: null, ct);
    return Results.Created($"/v1/jobs/{record.JobId}", record);
});

app.MapPost("/v1/jobs/{jobId}/logs", async (
    string jobId,
    JobLogEntry entry,
    ILogStore logs,
    CancellationToken ct) =>
{
    var timestamp = entry.TimestampUtc == default ? DateTimeOffset.UtcNow : entry.TimestampUtc;
    var normalized = entry with { JobId = jobId, TimestampUtc = timestamp };
    await logs.AppendAsync(normalized, ct);
    return Results.Accepted();
});

app.MapGet("/v1/jobs/{jobId}/logs", async (
    string jobId,
    int? take,
    DateTimeOffset? since,
    ILogStore logs,
    CancellationToken ct) =>
{
    var query = new LogQuery(jobId, WorkerId: null, SinceUtc: since, Take: take);
    var results = await logs.QueryAsync(query, ct);
    return results.Count == 0 ? Results.NoContent() : Results.Ok(results);
});

app.MapGet("/v1/logs", async (
    string? jobId,
    string? workerId,
    int? take,
    DateTimeOffset? since,
    ILogStore logs,
    CancellationToken ct) =>
{
    var query = new LogQuery(jobId, workerId, since, take);
    var results = await logs.QueryAsync(query, ct);
    return results.Count == 0 ? Results.NoContent() : Results.Ok(results);
});

app.MapPost("/v1/jobs/{jobId}/lease", async (
    string jobId,
    LeaseRequest request,
    IJobStore store,
    JobLeaseService leases,
    CancellationToken ct) =>
{
    var job = await store.GetAsync(jobId, ct);
    if (job is null)
    {
        return Results.NotFound();
    }

    var leaseSeconds = request.LeaseSeconds > 0 ? request.LeaseSeconds : 300;
    var lease = await leases.TryAcquireAsync(jobId, TimeSpan.FromSeconds(leaseSeconds), ct);
    return lease is null ? Results.Conflict() : Results.Ok(lease);
});

app.MapPost("/v1/jobs/{jobId}/lease/renew", async (
    string jobId,
    LeaseRenewRequest request,
    JobLeaseService leases,
    CancellationToken ct) =>
{
    var extensionSeconds = request.ExtensionSeconds > 0 ? request.ExtensionSeconds : 60;
    var updated = await leases.RenewAsync(
        new LeaseHandle(jobId, request.LeaseId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        TimeSpan.FromSeconds(extensionSeconds),
        ct);
    return updated is null ? Results.Conflict() : Results.Ok(updated);
});

app.MapPost("/v1/jobs/{jobId}/lease/release", async (
    string jobId,
    LeaseReleaseRequest request,
    JobLeaseService leases,
    CancellationToken ct) =>
{
    var released = await leases.ReleaseAsync(
        new LeaseHandle(jobId, request.LeaseId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        ct);
    return released ? Results.Ok() : Results.Conflict();
});

app.MapPost("/v1/jobs/{jobId}/start", async (
    string jobId,
    JobStartRequest request,
    IJobStore store,
    JobStateService states,
    CancellationToken ct) =>
{
    var job = await store.GetAsync(jobId, ct);
    if (job is null)
    {
        return Results.NotFound();
    }

    if (job.State == JobState.Running)
    {
        return Results.Ok(job);
    }

    if (job.State != JobState.Leased)
    {
        return Results.Conflict();
    }

    var message = string.IsNullOrWhiteSpace(request.WorkerId)
        ? "Execution started."
        : $"Execution started on worker {request.WorkerId}.";
    var updated = await states.TransitionAsync(job, JobState.Running, message, ct);
    return updated is null ? Results.Conflict() : Results.Ok(updated);
});

app.MapPost("/v1/jobs/{jobId}/ack", async (
    string jobId,
    JobStateService states,
    JobLeaseService leases,
    CancellationToken ct) =>
{
    var updated = await states.MoveToAsync(jobId, JobState.Succeeded, "Job completed via HTTP.", ct);
    if (updated is null)
    {
        return Results.Conflict();
    }

    await states.TransitionAsync(updated, JobState.Finalized, "Job finalized.", ct);
    await leases.ClearAsync(jobId, ct);
    return Results.Ok(updated);
});

app.MapPost("/v1/jobs/{jobId}/nack", async (
    string jobId,
    NegativeAckRequest request,
    JobStateService states,
    JobLeaseService leases,
    CancellationToken ct) =>
{
    JobRecord? updated;
    if (request.Requeue)
    {
        updated = await states.TransitionAsync(jobId, JobState.Accepted, "Job requeued.", ct);
    }
    else
    {
        updated = await states.MoveToAsync(jobId, JobState.Failed, "Job failed via HTTP.", ct);
        if (updated is not null)
        {
            updated = await states.TransitionAsync(updated, JobState.Finalized, "Job finalized.", ct);
        }
    }

    await leases.ClearAsync(jobId, ct);
    return updated is null ? Results.Conflict() : Results.Ok(updated);
});

app.MapDefaultEndpoints();

app.Run();
