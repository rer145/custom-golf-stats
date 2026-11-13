using System.Data.Common;
using System.Text.RegularExpressions;

namespace CustomGolfStats.Migration.Cli;

/// <summary>
/// Removes connection strings and passwords from text before it is shown: the given connection strings, their
/// password values, and anything that looks like <c>Password=...</c> or <c>Pwd=...</c>.
/// </summary>
public sealed partial class SecretRedactor
{
    private const string Mask = "***";

    private readonly List<string> _secrets;

    public SecretRedactor(IEnumerable<string?> connectionStrings)
    {
        var secrets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connectionString in connectionStrings.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            secrets.Add(connectionString!);
            foreach (var password in Passwords(connectionString!))
            {
                secrets.Add(password);
            }
        }

        _secrets = secrets.OrderByDescending(s => s.Length).ToList();
    }

    public string Redact(string text)
    {
        foreach (var secret in _secrets)
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);
        }

        return PasswordSetting().Replace(text, m => m.Groups["key"].Value + Mask);
    }

    /// <summary>Exception type and message, then each inner exception's; no stack trace.</summary>
    public static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (var e = exception; e is not null; e = e.InnerException)
        {
            parts.Add($"{e.GetType().Name}: {e.Message}");
        }

        return string.Join(" ---> ", parts);
    }

    private static IEnumerable<string> Passwords(string connectionString)
    {
        var builder = new DbConnectionStringBuilder();
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            yield break; // not a connection string; the whole value is still masked
        }

        foreach (var key in new[] { "Password", "Pwd" })
        {
            if (builder.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } password)
            {
                yield return password;
            }
        }
    }

    [GeneratedRegex(@"(?<key>\b(?:password|pwd)\s*=\s*)[^;""'\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordSetting();
}
