using System.Collections.Generic;
using System.Linq;

namespace JD.Worker.Core;

public sealed class SecretRedactor
{
    private readonly HashSet<string> _secrets = new();

    public void RegisterSecret(string secret)
    {
        if (!string.IsNullOrWhiteSpace(secret))
        {
            _secrets.Add(secret);
        }
    }

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text) || _secrets.Count == 0)
        {
            return text;
        }

        var result = text;
        foreach (var secret in _secrets.OrderByDescending(s => s.Length))
        {
            result = result.Replace(secret, "***REDACTED***", System.StringComparison.Ordinal);
        }

        return result;
    }
}
