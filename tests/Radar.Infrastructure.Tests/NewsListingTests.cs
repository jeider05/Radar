using Radar.Core.Contents;
using Radar.Core.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class NewsListingTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildRows_maps_the_items_source_name_and_type()
    {
        var source = Source(1, SourceType.GitHub, "org/repo");
        var item = Item(1, source.Id);

        var rows = NewsListing.BuildRows([item], [source], [], geminiConfigured: false);

        var row = Assert.Single(rows);
        Assert.Equal("repo", row.SourceName); // GitHub DisplayName drops the owner
        Assert.Equal(SourceType.GitHub, row.SourceType);
    }

    [Fact]
    public void BuildRows_falls_back_to_unknown_source_when_the_source_was_removed()
    {
        var item = Item(1, sourceId: 404);

        var rows = NewsListing.BuildRows([item], [], [], geminiConfigured: false);

        var row = Assert.Single(rows);
        Assert.Equal("Unknown source", row.SourceName);
    }

    [Fact]
    public void BuildRows_without_a_summary_is_Pending_when_gemini_is_configured()
    {
        var source = Source(1, SourceType.GitHub, "org/repo");
        var item = Item(1, source.Id);

        var rows = NewsListing.BuildRows([item], [source], [], geminiConfigured: true);

        Assert.Equal(SummaryDisplayStatus.Pending, Assert.Single(rows).SummaryStatus);
    }

    [Fact]
    public void BuildRows_without_a_summary_is_None_when_gemini_is_not_configured()
    {
        var source = Source(1, SourceType.GitHub, "org/repo");
        var item = Item(1, source.Id);

        var rows = NewsListing.BuildRows([item], [source], [], geminiConfigured: false);

        Assert.Equal(SummaryDisplayStatus.None, Assert.Single(rows).SummaryStatus);
    }

    [Fact]
    public void BuildRows_parses_a_ready_release_summary_with_points_and_action()
    {
        var source = Source(1, SourceType.GitHub, "org/repo");
        var item = Item(1, source.Id);
        var summary = ReadySummary(item.Id, ContentSummaryKind.Release, """{"puntos":["Agrega X","Corrige Y"],"accion":"npm install"}""");

        var row = Assert.Single(NewsListing.BuildRows([item], [source], [summary], geminiConfigured: true));

        Assert.Equal(SummaryDisplayStatus.Ready, row.SummaryStatus);
        Assert.Equal(["Agrega X", "Corrige Y", "npm install"], row.ReleaseSummaryBullets);
        Assert.Null(row.VideoSummary);
    }

    [Fact]
    public void BuildRows_release_bullets_omit_the_action_when_there_is_none()
    {
        var source = Source(1, SourceType.GitHub, "org/repo");
        var item = Item(1, source.Id);
        var summary = ReadySummary(item.Id, ContentSummaryKind.Release, """{"puntos":["Agrega X"]}""");

        var row = Assert.Single(NewsListing.BuildRows([item], [source], [summary], geminiConfigured: true));

        Assert.Equal(["Agrega X"], row.ReleaseSummaryBullets);
    }

    [Fact]
    public void BuildRows_parses_a_ready_video_summary()
    {
        var source = Source(1, SourceType.YouTube, "Channel");
        var item = Item(1, source.Id);
        var summary = ReadySummary(item.Id, ContentSummaryKind.Video, """{"deQueTrata":"Explica cómo usar X."}""");

        var row = Assert.Single(NewsListing.BuildRows([item], [source], [summary], geminiConfigured: true));

        Assert.Equal(SummaryDisplayStatus.Ready, row.SummaryStatus);
        Assert.Equal("Explica cómo usar X.", row.VideoSummary!.DeQueTrata);
        Assert.Null(row.ReleaseSummaryBullets);
    }

    [Fact]
    public void BuildRows_an_error_summary_shows_as_None_even_when_gemini_is_configured()
    {
        var source = Source(1, SourceType.GitHub, "org/repo");
        var item = Item(1, source.Id);
        var summary = new ContentSummary(item.Id, ContentSummaryKind.Release, "{bad}", 1, "gemini-test", ContentSummaryStatus.Error, "falló", NowUtc);

        var row = Assert.Single(NewsListing.BuildRows([item], [source], [summary], geminiConfigured: true));

        Assert.Equal(SummaryDisplayStatus.None, row.SummaryStatus);
    }

    [Fact]
    public void HasChanges_is_true_on_the_first_load()
    {
        Assert.True(NewsListing.HasChanges(null, [], null, [], null, [], null, null));
    }

    [Fact]
    public void HasChanges_is_false_when_nothing_changed()
    {
        var sources = new List<Source> { Source(1, SourceType.GitHub, "org/repo") };
        var items = new List<ContentItem> { Item(1, 1) };
        var summaries = new List<ContentSummary> { ReadySummary(1, ContentSummaryKind.Release, """{"puntos":["x"]}""") };
        var digest = new DailyDigest(DateOnly.FromDateTime(NowUtc), """{"frase":"Hoy"}""", "hash", "gemini-test", NowUtc);

        Assert.False(NewsListing.HasChanges(sources, sources, items, items, summaries, summaries, digest, digest));
    }

    [Fact]
    public void HasChanges_detects_a_new_summary()
    {
        var sources = new List<Source> { Source(1, SourceType.GitHub, "org/repo") };
        var items = new List<ContentItem> { Item(1, 1) };
        var before = new List<ContentSummary>();
        var after = new List<ContentSummary> { ReadySummary(1, ContentSummaryKind.Release, """{"puntos":["x"]}""") };

        Assert.True(NewsListing.HasChanges(sources, sources, items, items, before, after, null, null));
    }

    [Fact]
    public void HasChanges_detects_a_changed_daily_digest()
    {
        var sources = new List<Source> { Source(1, SourceType.GitHub, "org/repo") };
        var items = new List<ContentItem> { Item(1, 1) };
        var summaries = new List<ContentSummary>();
        var date = DateOnly.FromDateTime(NowUtc);
        var before = new DailyDigest(date, """{"frase":"Antes"}""", "hash-1", "gemini-test", NowUtc);
        var after = new DailyDigest(date, """{"frase":"Después"}""", "hash-2", "gemini-test", NowUtc);

        Assert.True(NewsListing.HasChanges(sources, sources, items, items, summaries, summaries, before, after));
    }

    private static Source Source(long id, SourceType type, string name) =>
        new(id, type, name, name, $"https://example.invalid/{name}", true, NowUtc);

    private static ContentItem Item(long id, long sourceId) =>
        new(id, sourceId, id.ToString(), "Title", "https://example.invalid", "Notes", NowUtc, NowUtc, IsRead: false);

    private static ContentSummary ReadySummary(long contentItemId, ContentSummaryKind kind, string json) =>
        new(contentItemId, kind, json, 1, "gemini-test", ContentSummaryStatus.Ready, null, NowUtc);
}
