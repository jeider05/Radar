using System.Net;
using Microsoft.Data.Sqlite;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;
using Radar.Infrastructure.YouTube;

namespace Radar.Infrastructure.Tests;

public sealed class YouTubeChannelLinkTests
{
    private const string Id = "UCBR8-60-B28hp2BmDPdntcQ";

    [Theory]
    [InlineData(Id)]
    [InlineData("https://www.youtube.com/channel/" + Id)]
    [InlineData("https://youtube.com/channel/" + Id + "/videos")]
    [InlineData("www.youtube.com/channel/" + Id + "?si=abc")]
    [InlineData("  https://m.youtube.com/channel/" + Id + "/  ")]
    public void TryParse_reads_channel_ids_directly(string input)
    {
        Assert.True(YouTubeChannelLink.TryParse(input, out var channelId, out var pageUrl, out var error));

        Assert.Equal(Id, channelId);
        Assert.Null(pageUrl);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData("@YouTube", "https://www.youtube.com/@YouTube")]
    [InlineData("https://www.youtube.com/@Some.Channel_1", "https://www.youtube.com/@Some.Channel_1")]
    [InlineData("youtube.com/@channel-name/videos", "https://www.youtube.com/@channel-name")]
    [InlineData("https://www.youtube.com/c/LegacyName", "https://www.youtube.com/c/LegacyName")]
    [InlineData("https://www.youtube.com/user/OldUser", "https://www.youtube.com/user/OldUser")]
    public void TryParse_returns_the_page_to_resolve_for_handles_and_legacy_names(string input, string expectedPage)
    {
        Assert.True(YouTubeChannelLink.TryParse(input, out var channelId, out var pageUrl, out _));

        Assert.Null(channelId);
        Assert.Equal(expectedPage, pageUrl!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("YouTube")]
    [InlineData("@ab")]
    [InlineData("https://www.youtube.com/watch?v=idqAz3omDi4")]
    [InlineData("https://youtu.be/idqAz3omDi4")]
    [InlineData("https://vimeo.com/channel/" + Id)]
    [InlineData("https://www.youtube.com/channel/UCshort")]
    public void TryParse_rejects_non_channel_input_with_a_message(string? input)
    {
        Assert.False(YouTubeChannelLink.TryParse(input, out var channelId, out var pageUrl, out var error));

        Assert.Null(channelId);
        Assert.Null(pageUrl);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Video_links_get_a_specific_message()
    {
        YouTubeChannelLink.TryParse("https://www.youtube.com/watch?v=idqAz3omDi4", out _, out _, out var error);

        Assert.Contains("video link", error);
    }
}

public sealed class YouTubeClientTests
{
    internal const string ChannelId = "UCBR8-60-B28hp2BmDPdntcQ";

    // Trimmed copy of a real feed (October 2026). Note the feed-level yt:channelId without "UC".
    internal const string FeedXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns:media="http://search.yahoo.com/mrss/" xmlns="http://www.w3.org/2005/Atom">
         <id>yt:channel:BR8-60-B28hp2BmDPdntcQ</id>
         <yt:channelId>BR8-60-B28hp2BmDPdntcQ</yt:channelId>
         <title>YouTube</title>
         <entry>
          <id>yt:video:newVideo001</id>
          <yt:videoId>newVideo001</yt:videoId>
          <title>Newest video</title>
          <link rel="alternate" href="https://www.youtube.com/shorts/newVideo001"/>
          <published>2026-10-23T18:00:50+00:00</published>
          <media:group><media:description>  First line of the description.  </media:description></media:group>
         </entry>
         <entry>
          <id>yt:video:oldVideo002</id>
          <yt:videoId>oldVideo002</yt:videoId>
          <title>Older video</title>
          <link rel="alternate" href="https://www.youtube.com/watch?v=oldVideo002"/>
          <published>2026-10-20T10:00:00-05:00</published>
          <media:group><media:description></media:description></media:group>
         </entry>
        </feed>
        """;

    [Fact]
    public async Task GetFeedAsync_reads_channel_title_and_videos()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, FeedXml);

        var feed = await new YouTubeClient(new HttpClient(handler)).GetFeedAsync(ChannelId);

        Assert.Equal(ChannelId, feed.ChannelId);
        Assert.Equal("YouTube", feed.ChannelTitle);
        Assert.Equal(
            new YouTubeVideo("newVideo001", "Newest video", "https://www.youtube.com/shorts/newVideo001",
                "First line of the description.", new DateTime(2026, 10, 23, 18, 0, 50, DateTimeKind.Utc)),
            feed.Videos[0]);
        Assert.Null(feed.Videos[1].Description);
        Assert.Equal(new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc), feed.Videos[1].PublishedAtUtc);
        Assert.Equal(
            $"https://www.youtube.com/feeds/videos.xml?channel_id={ChannelId}",
            Assert.Single(handler.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task GetFeedAsync_of_a_missing_channel_throws_with_status()
    {
        var client = new YouTubeClient(new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.NotFound, "")));

        var ex = await Assert.ThrowsAsync<YouTubeApiException>(() => client.GetFeedAsync(ChannelId));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetFeedAsync_with_invalid_xml_throws()
    {
        var client = new YouTubeClient(new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.OK, "<html>consent</html><")));

        await Assert.ThrowsAsync<YouTubeApiException>(() => client.GetFeedAsync(ChannelId));
    }

    [Fact]
    public async Task Too_many_requests_reports_when_to_retry()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests, "", r =>
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5)));

        var ex = await Assert.ThrowsAsync<YouTubeApiException>(() => new YouTubeClient(new HttpClient(handler)).GetFeedAsync(ChannelId));

        Assert.InRange(ex.RetryAfterUtc!.Value, DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(6));
    }

    [Theory]
    [InlineData("""<html><link rel="canonical" href="https://www.youtube.com/channel/UCBR8-60-B28hp2BmDPdntcQ"></html>""")]
    [InlineData("""<script>var x = {"externalId":"UCBR8-60-B28hp2BmDPdntcQ"};</script>""")]
    public async Task ResolveChannelIdAsync_reads_the_channel_id_from_the_page(string html)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, html);

        var channelId = await new YouTubeClient(new HttpClient(handler)).ResolveChannelIdAsync(new Uri("https://www.youtube.com/@YouTube"));

        Assert.Equal(ChannelId, channelId);
        Assert.Equal("https://www.youtube.com/@YouTube", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task ResolveChannelIdAsync_fails_clearly_when_the_page_has_no_channel()
    {
        var client = new YouTubeClient(new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.OK, "<html>nothing here</html>")));

        var ex = await Assert.ThrowsAsync<YouTubeApiException>(() => client.ResolveChannelIdAsync(new Uri("https://www.youtube.com/@missing")));

        Assert.Contains("Could not find", ex.Message);
    }
}

public sealed class YouTubeVideoImporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _contentItems;

    public YouTubeVideoImporterTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _database.Initialize();
        _sources = new SourceStore(_database);
        _contentItems = new ContentItemStore(_database);
    }

    [Fact]
    public async Task Import_registers_the_channel_and_stores_videos_as_unread_newest_first()
    {
        var result = await CreateImporter(YouTubeClientTests.FeedXml).ImportAsync(YouTubeClientTests.ChannelId);

        var source = _sources.Find(SourceType.YouTube, YouTubeClientTests.ChannelId)!;
        Assert.Equal("YouTube", source.Name);
        Assert.Equal($"https://www.youtube.com/channel/{YouTubeClientTests.ChannelId}", source.Url);
        Assert.Equal(new YouTubeImportResult(source.Id, VideosFound: 2, VideosAdded: 2), result);

        var items = _contentItems.GetBySource(source.Id);
        Assert.Equal(["newVideo001", "oldVideo002"], items.Select(i => i.ExternalId));
        Assert.All(items, i => Assert.False(i.IsRead));
    }

    [Fact]
    public async Task Importing_again_does_not_duplicate_videos()
    {
        await CreateImporter(YouTubeClientTests.FeedXml).ImportAsync(YouTubeClientTests.ChannelId);

        var second = await CreateImporter(YouTubeClientTests.FeedXml).ImportAsync(YouTubeClientTests.ChannelId);

        Assert.Equal(0, second.VideosAdded);
        Assert.Equal(2, _contentItems.GetAll().Count);
        Assert.Single(_sources.GetAll());
    }

    [Fact]
    public async Task Failed_feed_request_stores_nothing()
    {
        var importer = CreateImporter("", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<YouTubeApiException>(() => importer.ImportAsync(YouTubeClientTests.ChannelId));

        Assert.Empty(_sources.GetAll());
    }

    private YouTubeVideoImporter CreateImporter(string xml, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new YouTubeClient(new HttpClient(new FakeHttpMessageHandler(status, xml))), _sources, _contentItems);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}

public sealed class YouTubeChannelMonitorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _contentItems;

    public YouTubeChannelMonitorTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _database.Initialize();
        _sources = new SourceStore(_database);
        _contentItems = new ContentItemStore(_database);
    }

    [Fact]
    public async Task Cycle_imports_active_youtube_channels_only()
    {
        var channel = _sources.TryAdd(SourceType.YouTube, YouTubeClientTests.ChannelId, "YouTube", "https://www.youtube.com/channel/x")!;
        _sources.TryAdd(SourceType.YouTube, "UCpausedChannel0000000000", "Paused", "https://www.youtube.com/channel/y", isActive: false);
        _sources.TryAdd(SourceType.GitHub, "example-org/example-repo", "example-org/example-repo", "https://github.com/example-org/example-repo");
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, YouTubeClientTests.FeedXml);

        await CreateMonitor(handler).RunCycleAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Contains(YouTubeClientTests.ChannelId, request.RequestUri!.ToString());
        Assert.Equal(2, _contentItems.GetBySource(channel.Id).Count);
    }

    [Fact]
    public async Task GitHub_monitor_ignores_youtube_channels()
    {
        _sources.TryAdd(SourceType.YouTube, YouTubeClientTests.ChannelId, "YouTube", "https://www.youtube.com/channel/x");
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var gitHub = new Radar.Worker.GitHubReleaseMonitor(
            new Radar.Infrastructure.GitHub.GitHubReleaseImporter(new Radar.Infrastructure.GitHub.GitHubReleasesClient(new HttpClient(handler)), _sources, _contentItems),
            _sources, Microsoft.Extensions.Options.Options.Create(new Radar.Worker.GitHubMonitorOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Radar.Worker.GitHubReleaseMonitor>.Instance);

        await gitHub.RunCycleAsync(CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Too_many_requests_pauses_the_cycle_until_youtube_allows_it()
    {
        _sources.TryAdd(SourceType.YouTube, YouTubeClientTests.ChannelId, "YouTube", "https://www.youtube.com/channel/x");
        _sources.TryAdd(SourceType.YouTube, "UCsecondChannel000000000", "Second", "https://www.youtube.com/channel/z");
        var handler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests, "", r =>
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(20)));

        var wait = await CreateMonitor(handler).RunCycleAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.InRange(wait, TimeSpan.FromMinutes(19), TimeSpan.FromMinutes(21));
    }

    [Fact]
    public async Task New_channel_is_checked_without_waiting_for_the_next_cycle()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, YouTubeClientTests.FeedXml);
        var monitor = CreateMonitor(handler);
        await monitor.RunCycleAsync(CancellationToken.None);

        _sources.TryAdd(SourceType.YouTube, YouTubeClientTests.ChannelId, "YouTube", "https://www.youtube.com/channel/x");
        await monitor.CheckNewSourcesAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    private Radar.Worker.YouTubeChannelMonitor CreateMonitor(FakeHttpMessageHandler handler) => new(
        new YouTubeVideoImporter(new YouTubeClient(new HttpClient(handler)), _sources, _contentItems),
        _sources,
        Microsoft.Extensions.Options.Options.Create(new Radar.Worker.YouTubeMonitorOptions()),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Radar.Worker.YouTubeChannelMonitor>.Instance);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
