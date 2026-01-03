using System;
using System.Collections.Generic;

namespace JD.Worker.Connectors.Local;

internal sealed class LocalConnectorOptions
{
    public string Name { get; init; } = "local";
    public string InboxPath { get; init; } = "inbox";
    public string OutboxPath { get; init; } = "outbox";
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public static LocalConnectorOptions FromSettings(IReadOnlyDictionary<string, object?> settings)
    {
        var name = GetString(settings, "name") ?? "local";
        var inboxPath = GetString(settings, "inboxPath") ?? "inbox";
        var outboxPath = GetString(settings, "outboxPath") ?? "outbox";
        var pollInterval = GetTimeSpan(settings, "pollIntervalSeconds", "pollIntervalMs") ?? TimeSpan.FromSeconds(2);

        return new LocalConnectorOptions
        {
            Name = name,
            InboxPath = inboxPath,
            OutboxPath = outboxPath,
            PollInterval = pollInterval
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
