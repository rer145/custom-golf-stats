using System.Diagnostics;
using ApiException = CustomGolfStats.Api.Client.ApiException;

namespace CustomGolfStats.App.Diagnostics;

/// <summary>
/// The app's only log writer. It never writes a token: an API exception is logged by status code and type only (its message
/// carries the response body, which for the auth endpoints contains tokens), and other exceptions by type, message and stack.
/// </summary>
public static class SafeLog
{
    public static void Error(string context, Exception ex) => Write($"[error] {context}: {Describe(ex)}");

    public static void Info(string message) => Write($"[info] {message}");

    /// <summary>A token-free description of an exception and its inner exceptions.</summary>
    public static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            parts.Add(e is ApiException api
                ? $"{e.GetType().Name} HTTP {api.StatusCode}"
                : $"{e.GetType().FullName}: {e.Message}");
        }
        return string.Join(" ---> ", parts) + Environment.NewLine + ex.StackTrace;
    }

    private static void Write(string line)
    {
        Debug.WriteLine(line);
        Console.Error.WriteLine(line);
    }
}
