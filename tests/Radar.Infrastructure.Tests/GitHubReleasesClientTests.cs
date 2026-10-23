using System.Net;
using Radar.Infrastructure.GitHub;

namespace Radar.Infrastructure.Tests;

public sealed class GitHubReleasesClientTests
{
    // Trimmed shape of https://docs.github.com/rest/releases/releases#list-releases (newest first).
    private const string TwoReleasesJson = """
        [
          {
            "id": 222,
            "tag_name": "v2.0.0",
            "name": "Version 2",
            "html_url": "https://github.com/example-org/example-repo/releases/tag/v2.0.0",
            "body": "Second release notes",
            "published_at": "2026-10-02T08:15:00Z",
            "draft": false,
            "prerelease": false
          },
          {
            "id": 111,
            "tag_name": "v1.0.0",
            "name": null,
            "html_url": "https://github.com/example-org/example-repo/releases/tag/v1.0.0",
            "body": "",
            "published_at": null
          }
        ]
        """;

    [Fact]
    public async Task Maps_release_fields()
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, TwoReleasesJson));

        var releases = await client.GetReleasesAsync("example-org", "example-repo");

        Assert.Equal(2, releases.Count);
        Assert.Equal(
            new GitHubRelease(222, "Version 2", "https://github.com/example-org/example-repo/releases/tag/v2.0.0",
                "Second release notes", new DateTime(2026, 10, 2, 8, 15, 0, DateTimeKind.Utc)),
            releases[0]);
        Assert.Equal(DateTimeKind.Utc, releases[0].PublishedAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task Uses_tag_name_when_name_is_missing_and_null_for_empty_body_or_date()
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, TwoReleasesJson));

        var release = (await client.GetReleasesAsync("example-org", "example-repo"))[1];

        Assert.Equal("v1.0.0", release.Title);
        Assert.Null(release.Description);
        Assert.Null(release.PublishedAtUtc);
    }

    [Fact]
    public async Task Sends_request_to_releases_endpoint_with_required_headers()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);

        await CreateClient(handler).GetReleasesAsync("example-org", "example-repo");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/example-org/example-repo/releases?per_page=30", request.RequestUri!.ToString());
        Assert.Equal("Radar", request.Headers.UserAgent.ToString());
        Assert.Equal("application/vnd.github+json", request.Headers.Accept.ToString());
        Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Returns_empty_list_when_repository_has_no_releases()
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, "[]"));

        Assert.Empty(await client.GetReleasesAsync("example-org", "example-repo"));
    }

    [Fact]
    public async Task Not_found_throws_with_status_code()
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(() => client.GetReleasesAsync("example-org", "missing"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.False(ex.IsRateLimited);
        Assert.Contains("example-org/missing", ex.Message);
    }

    [Fact]
    public async Task Primary_rate_limit_throws_with_reset_time()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}""", r =>
        {
            r.Headers.Add("x-ratelimit-remaining", "0");
            r.Headers.Add("x-ratelimit-reset", "1792800000");
        });

        var ex = await Assert.ThrowsAsync<GitHubApiException>(() => CreateClient(handler).GetReleasesAsync("example-org", "example-repo"));

        Assert.True(ex.IsRateLimited);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792800000).UtcDateTime, ex.RateLimitResetUtc);
    }

    [Fact]
    public async Task Secondary_rate_limit_uses_retry_after()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests, "{}", r =>
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60)));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(() => CreateClient(handler).GetReleasesAsync("example-org", "example-repo"));

        Assert.True(ex.IsRateLimited);
        Assert.InRange(ex.RateLimitResetUtc!.Value, DateTime.UtcNow.AddSeconds(50), DateTime.UtcNow.AddSeconds(70));
    }

    [Fact]
    public async Task Forbidden_without_rate_limit_headers_is_not_reported_as_rate_limit()
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "{}"));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(() => client.GetReleasesAsync("example-org", "example-repo"));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.False(ex.IsRateLimited);
    }

    [Fact]
    public async Task Server_error_throws_with_status_code()
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "{}"));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(() => client.GetReleasesAsync("example-org", "example-repo"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [Fact]
    public async Task Unexpected_response_shape_throws_GitHubApiException()
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, """[{ "id": 1 }]"""));

        await Assert.ThrowsAsync<GitHubApiException>(() => client.GetReleasesAsync("example-org", "example-repo"));
    }

    private static GitHubReleasesClient CreateClient(FakeHttpMessageHandler handler) => new(new HttpClient(handler));
}
