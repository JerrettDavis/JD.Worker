using System;
using System.Collections.Generic;

namespace JD.Worker.Connectors.Http;

internal sealed class HttpConnectorOptions
{
    public string Name { get; init; } = "http";
    public Uri BaseUri { get; init; } = new("http://localhost");
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);
    public int BatchSize { get; init; } = 10;
    public string? WorkerId { get; init; }

    public static HttpConnectorOptions FromSettings(IReadOnlyDictionary<string, object?> settings)
    {
        var name = GetString(settings, "name") ?? "http";
        var baseUrl = GetString(settings, "baseUrl") ?? GetString(settings, "baseUri");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Http connector requires a baseUrl setting.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Invalid baseUrl '{baseUrl}'.");
        }

        var pollInterval = GetTimeSpan(settings, "pollIntervalSeconds", "pollIntervalMs") ?? TimeSpan.FromSeconds(2);
        var leaseDuration = GetTimeSpan(settings, "leaseSeconds", "leaseMs") ?? TimeSpan.FromMinutes(5);
        var batchSize = GetInt(settings, "batchSize") ?? 10;
        var workerId = GetString(settings, "workerId");

        return new HttpConnectorOptions
        {
            Name = name,
            BaseUri = baseUri,
            PollInterval = pollInterval,
            LeaseDuration = leaseDuration,
            BatchSize = batchSize,
            WorkerId = workerId
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> settings, string key)
    {
        if (!settings.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value.ToString();
    }

    private static int? GetInt(IReadOnlyDictionary<string, object?> settings, string key)
    {
        if (!settings.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (value is long longValue)
        {
            return checked((int)longValue);
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static TimeSpan? GetTimeSpan(IReadOnlyDictionary<string, object?> settings, string secondsKey, string millisKey)
    {
        var seconds = GetInt(settings, secondsKey);
        if (seconds.HasValue)
        {
            return TimeSpan.FromSeconds(seconds.Value);
        }

        var millis = GetInt(settings, millisKey);
        if (millis.HasValue)
        {
            return TimeSpan.FromMilliseconds(millis.Value);
        }

        return null;
    }
}
