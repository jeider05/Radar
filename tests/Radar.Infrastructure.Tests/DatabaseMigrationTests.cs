using Microsoft.Data.Sqlite;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Tests;

public sealed class DatabaseMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;

    public DatabaseMigrationTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
    }

    [Fact]
    public void Initialize_upgrades_a_version_0_database_and_keeps_existing_sources()
    {
        // Schema exactly as created by the first release (user_version 0, Sources only).
        Directory.CreateDirectory(_directory);
        Execute("""
            CREATE TABLE Sources (
                Id           INTEGER PRIMARY KEY,
                SourceType   TEXT    NOT NULL,
                ExternalId   TEXT    NOT NULL,
                Name         TEXT    NOT NULL,
                Url          TEXT    NOT NULL,
                IsActive     INTEGER NOT NULL,
                CreatedAtUtc TEXT    NOT NULL,
                UNIQUE (SourceType, ExternalId)
            );
            INSERT INTO Sources (SourceType, ExternalId, Name, Url, IsActive, CreatedAtUtc)
            VALUES ('GitHub', 'example-org/example-repo', 'Example repo', 'https://github.com/example-org/example-repo', 1, '2026-01-01T00:00:00.0000000Z');
            """);

        _database.Initialize();

        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM Sources WHERE ExternalId = 'example-org/example-repo';"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ContentItems';"));
        Assert.Equal(RadarDatabase.SchemaVersion, Scalar("PRAGMA user_version;"));
    }

    [Fact]
    public void Initialize_on_a_new_database_reaches_the_current_schema_version()
    {
        _database.Initialize();

        Assert.Equal(RadarDatabase.SchemaVersion, Scalar("PRAGMA user_version;"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ContentItems';"));
    }

    [Fact]
    public void Connections_have_foreign_keys_enabled()
    {
        _database.Initialize();

        Assert.Equal(1L, Scalar("PRAGMA foreign_keys;"));
    }

    private void Execute(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
