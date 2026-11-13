namespace CustomGolfStats.Api.Infrastructure;

/// <summary>Collects validation errors keyed by field (for <c>TypedResults.ValidationProblem</c>); repeated messages for a key are kept once.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool Any => _errors.Count > 0;

    public void Add(string key, string message)
    {
        if (!_errors.TryGetValue(key, out var messages))
        {
            _errors[key] = messages = [];
        }

        if (!messages.Contains(message))
        {
            messages.Add(message);
        }
    }

    public Dictionary<string, string[]> ToDictionary() => _errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);
}
