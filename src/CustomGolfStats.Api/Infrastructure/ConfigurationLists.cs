namespace CustomGolfStats.Api.Infrastructure;

public static class ConfigurationLists
{
    /// <summary>
    /// Lets a list setting come as one comma-separated value (e.g. the environment variable
    /// <c>Auth__Apple__ClientIds=com.example.app,com.example.web</c> from deploy/compose.yaml) as well as indexed
    /// children (<c>Auth:Apple:ClientIds:0</c>, as in appsettings arrays). An empty value becomes an empty list.
    /// </summary>
    public static void ExpandCommaSeparatedList(this ConfigurationManager configuration, string key)
    {
        var section = configuration.GetSection(key);
        if (section.Value is null || section.GetChildren().Any())
        {
            return;
        }

        var items = section.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expanded = new Dictionary<string, string?> { [key] = null };
        for (var i = 0; i < items.Length; i++)
        {
            expanded[$"{key}:{i}"] = items[i];
        }

        configuration.AddInMemoryCollection(expanded);
    }
}
