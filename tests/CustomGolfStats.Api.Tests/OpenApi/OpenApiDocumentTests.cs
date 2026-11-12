using System.Text.Json;
using System.Text.Json.Nodes;
using CustomGolfStats.Api.Tests.Infrastructure;

namespace CustomGolfStats.Api.Tests.OpenApi;

public sealed class OpenApiDocumentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    [Fact]
    public async Task Committed_document_matches_the_served_document()
    {
        var committedPath = Path.Combine(AppContext.BaseDirectory, "openapi", "CustomGolfStats.Api.json");
        var committed = Normalise(JsonNode.Parse(await File.ReadAllTextAsync(committedPath, TestContext.Current.CancellationToken)));
        var served = Normalise(JsonNode.Parse(await factory.CreateClient()
            .GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken)));

        // Compare as indented text so a failure shows the differing lines. Rebuild the API project to regenerate the file.
        Assert.Equal(Serialise(committed), Serialise(served));
    }

    [Fact]
    public async Task Every_operation_has_a_unique_operation_id()
    {
        var served = JsonNode.Parse(await factory.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

        var ids = served["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject().Where(item => HttpMethods.Contains(item.Key)))
            .Select(item => item.Value!["operationId"]?.GetValue<string>())
            .ToList();

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Operations_that_need_a_token_declare_the_bearer_scheme()
    {
        var served = JsonNode.Parse(await factory.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

        var scheme = served["components"]!["securitySchemes"]!["Bearer"]!;
        Assert.Equal(("http", "bearer"), (scheme["type"]!.GetValue<string>(), scheme["scheme"]!.GetValue<string>()));

        static bool Secured(JsonNode operation) =>
            operation["security"]?.AsArray().Any(r => r!.AsObject().ContainsKey("Bearer")) == true;
        Assert.True(Secured(served["paths"]!["/me"]!["get"]!));
        Assert.True(Secured(served["paths"]!["/admin/users"]!["get"]!));
        Assert.True(Secured(served["paths"]!["/auth/manage/info"]!["get"]!));
        Assert.False(Secured(served["paths"]!["/auth/login"]!["post"]!));
        Assert.False(Secured(served["paths"]!["/auth/external"]!["post"]!));
    }

    // The server list depends on the host the document was generated or requested from, so it is not part of the contract.
    private static JsonNode? Normalise(JsonNode? document)
    {
        document!.AsObject().Remove("servers");
        return document;
    }

    private static string Serialise(JsonNode? node) =>
        node!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
