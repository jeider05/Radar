using Microsoft.Data.Sqlite;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class SourceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly SourceStore _store;

    public SourceStoreTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _store = new SourceStore(_database);
    }

    [Fact]
    public void Initialize_creates_database_file_and_sources_table()
    {
        _database.Initialize();

        Assert.True(File.Exists(_database.DatabasePath));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Sources';";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void Initialize_is_safe_to_call_twice()
    {
        _database.Initialize();
        _store.TryAdd(SourceType.GitHub, "example-org/example-repo", "Example repo", "https://github.com/example-org/example-repo");

        _database.Initialize();

        Assert.NotNull(_store.Find(SourceType.GitHub, "example-org/example-repo"));
    }

    [Fact]
    public void TryAdd_then_Find_returns_the_stored_source()
    {
        _database.Initialize();

        var added = _store.TryAdd(SourceType.YouTube, "UC_test_channel", "Test channel", "https://www.youtube.com/channel/UC_test_channel");
        var found = _store.Find(SourceType.YouTube, "UC_test_channel");

        Assert.NotNull(added);
        Assert.Equal(added, found);
        Assert.True(found!.IsActive);
        Assert.Equal(DateTimeKind.Utc, found.CreatedAtUtc.Kind);
    }

    [Fact]
    public void TryAdd_with_same_type_and_external_id_does_not_create_a_duplicate()
    {
        _database.Initialize();
        var first = _store.TryAdd(SourceType.GitHub, "example-org/example-repo", "Example repo", "https://github.com/example-org/example-repo");

        var second = _store.TryAdd(SourceType.GitHub, "example-org/example-repo", "Renamed", "https://github.com/example-org/example-repo");

        Assert.Null(second);
        Assert.Equal(1L, CountSources());
        Assert.Equal(first, _store.Find(SourceType.GitHub, "example-org/example-repo"));
    }

    [Fact]
    public void Same_external_id_with_different_type_is_a_different_source()
    {
        _database.Initialize();

        _store.TryAdd(SourceType.GitHub, "shared-id", "GitHub source", "https://github.com/shared-id");
        var youtube = _store.TryAdd(SourceType.YouTube, "shared-id", "YouTube source", "https://www.youtube.com/@shared-id");

        Assert.NotNull(youtube);
        Assert.Equal(2L, CountSources());
    }

    [Fact]
    public void Find_returns_null_when_source_does_not_exist()
    {
        _database.Initialize();

        Assert.Null(_store.Find(SourceType.GitHub, "missing"));
    }

    [Fact]
    public void GetActive_returns_only_active_sources_of_the_requested_type()
    {
        _database.Initialize();
        _store.TryAdd(SourceType.GitHub, "example-org/active", "Active", "https://github.com/example-org/active");
        _store.TryAdd(SourceType.GitHub, "example-org/inactive", "Inactive", "https://github.com/example-org/inactive", isActive: false);
        _store.TryAdd(SourceType.YouTube, "UC_test_channel", "Channel", "https://www.youtube.com/channel/UC_test_channel");

        var active = _store.GetActive(SourceType.GitHub);

        Assert.Equal(["example-org/active"], active.Select(s => s.ExternalId));
    }

    [Fact]
    public void GetAll_returns_active_and_inactive_sources_of_every_type()
    {
        _database.Initialize();
        _store.TryAdd(SourceType.GitHub, "example-org/active", "Active", "https://github.com/example-org/active");
        _store.TryAdd(SourceType.GitHub, "example-org/inactive", "Inactive", "https://github.com/example-org/inactive", isActive: false);
        _store.TryAdd(SourceType.YouTube, "UC_test_channel", "Channel", "https://www.youtube.com/channel/UC_test_channel");

        var all = _store.GetAll();

        Assert.Equal(["example-org/active", "example-org/inactive", "UC_test_channel"], all.Select(s => s.ExternalId));
    }

    [Fact]
    public void SetActive_pauses_and_resumes_a_source()
    {
        _database.Initialize();
        var source = _store.TryAdd(SourceType.GitHub, "example-org/example-repo", "Example repo", "https://github.com/example-org/example-repo")!;

        Assert.True(_store.SetActive(source.Id, false));
        Assert.False(_store.Find(SourceType.GitHub, "example-org/example-repo")!.IsActive);
        Assert.Empty(_store.GetActive(SourceType.GitHub));

        Assert.True(_store.SetActive(source.Id, true));
        Assert.True(_store.Find(SourceType.GitHub, "example-org/example-repo")!.IsActive);
        Assert.Single(_store.GetActive(SourceType.GitHub));
    }

    [Fact]
    public void SetActive_returns_false_when_source_does_not_exist()
    {
        _database.Initialize();

        Assert.False(_store.SetActive(999, false));
    }

    [Fact]
    public void Delete_removes_a_source_without_content()
    {
        _database.Initialize();
        var source = _store.TryAdd(SourceType.GitHub, "example-org/example-repo", "Example repo", "https://github.com/example-org/example-repo")!;

        Assert.True(_store.Delete(source.Id));
        Assert.Null(_store.Find(SourceType.GitHub, "example-org/example-repo"));
    }

    [Fact]
    public void Delete_removes_a_source_with_all_its_data_and_keeps_other_sources()
    {
        _database.Initialize();
        var source = _store.TryAdd(SourceType.GitHub, "example-org/example-repo", "Example repo", "https://github.com/example-org/example-repo")!;
        var other = _store.TryAdd(SourceType.GitHub, "example-org/other-repo", "Other repo", "https://github.com/example-org/other-repo")!;
        var contentItems = new ContentItemStore(_database);
        contentItems.TryAdd(source.Id, "release-1", "Release 1", "https://example.invalid/release-1");
        contentItems.TryAdd(other.Id, "release-1", "Release 1", "https://example.invalid/other/release-1");
        foreach (var table in new[] { "ContentSummaries", "ContentDetails", "ContentExplanations" })
        {
            InsertDerivedRows(table);
        }

        Assert.True(_store.Delete(source.Id));

        Assert.Null(_store.Find(SourceType.GitHub, "example-org/example-repo"));
        Assert.Empty(contentItems.GetBySource(source.Id));
        Assert.Single(contentItems.GetBySource(other.Id));
        foreach (var table in new[] { "ContentSummaries", "ContentDetails", "ContentExplanations" })
        {
            Assert.Equal(1L, CountRows(table));
        }
    }

    [Fact]
    public void Delete_returns_false_when_source_does_not_exist()
    {
        _database.Initialize();

        Assert.False(_store.Delete(999));
    }

    // Gives every content item one row in a derived table, filling only the columns that must not be null.
    private void InsertDerivedRows(string table)
    {
        using var connection = _database.OpenConnection();
        using var columns = connection.CreateCommand();
        columns.CommandText = $"SELECT name, type FROM pragma_table_info('{table}') WHERE \"notnull\" = 1 AND name <> 'ContentItemId';";
        var names = new List<string>();
        var values = new List<string>();
        using (var reader = columns.ExecuteReader())
        {
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
                values.Add(reader.GetString(1).Equals("INTEGER", StringComparison.OrdinalIgnoreCase) ? "1" : "'x'");
            }
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO {table} (ContentItemId, {string.Join(", ", names)}) " +
                             $"SELECT Id, {string.Join(", ", values)} FROM ContentItems;";
        insert.ExecuteNonQuery();
    }

    private long CountRows(string table)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)command.ExecuteScalar()!;
    }

    private long CountSources()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Sources;";
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        // Pooled connections keep the file open; release them so the temp folder can be deleted.
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
