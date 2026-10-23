using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radar.Infrastructure.GitHub;

public sealed record GitHubRelease(long Id, string Title, string Url, string? Description, DateTime? PublishedAtUtc);

public sealed class GitHubApiException(string message, HttpStatusCode? statusCode = null, DateTime? rateLimitResetUtc = null)
    : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    public bool IsRateLimited => RateLimitResetUtc is not null;

    /// <summary>When the rate limit resets, if the failure was caused by it.</summary>
    public DateTime? RateLimitResetUtc { get; } = rateLimitResetUtc;
}

/// <summary>
/// Reads releases of a public repository from the GitHub REST API without authentication.
/// See https://docs.github.com/rest/releases/releases#list-releases.
/// </summary>
public sealed class GitHubReleasesClient(HttpClient httpClient)
{
    private const string ApiBaseUrl = "https://api.github.com";
    private const string UserAgent = "Radar";
    private const int PageSize = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>HttpClient settings used by Radar for GitHub requests.</summary>
    public static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Returns the most recent releases (first page only), newest first. Empty when the repository has none.</summary>
    public async Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(
        string owner, string repository, CancellationToken cancellationToken = default)
    {
        var url = $"{ApiBaseUrl}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases?per_page={PageSize}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, null));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubApiException($"Could not reach GitHub: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw CreateError(response, owner, repository);
            }

            try
            {
                var releases = await JsonSerializer.DeserializeAsync<List<ReleaseDto>>(
                    await response.Content.ReadAsStreamAsync(cancellationToken), JsonOptions, cancellationToken);

                return (releases ?? []).Select(Map).ToList();
            }
            catch (JsonException ex)
            {
                throw new GitHubApiException($"Unexpected response from GitHub: {ex.Message}", response.StatusCode);
            }
        }
    }

    private static GitHubRelease Map(ReleaseDto dto) => new(
        dto.Id,
        string.IsNullOrWhiteSpace(dto.Name) ? dto.TagName : dto.Name,
        dto.HtmlUrl,
        string.IsNullOrWhiteSpace(dto.Body) ? null : dto.Body,
        dto.PublishedAt?.UtcDateTime);

    private static GitHubApiException CreateError(HttpResponseMessage response, string owner, string repository)
    {
        var status = response.StatusCode;

        // Primary rate limit: 403/429 with no remaining requests. Secondary rate limit: Retry-After.
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var resetUtc = GetRetryAfter(response) ?? GetRateLimitReset(response);
            if (resetUtc is not null)
            {
                return new GitHubApiException(
                    $"GitHub rate limit exceeded. Try again after {resetUtc:u}.", status, resetUtc);
            }
        }

        var message = status == HttpStatusCode.NotFound
            ? $"Repository '{owner}/{repository}' was not found or is not public."
            : $"GitHub returned {(int)status} ({response.ReasonPhrase}).";
        return new GitHubApiException(message, status);
    }

    private static DateTime? GetRetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta ? DateTime.UtcNow + delta : null;

    private static DateTime? GetRateLimitReset(HttpResponseMessage response)
    {
        if (GetHeader(response, "x-ratelimit-remaining") != "0"
            || !long.TryParse(GetHeader(response, "x-ratelimit-reset"), out var resetEpochSeconds))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(resetEpochSeconds).UtcDateTime;
    }

    private static string? GetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private sealed record ReleaseDto(
        long Id,
        [property: JsonRequired] string TagName,
        string? Name,
        [property: JsonRequired] string HtmlUrl,
        string? Body,
        DateTimeOffset? PublishedAt);
}
