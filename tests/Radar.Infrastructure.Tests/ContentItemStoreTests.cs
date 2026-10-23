using Microsoft.Data.Sqlite;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class ContentItemStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly ContentItemStore _store;
    private readonly long _sourceId;

    public ContentItemStoreTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _database.Initialize();
        _store = new ContentItemStore(_database);
        _sourceId = AddSource("example-org/example-repo");
    }

    [Fact]
    public void TryAdd_then_GetBySource_returns_the_stored_item_unread()
    {
        var publishedAtUtc = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc);

        var added = _store.TryAdd(_sourceId, "release-1", "Release 1", "https://example.invalid/release-1", "First release", publishedAtUtc);
        var items = _store.GetBySource(_sourceId);

        Assert.NotNull(added);
        var item = Assert.Single(items);
        Assert.Equal(added, item);
        Assert.False(item.IsRead);
        Assert.Equal(publishedAtUtc, item.PublishedAtUtc);
        Assert.Equal(DateTimeKind.Utc, item.DetectedAtUtc.Kind);
    }

    [Fact]
    public void Optional_fields_can_be_null()
    {
        _store.TryAdd(_sourceId, "video-1", "Video 1", "https://example.invalid/video-1");

        var item = Assert.Single(_store.GetBySource(_sourceId));

        Assert.Null(item.Description);
        Assert.Null(item.PublishedAtUtc);
    }

    [Fact]
    public void TryAdd_with_same_source_and_external_id_does_not_create_a_duplicate()
    {
        var first = _store.TryAdd(_sourceId, "release-1", "Release 1", "https://example.invalid/release-1");

        var second = _store.TryAdd(_sourceId, "release-1", "Release 1 (again)", "https://example.invalid/release-1");

        Assert.Null(second);
        Assert.Equal(first, Assert.Single(_store.GetBySource(_sourceId)));
    }

    [Fact]
    public void Items_belong_to_their_source()
    {
        var otherSourceId = AddSource("example-org/other-repo");

        _store.TryAdd(_sourceId, "release-1", "Release 1", "https://example.invalid/a/release-1");
        var otherItem = _store.TryAdd(otherSourceId, "release-1", "Other release 1", "https://example.invalid/b/release-1");

        Assert.NotNull(otherItem);
        Assert.Equal(_sourceId, Assert.Single(_store.GetBySource(_sourceId)).SourceId);
        Assert.Equal(otherSourceId, Assert.Single(_store.GetBySource(otherSourceId)).SourceId);
    }

    [Fact]
    public void GetBySource_returns_most_recently_detected_first()
    {
        _store.TryAdd(_sourceId, "release-1", "Release 1", "https://example.invalid/release-1");
        _store.TryAdd(_sourceId, "release-2", "Release 2", "https://example.invalid/release-2");

        var items = _store.GetBySource(_sourceId);

        Assert.Equal(["release-2", "release-1"], items.Select(i => i.ExternalId));
    }

    [Fact]
    public void MarkAsRead_sets_IsRead()
    {
        var added = _store.TryAdd(_sourceId, "release-1", "Release 1", "https://example.invalid/release-1")!;

        var updated = _store.MarkAsRead(added.Id);

        Assert.True(updated);
        Assert.True(Assert.Single(_store.GetBySource(_sourceId)).IsRead);
    }

    [Fact]
    public void MarkAsRead_returns_false_when_item_does_not_exist()
    {
        Assert.False(_store.MarkAsRead(999));
    }

    [Fact]
    public void TryAdd_for_a_missing_source_throws_and_stores_nothing()
    {
        const long missingSourceId = 999;

        var exception = Assert.Throws<SqliteException>(
            () => _store.TryAdd(missingSourceId, "release-1", "Release 1", "https://example.invalid/release-1"));

        Assert.Equal(19, exception.SqliteErrorCode); // SQLITE_CONSTRAINT (foreign key)
        Assert.Empty(_store.GetBySource(missingSourceId));
    }

    [Fact]
    public void GetAll_returns_items_of_all_sources_newest_publication_first()
    {
        var otherSourceId = AddSource("example-org/other-repo");
        _store.TryAdd(_sourceId, "old", "Old", "https://example.invalid/old", publishedAtUtc: Utc(2026, 1, 1));
        _store.TryAdd(otherSourceId, "newest", "Newest", "https://example.invalid/newest", publishedAtUtc: Utc(2026, 9, 1));
        _store.TryAdd(_sourceId, "middle", "Middle", "https://example.invalid/middle", publishedAtUtc: Utc(2026, 5, 1));

        var items = _store.GetAll();

        Assert.Equal(["newest", "middle", "old"], items.Select(i => i.ExternalId));
        Assert.Equal([otherSourceId, _sourceId, _sourceId], items.Select(i => i.SourceId));
    }

    [Fact]
    public void GetAll_orders_items_without_publication_date_by_detection_date()
    {
        _store.TryAdd(_sourceId, "published-2020", "Published 2020", "https://example.invalid/2020", publishedAtUtc: Utc(2020, 1, 1));
        _store.TryAdd(_sourceId, "unpublished", "Detected now", "https://example.invalid/now");

        Assert.Equal(["unpublished", "published-2020"], _store.GetAll().Select(i => i.ExternalId));
    }

    [Fact]
    public void GetAll_reflects_MarkAsRead()
    {
        var unread = _store.TryAdd(_sourceId, "unread", "Unread", "https://example.invalid/unread", publishedAtUtc: Utc(2026, 1, 1))!;
        var read = _store.TryAdd(_sourceId, "read", "Read", "https://example.invalid/read", publishedAtUtc: Utc(2026, 2, 1))!;

        _store.MarkAsRead(read.Id);

        var items = _store.GetAll().ToDictionary(i => i.Id);
        Assert.True(items[read.Id].IsRead);
        Assert.False(items[unread.Id].IsRead);
    }

    [Fact]
    public void GetAll_returns_empty_list_when_there_are_no_items()
    {
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void SaveExplanation_then_GetExplanation_returns_it()
    {
        var item = _store.TryAdd(_sourceId, "release-1", "Release 1", "https://example.invalid/release-1", "Notes")!;

        var saved = _store.SaveExplanation(item.Id, "Explicación de prueba", "gemini-test-model");
        var loaded = _store.GetExplanation(item.Id);

        Assert.Equal(saved, loaded);
        Assert.Equal("Explicación de prueba", loaded!.Text);
        Assert.Equal("gemini-test-model", loaded.Model);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
    }

    [Fact]
    public void GetExplanation_returns_null_when_none_was_saved()
    {
        var item = _store.TryAdd(_sourceId, "release-1", "Release 1", "https://example.invalid/release-1")!;

        Assert.Null(_store.GetExplanation(item.Id));
    }

    [Fact]
    public void SaveExplanation_replaces_a_previous_explanation_without_duplicates()
    {
        var item = _store.TryAdd(_sourceId, "release-1", "Release 1", "https://example.invalid/release-1")!;

        _store.SaveExplanation(item.Id, "Primera", "model-a");
        _store.SaveExplanation(item.Id, "Segunda", "model-b");

        Assert.Equal("Segunda", _store.GetExplanation(item.Id)!.Text);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ContentExplanations;";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void SaveExplanation_for_a_missing_content_item_throws()
    {
        var exception = Assert.Throws<SqliteException>(() => _store.SaveExplanation(999, "Texto", "model"));

        Assert.Equal(19, exception.SqliteErrorCode); // SQLITE_CONSTRAINT (foreign key)
    }

    [Fact]
    public void Saving_an_explanation_does_not_change_the_content_item()
    {
        var item = _store.TryAdd(_sourceId, "release-1", "Release 1", "https://example.invalid/release-1", "Notes")!;

        _store.SaveExplanation(item.Id, "Explicación", "model");

        Assert.Equal(item, Assert.Single(_store.GetBySource(_sourceId)));
    }

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private long AddSource(string externalId) =>
        new SourceStore(_database).TryAdd(SourceType.GitHub, externalId, externalId, $"https://github.com/{externalId}")!.Id;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
