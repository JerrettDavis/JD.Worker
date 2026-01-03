using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using Microsoft.Data.Sqlite;

namespace JD.Worker.Core;

public sealed class SqliteJobStoreOptions
{
    public string ConnectionString { get; set; } = "Data Source=jdworker.db";
}

public sealed class SqliteJobStore : IJobStore
{
    private readonly SqliteJobStoreOptions _options;
    private bool _initialized;

    public SqliteJobStore(SqliteJobStoreOptions options)
    {
        _options = options;
    }

    public async Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT job_id, attempt, state, envelope_json, source, created_utc, updated_utc FROM jobs WHERE job_id = $jobId";
        command.Parameters.AddWithValue("$jobId", jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadJob(reader);
    }

    public async Task<IReadOnlyList<JobRecord>> ListAsync(JobState? state, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        if (state is null)
        {
            command.CommandText = "SELECT job_id, attempt, state, envelope_json, source, created_utc, updated_utc FROM jobs ORDER BY updated_utc DESC";
        }
        else
        {
            command.CommandText = "SELECT job_id, attempt, state, envelope_json, source, created_utc, updated_utc FROM jobs WHERE state = $state ORDER BY updated_utc DESC";
            command.Parameters.AddWithValue("$state", state.ToString());
        }

        var results = new List<JobRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadJob(reader));
        }

        return results;
    }

    public async Task SaveAsync(JobRecord job, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);

        var envelopeJson = System.Text.Json.JsonSerializer.Serialize(
            job.Envelope,
            ConfigSerializerContext.Default.JobEnvelope);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO jobs (job_id, attempt, state, envelope_json, source, created_utc, updated_utc)
VALUES ($jobId, $attempt, $state, $envelope, $source, $createdUtc, $updatedUtc)
ON CONFLICT(job_id) DO UPDATE SET
    attempt = $attempt,
    state = $state,
    envelope_json = $envelope,
    source = $source,
    updated_utc = $updatedUtc;
""";
        command.Parameters.AddWithValue("$jobId", job.JobId);
        command.Parameters.AddWithValue("$attempt", job.Attempt);
        command.Parameters.AddWithValue("$state", job.State.ToString());
        command.Parameters.AddWithValue("$envelope", envelopeJson);
        command.Parameters.AddWithValue("$source", job.Source ?? string.Empty);
        command.Parameters.AddWithValue("$createdUtc", job.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", job.UpdatedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendEventAsync(JobEvent @event, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO job_events (job_id, attempt, from_state, to_state, timestamp_utc, message)
VALUES ($jobId, $attempt, $fromState, $toState, $timestampUtc, $message);
""";
        command.Parameters.AddWithValue("$jobId", @event.JobId);
        command.Parameters.AddWithValue("$attempt", @event.Attempt);
        command.Parameters.AddWithValue("$fromState", @event.FromState.ToString());
        command.Parameters.AddWithValue("$toState", @event.ToState.ToString());
        command.Parameters.AddWithValue("$timestampUtc", @event.TimestampUtc.ToString("O"));
        command.Parameters.AddWithValue("$message", @event.Message ?? string.Empty);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE IF NOT EXISTS jobs (
    job_id TEXT PRIMARY KEY,
    attempt INTEGER NOT NULL,
    state TEXT NOT NULL,
    envelope_json TEXT NOT NULL,
    source TEXT,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS job_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    job_id TEXT NOT NULL,
    attempt INTEGER NOT NULL,
    from_state TEXT NOT NULL,
    to_state TEXT NOT NULL,
    timestamp_utc TEXT NOT NULL,
    message TEXT
);
""";

        await command.ExecuteNonQueryAsync(cancellationToken);
        _initialized = true;
    }

    private static JobRecord ReadJob(SqliteDataReader reader)
    {
        var jobId = reader.GetString(0);
        var attempt = reader.GetInt32(1);
        var state = Enum.Parse<JobState>(reader.GetString(2));
        var envelopeJson = reader.GetString(3);
        var source = reader.IsDBNull(4) ? null : reader.GetString(4);
        var envelope = System.Text.Json.JsonSerializer.Deserialize(
            envelopeJson,
            ConfigSerializerContext.Default.JobEnvelope) ?? new JobEnvelope();
        var createdUtc = DateTimeOffset.Parse(reader.GetString(5));
        var updatedUtc = DateTimeOffset.Parse(reader.GetString(6));

        return new JobRecord
        {
            JobId = jobId,
            Attempt = attempt,
            State = state,
            Envelope = envelope,
            Source = source,
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc
        };
    }
}
