using Microsoft.Data.Sqlite;
using Radar.Core.Sources;
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
