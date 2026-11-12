using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CustomGolfStats.Api.Features.Courses;

/// <summary>
/// <see cref="ICourseProvider"/> over GolfCourseAPI (https://api.golfcourseapi.com/v1/), as a typed <see cref="HttpClient"/>
/// configured by <see cref="CoursesFeature.AddCoursesFeature"/>: base address plus <c>Authorization: Key {key}</c> from
/// configuration <c>GolfCourseApi:ApiKey</c>. Any failure surfaces as <see cref="CourseProviderException"/>.
/// </summary>
public sealed class GolfCourseApiProvider(HttpClient http) : ICourseProvider
{
    public const string BaseAddress = "https://api.golfcourseapi.com/v1/";
    public const string ApiKeySetting = "GolfCourseApi:ApiKey";

    /// <summary>Sets the base address and key header on the typed client.</summary>
    public static void Configure(HttpClient client, string? apiKey)
    {
        client.BaseAddress = new Uri(BaseAddress);
        client.Timeout = TimeSpan.FromSeconds(20);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Key", apiKey.Trim());
        }
    }

    public async Task<IReadOnlyList<ProviderCourseSummary>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var result = await GetJsonAsync($"search?search_query={Uri.EscapeDataString(query.Trim())}", ct);
        if (result is null)
        {
            return [];
        }

        using var json = result;
        return GolfCourseApiMapper.MapSearch(json.RootElement);
    }

    public async Task<ProviderCourse?> GetAsync(string providerCourseId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerCourseId))
        {
            return null;
        }

        using var json = await GetJsonAsync($"courses/{Uri.EscapeDataString(providerCourseId.Trim())}", ct);
        return json is null ? null : GolfCourseApiMapper.MapCourseResponse(json.RootElement);
    }

    /// <returns>The parsed body, or null for 404.</returns>
    private async Task<JsonDocument?> GetJsonAsync(string path, CancellationToken ct)
    {
        if (http.DefaultRequestHeaders.Authorization is null)
        {
            throw new CourseProviderException($"GolfCourseAPI is not configured (set {ApiKeySetting}).");
        }

        try
        {
            using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CourseProviderException($"GolfCourseAPI returned {(int)response.StatusCode}.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(body, cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new CourseProviderException("GolfCourseAPI could not be reached or sent an unreadable response.", ex);
        }
    }
}
