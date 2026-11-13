using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomGolfStats.Api.Tests.Infrastructure;

/// <summary>The API's JSON conventions for reading responses in tests: web defaults, enums as names.</summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
