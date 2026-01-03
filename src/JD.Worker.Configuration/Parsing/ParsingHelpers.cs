using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JD.Worker.Configuration;

internal static class ParsingHelpers
{
    private static readonly Regex EnvVarPattern = new(
        "\\$\\{(?<name>[^}:]+)(:(?<default>[^}]*))?\\}",
        RegexOptions.Compiled);

    private static readonly Regex DurationPattern = new(
        "^(?<value>-?\\d+(\\.\\d+)?)(?<unit>ms|s|m|h|d)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string InterpolateEnvironmentVariables(string content, List<ParseError> errors)
    {
        return EnvVarPattern.Replace(content, match =>
        {
            var name = match.Groups["name"].Value;
            var defaultValue = match.Groups["default"].Success
                ? match.Groups["default"].Value
                : null;

            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value))
            {
                if (defaultValue is not null)
                {
                    return defaultValue;
                }

                var (line, column) = GetLineColumn(content, match.Index);
                errors.Add(new ParseError($"Missing environment variable '{name}'.", line, column));
                return string.Empty;
            }

            return value;
        });
    }

    public static bool TryParseDuration(string value, out TimeSpan result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        var match = DurationPattern.Match(value.Trim());
        if (!match.Success)
        {
            result = default;
            return false;
        }

        var magnitude = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups["unit"].Value.ToLowerInvariant();

        result = unit switch
        {
            "ms" => TimeSpan.FromMilliseconds(magnitude),
            "s" => TimeSpan.FromSeconds(magnitude),
            "m" => TimeSpan.FromMinutes(magnitude),
            "h" => TimeSpan.FromHours(magnitude),
            "d" => TimeSpan.FromDays(magnitude),
            _ => default
        };

        return result != default || magnitude == 0;
    }

    public static bool TryParseNormalizedEnum<TEnum>(string value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalizedValue = NormalizeEnumToken(value);
        foreach (var name in Enum.GetNames(typeof(TEnum)))
        {
            if (string.Equals(normalizedValue, NormalizeEnumToken(name), StringComparison.OrdinalIgnoreCase))
            {
                result = (TEnum)Enum.Parse(typeof(TEnum), name, ignoreCase: true);
                return true;
            }
        }

        return false;
    }

    private static string NormalizeEnumToken(string value)
    {
        return value
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static (int line, int column) GetLineColumn(string text, int index)
    {
        var line = 1;
        var lastLineStart = 0;

        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lastLineStart = i + 1;
            }
        }

        var column = index - lastLineStart + 1;
        return (line, column);
    }
}
