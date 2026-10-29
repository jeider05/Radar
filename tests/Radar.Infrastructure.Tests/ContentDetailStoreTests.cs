using Microsoft.Data.Sqlite;
using Radar.Core.Contents;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class ContentDetailStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests.Details", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _items;
    private readonly ContentDetailStore _details;

    public ContentDetailStoreTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _database.Initialize();
        _sources = new SourceStore(_database);
        _items = new ContentItemStore(_database);
        _details = new ContentDetailStore(_database);
    }

    [Fact]
    public void SaveReady_then_Get_returns_the_stored_ficha()
    {
        var item = AddGitHubItem("v1");

        var saved = _details.SaveReady(item.Id, """{"resumen":"x"}""", promptVersion: 1, model: "gemini-test");

        var loaded = _details.Get(item.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ContentSummaryStatus.Ready, loaded.Status);
        Assert.Equal("""{"resumen":"x"}""", loaded.Json);
        Assert.Equal(saved.CreatedAtUtc, loaded.CreatedAtUtc);
    }

    [Fact]
    public void SaveError_then_Get_returns_error_status_and_message()
    {
        var item = AddGitHubItem("v1");

        _details.SaveError(item.Id, promptVersion: 1, model: "gemini-test", error: "no válido");

        var loaded = _details.Get(item.Id);
        Assert.Equal(ContentSummaryStatus.Error, loaded!.Status);
        Assert.Equal("no válido", loaded.Error);
    }

    [Fact]
    public void GetCandidates_returns_items_without_a_ficha_yet()
    {
        var item = AddGitHubItem("v1");

        var candidates = _details.GetCandidates(promptVersion: 1, onlyUnread: false);

        Assert.Equal([item.Id], candidates.Select(c => c.Item.Id));
    }

    [Fact]
    public void GetCandidates_excludes_items_already_generated_with_the_current_prompt_version()
    {
        var item = AddGitHubItem("v1");
        _details.SaveReady(item.Id, """{"resumen":"x"}""", promptVersion: 2, model: "gemini-test");

        var candidates = _details.GetCandidates(promptVersion: 2, onlyUnread: false);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GetCandidates_includes_items_generated_with_an_older_prompt_version()
    {
        var item = AddGitHubItem("v1");
        _details.SaveReady(item.Id, """{"resumen":"x"}""", promptVersion: 1, model: "gemini-test");

        var candidates = _details.GetCandidates(promptVersion: 2, onlyUnread: false);

        Assert.Single(candidates);
    }

    [Fact]
    public void GetCandidates_excludes_YouTube_sources()
    {
        var source = _sources.TryAdd(SourceType.YouTube, "channel", "Channel", "https://youtube.com/channel")!;
        _items.TryAdd(source.Id, "vid1", "Video 1", "https://youtube.com/watch?v=vid1");

        var candidates = _details.GetCandidates(promptVersion: 1, onlyUnread: false);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GetCandidates_respects_only_unread()
    {
        var unread = AddGitHubItem("v1");
        var read = AddGitHubItem("v2");
        _items.MarkAsRead(read.Id);

        var candidates = _details.GetCandidates(promptVersion: 1, onlyUnread: true);

        Assert.Equal([unread.Id], candidates.Select(c => c.Item.Id));
    }

    private ContentItem AddGitHubItem(string externalId)
    {
        var source = _sources.TryAdd(SourceType.GitHub, $"org/{externalId}", $"org/{externalId}", $"https://github.com/org/{externalId}")!;
        return _items.TryAdd(source.Id, externalId, externalId, $"https://github.com/org/{externalId}/releases/{externalId}")!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
