using Microsoft.Data.Sqlite;
using Radar.Core.Contents;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class ContentSummaryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests.Summaries", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _items;
    private readonly ContentSummaryStore _summaries;

    public ContentSummaryStoreTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _database.Initialize();
        _sources = new SourceStore(_database);
        _items = new ContentItemStore(_database);
        _summaries = new ContentSummaryStore(_database);
    }

    [Fact]
    public void SaveReady_then_Get_returns_the_stored_summary()
    {
        var item = AddGitHubItem("v1");

        var saved = _summaries.SaveReady(item.Id, ContentSummaryKind.Release, """{"puntos":["x"]}""", promptVersion: 1, model: "gemini-test");

        var loaded = _summaries.Get(item.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ContentSummaryStatus.Ready, loaded.Status);
        Assert.Equal("""{"puntos":["x"]}""", loaded.Json);
        Assert.Null(loaded.Error);
        Assert.Equal(saved.CreatedAtUtc, loaded.CreatedAtUtc);
    }

    [Fact]
    public void SaveError_then_Get_returns_error_status_and_message()
    {
        var item = AddGitHubItem("v1");

        _summaries.SaveError(item.Id, ContentSummaryKind.Release, promptVersion: 1, model: "gemini-test", error: "no válido", json: "{bad}");

        var loaded = _summaries.Get(item.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ContentSummaryStatus.Error, loaded.Status);
        Assert.Equal("no válido", loaded.Error);
        Assert.Equal("{bad}", loaded.Json);
    }

    [Fact]
    public void SaveReady_overwrites_a_previous_summary_for_the_same_item()
    {
        var item = AddGitHubItem("v1");
        _summaries.SaveError(item.Id, ContentSummaryKind.Release, promptVersion: 1, model: "gemini-test", error: "falló");

        _summaries.SaveReady(item.Id, ContentSummaryKind.Release, """{"puntos":["x"]}""", promptVersion: 2, model: "gemini-test");

        var loaded = _summaries.Get(item.Id);
        Assert.Equal(ContentSummaryStatus.Ready, loaded!.Status);
        Assert.Equal(2, loaded.PromptVersion);
        Assert.Null(loaded.Error);
    }

    [Fact]
    public void GetReady_returns_only_ready_summaries()
    {
        var ready = AddGitHubItem("v1");
        var failed = AddGitHubItem("v2");
        _summaries.SaveReady(ready.Id, ContentSummaryKind.Release, """{"puntos":["x"]}""", 1, "gemini-test");
        _summaries.SaveError(failed.Id, ContentSummaryKind.Release, 1, "gemini-test", "falló");

        var result = _summaries.GetReady();

        Assert.Equal([ready.Id], result.Select(s => s.ContentItemId));
    }

    [Fact]
    public void GetCandidates_returns_items_without_a_summary_yet()
    {
        var item = AddGitHubItem("v1");

        var candidates = _summaries.GetCandidates(releasePromptVersion: 1, videoPromptVersion: 1, onlyUnread: false);

        var candidate = Assert.Single(candidates);
        Assert.Equal(item.Id, candidate.Item.Id);
        Assert.Equal(ContentSummaryKind.Release, candidate.Kind);
    }

    [Fact]
    public void GetCandidates_excludes_items_already_summarized_with_the_current_prompt_version()
    {
        var item = AddGitHubItem("v1");
        _summaries.SaveReady(item.Id, ContentSummaryKind.Release, """{"puntos":["x"]}""", promptVersion: 2, model: "gemini-test");

        var candidates = _summaries.GetCandidates(releasePromptVersion: 2, videoPromptVersion: 1, onlyUnread: false);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GetCandidates_includes_items_summarized_with_an_older_prompt_version()
    {
        var item = AddGitHubItem("v1");
        _summaries.SaveReady(item.Id, ContentSummaryKind.Release, """{"puntos":["x"]}""", promptVersion: 1, model: "gemini-test");

        var candidates = _summaries.GetCandidates(releasePromptVersion: 2, videoPromptVersion: 1, onlyUnread: false);

        Assert.Single(candidates);
    }

    [Fact]
    public void GetCandidates_respects_only_unread()
    {
        var unread = AddGitHubItem("v1");
        var read = AddGitHubItem("v2");
        _items.MarkAsRead(read.Id);

        var candidates = _summaries.GetCandidates(releasePromptVersion: 1, videoPromptVersion: 1, onlyUnread: true);

        Assert.Equal([unread.Id], candidates.Select(c => c.Item.Id));
    }

    [Fact]
    public void GetCandidates_maps_GitHub_to_release_and_YouTube_to_video()
    {
        AddGitHubItem("v1");
        AddYouTubeItem("abc");

        var candidates = _summaries.GetCandidates(releasePromptVersion: 1, videoPromptVersion: 1, onlyUnread: false);

        Assert.Equal(ContentSummaryKind.Release, candidates.Single(c => c.Kind == ContentSummaryKind.Release).Kind);
        Assert.Equal(ContentSummaryKind.Video, candidates.Single(c => c.Kind == ContentSummaryKind.Video).Kind);
    }

    [Fact]
    public void GetDailyDigest_returns_null_when_none_was_generated_yet()
    {
        Assert.Null(_summaries.GetDailyDigest(new DateOnly(2026, 10, 27)));
    }

    [Fact]
    public void SaveDailyDigest_then_GetDailyDigest_roundtrips_and_overwrites_the_same_date()
    {
        var date = new DateOnly(2026, 10, 27);
        _summaries.SaveDailyDigest(date, """{"frase":"Primero"}""", "hash-1", "gemini-test");

        _summaries.SaveDailyDigest(date, """{"frase":"Segundo"}""", "hash-2", "gemini-test");

        var digest = _summaries.GetDailyDigest(date);
        Assert.NotNull(digest);
        Assert.Equal("Segundo", digest.Frase);
        Assert.Equal("hash-2", digest.ItemsHash);
    }

    private ContentItem AddGitHubItem(string externalId)
    {
        var source = _sources.TryAdd(SourceType.GitHub, $"org/{externalId}", $"org/{externalId}", $"https://github.com/org/{externalId}")!;
        return _items.TryAdd(source.Id, externalId, externalId, $"https://github.com/org/{externalId}/releases/{externalId}")!;
    }

    private ContentItem AddYouTubeItem(string externalId)
    {
        var source = _sources.TryAdd(SourceType.YouTube, $"channel-{externalId}", $"Channel {externalId}", $"https://youtube.com/{externalId}")!;
        return _items.TryAdd(source.Id, externalId, externalId, $"https://youtube.com/watch?v={externalId}")!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
