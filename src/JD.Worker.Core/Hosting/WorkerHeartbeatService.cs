using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JD.Worker.Core;

public sealed class WorkerHeartbeatService : BackgroundService
{
    private readonly WorkerConfig _config;
    private readonly WorkerStatusTracker _statusTracker;
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<WorkerHeartbeatService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);
    private HttpClient? _client;

    public WorkerHeartbeatService(
        WorkerConfig config,
        WorkerStatusTracker statusTracker,
        IHttpClientFactory clientFactory,
        ILogger<WorkerHeartbeatService> logger)
    {
        _config = config;
        _statusTracker = statusTracker;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = ResolveApiBaseUrl();
        if (baseUrl is null)
        {
            _logger.LogInformation("No HTTP connector configured; heartbeat disabled.");
            return;
        }

        _client = _clientFactory.CreateClient();
        _client.BaseAddress = baseUrl;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Worker heartbeat failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return;
        }

        var registration = new WorkerRegistration
        {
            WorkerId = _config.Worker.Id,
            Pool = _config.Worker.Pools.FirstOrDefault()?.Name,
            Labels = NormalizeLabels(_config.Worker.Labels),
            Status = _statusTracker.CurrentStatus,
            CurrentJobId = _statusTracker.CurrentJobId,
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        };

        using var content = JsonContent.Create(registration, WorkerRuntimeJsonContext.Default.WorkerRegistration);
        using var response = await _client.PostAsync("v1/workers/heartbeat", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private Uri? ResolveApiBaseUrl()
    {
        var cnc = _config.Cnc.FirstOrDefault(connector =>
            string.Equals(connector.Type, "Http", StringComparison.OrdinalIgnoreCase));
        if (cnc is null)
        {
            return null;
        }

        if (cnc.Settings is null)
        {
            return null;
        }

        var baseUrl = GetSetting(cnc.Settings, "baseUrl") ?? GetSetting(cnc.Settings, "baseUri");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static string? GetSetting(IReadOnlyDictionary<string, object?> settings, string key)
    {
        if (!settings.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value.ToString();
    }

    private static IReadOnlyDictionary<string, string>? NormalizeLabels(Dictionary<string, object>? labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in labels)
        {
            normalized[key] = value?.ToString() ?? string.Empty;
        }

        return normalized;
    }
}
