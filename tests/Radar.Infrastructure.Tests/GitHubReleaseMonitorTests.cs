using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.GitHub;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;
using Radar.Worker;

namespace Radar.Infrastructure.Tests;

public sealed class GitHubReleaseMonitorTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(60);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _contentItems;

    public GitHubReleaseMonitorTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _database.Initialize();
        _sources = new SourceStore(_database);
        _contentItems = new ContentItemStore(_database);
    }

    [Fact]
    public async Task Cycle_imports_releases_of_active_github_sources_and_skips_inactive_ones()
    {
        var active = AddGitHubSource("example-org/active-repo");
        AddGitHubSource("example-org/inactive-repo", isActive: false);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ReleasesJson(1));

        var wait = await CreateMonitor(handler).RunCycleAsync(CancellationToken.None);

        Assert.Equal(Interval, wait);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("/repos/example-org/active-repo/releases", request.RequestUri!.AbsolutePath);
        Assert.Single(_contentItems.GetBySource(active.Id));
    }

    [Fact]
    public async Task Cycle_without_active_sources_makes_no_requests()
    {
        AddGitHubSource("example-org/inactive-repo", isActive: false);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);

        var wait = await CreateMonitor(handler).RunCycleAsync(CancellationToken.None);

        Assert.Equal(Interval, wait);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Repeated_cycles_without_new_releases_do_not_create_duplicates()
    {
        var source = AddGitHubSource("example-org/example-repo");
        var monitor = CreateMonitor(new FakeHttpMessageHandler(HttpStatusCode.OK, ReleasesJson(1, 2)));

        await monitor.RunCycleAsync(CancellationToken.None);
        await monitor.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, _contentItems.GetBySource(source.Id).Count);
    }

    [Fact]
    public async Task Error_in_one_source_does_not_stop_the_others()
    {
        AddGitHubSource("example-org/broken-repo");
        var healthy = AddGitHubSource("example-org/healthy-repo");
        var handler = new FakeHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("broken-repo")
                ? FakeHttpMessageHandler.Response(HttpStatusCode.InternalServerError, "{}")
                : FakeHttpMessageHandler.Response(HttpStatusCode.OK, ReleasesJson(1)));

        var wait = await CreateMonitor(handler).RunCycleAsync(CancellationToken.None);

        Assert.Equal(Interval, wait);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(_contentItems.GetBySource(healthy.Id));
    }

    [Fact]
    public async Task Rate_limit_stops_the_cycle_and_waits_until_reset()
    {
        AddGitHubSource("example-org/first-repo");
        AddGitHubSource("example-org/second-repo");
        var resetUtc = DateTimeOffset.UtcNow.AddMinutes(30);
        var handler = RateLimitedHandler(resetUtc);

        var wait = await CreateMonitor(handler).RunCycleAsync(CancellationToken.None);

        Assert.Single(handler.Requests); // second source is not requested while the quota is exhausted
        Assert.InRange(wait, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(31));
    }

    [Fact]
    public async Task Rate_limit_with_past_reset_still_waits_at_least_one_minute()
    {
        AddGitHubSource("example-org/example-repo");

        var wait = await CreateMonitor(RateLimitedHandler(DateTimeOffset.UtcNow.AddMinutes(-5))).RunCycleAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(1), wait);
    }

    [Fact]
    public async Task Running_monitor_does_not_retry_quickly_after_a_rate_limit()
    {
        AddGitHubSource("example-org/example-repo");
        var handler = RateLimitedHandler(DateTimeOffset.UtcNow.AddMinutes(30));
        // A tiny normal interval: if the rate limit were ignored, many requests would happen during the wait below.
        var monitor = CreateMonitor(handler, TimeSpan.FromMilliseconds(20));

        await monitor.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await monitor.StopAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Running_monitor_stops_in_an_orderly_way()
    {
        AddGitHubSource("example-org/example-repo");
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ReleasesJson(1));
        var monitor = CreateMonitor(handler);

        await monitor.StartAsync(CancellationToken.None);
        await WaitUntil(() => handler.Requests.Count == 1);
        var stop = monitor.StopAsync(CancellationToken.None);

        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.True(monitor.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public void Non_positive_polling_interval_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateMonitor(new FakeHttpMessageHandler(HttpStatusCode.OK), TimeSpan.Zero));
    }

    private GitHubReleaseMonitor CreateMonitor(FakeHttpMessageHandler handler, TimeSpan? interval = null)
    {
        var importer = new GitHubReleaseImporter(new GitHubReleasesClient(new HttpClient(handler)), _sources, _contentItems);
        var options = Options.Create(new GitHubMonitorOptions { PollingInterval = interval ?? Interval });
        return new GitHubReleaseMonitor(importer, _sources, options, NullLogger<GitHubReleaseMonitor>.Instance);
    }

    private Source AddGitHubSource(string externalId, bool isActive = true) =>
        _sources.TryAdd(SourceType.GitHub, externalId, externalId, $"https://github.com/{externalId}", isActive)!;

    private static FakeHttpMessageHandler RateLimitedHandler(DateTimeOffset resetUtc) =>
        new(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}""", r =>
        {
            r.Headers.Add("x-ratelimit-remaining", "0");
            r.Headers.Add("x-ratelimit-reset", resetUtc.ToUnixTimeSeconds().ToString());
        });

    private static string ReleasesJson(params long[] ids) =>
        "[" + string.Join(",", ids.Select(id =>
            $$"""{ "id": {{id}}, "tag_name": "v{{id}}", "name": null, "html_url": "https://example.invalid/releases/{{id}}", "body": null, "published_at": "2026-10-01T00:00:00Z" }""")) + "]";

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
