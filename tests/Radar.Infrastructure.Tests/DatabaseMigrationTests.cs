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
    public void Initialize_upgrades_a_version_2_database_keeping_sources_contents_and_read_state()
    {
        // Schema exactly as created by migrations 1 and 2 (user_version 2), with data.
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
            CREATE TABLE ContentItems (
                Id             INTEGER PRIMARY KEY,
                SourceId       INTEGER NOT NULL REFERENCES Sources (Id),
                ExternalId     TEXT    NOT NULL,
                Title          TEXT    NOT NULL,
                Url            TEXT    NOT NULL,
                Description    TEXT    NULL,
                PublishedAtUtc TEXT    NULL,
                DetectedAtUtc  TEXT    NOT NULL,
                IsRead         INTEGER NOT NULL DEFAULT 0,
                UNIQUE (SourceId, ExternalId)
            );
            INSERT INTO Sources (Id, SourceType, ExternalId, Name, Url, IsActive, CreatedAtUtc)
            VALUES (1, 'GitHub', 'example-org/example-repo', 'Example repo', 'https://github.com/example-org/example-repo', 0, '2026-01-01T00:00:00.0000000Z');
            INSERT INTO ContentItems (SourceId, ExternalId, Title, Url, Description, PublishedAtUtc, DetectedAtUtc, IsRead)
            VALUES (1, '111', 'v1', 'https://example.invalid/v1', 'Notes', NULL, '2026-01-02T00:00:00.0000000Z', 1);
            PRAGMA user_version = 2;
            """);

        _database.Initialize();

        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM Sources WHERE ExternalId = 'example-org/example-repo' AND IsActive = 0;"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM ContentItems WHERE ExternalId = '111' AND IsRead = 1 AND Description = 'Notes';"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ContentExplanations';"));
        Assert.Equal(RadarDatabase.SchemaVersion, Scalar("PRAGMA user_version;"));
    }

    [Fact]
    public void Initialize_upgrades_a_version_3_database_keeping_explanations_and_adding_the_study_tables()
    {
        // Schema exactly as created by migrations 1 to 3 (user_version 3), with data.
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
            CREATE TABLE ContentItems (
                Id             INTEGER PRIMARY KEY,
                SourceId       INTEGER NOT NULL REFERENCES Sources (Id),
                ExternalId     TEXT    NOT NULL,
                Title          TEXT    NOT NULL,
                Url            TEXT    NOT NULL,
                Description    TEXT    NULL,
                PublishedAtUtc TEXT    NULL,
                DetectedAtUtc  TEXT    NOT NULL,
                IsRead         INTEGER NOT NULL DEFAULT 0,
                UNIQUE (SourceId, ExternalId)
            );
            CREATE TABLE ContentExplanations (
                ContentItemId INTEGER PRIMARY KEY REFERENCES ContentItems (Id),
                Text          TEXT    NOT NULL,
                Model         TEXT    NOT NULL,
                CreatedAtUtc  TEXT    NOT NULL
            );
            INSERT INTO Sources (Id, SourceType, ExternalId, Name, Url, IsActive, CreatedAtUtc)
            VALUES (1, 'GitHub', 'example-org/example-repo', 'Example repo', 'https://github.com/example-org/example-repo', 1, '2026-01-01T00:00:00.0000000Z');
            INSERT INTO ContentItems (Id, SourceId, ExternalId, Title, Url, Description, PublishedAtUtc, DetectedAtUtc, IsRead)
            VALUES (1, 1, '111', 'v1', 'https://example.invalid/v1', 'Notes', NULL, '2026-01-02T00:00:00.0000000Z', 1);
            INSERT INTO ContentExplanations (ContentItemId, Text, Model, CreatedAtUtc)
            VALUES (1, 'Resumen', 'gemini-test', '2026-01-03T00:00:00.0000000Z');
            PRAGMA user_version = 3;
            """);

        _database.Initialize();

        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM Sources;"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM ContentItems WHERE IsRead = 1;"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM ContentExplanations WHERE Text = 'Resumen';"));
        Assert.Equal(5L, Scalar("""
            SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'
            AND name IN ('StudyTopics', 'StudyQuestions', 'StudyTopicProgress', 'ExamAttempts', 'ExamAnswers');
            """));
        Assert.Equal(RadarDatabase.SchemaVersion, Scalar("PRAGMA user_version;"));
    }

    [Fact]
    public void Initialize_upgrades_a_version_4_database_keeping_study_data_and_adding_summary_tables()
    {
        // Schema exactly as created by migrations 1 to 4 (user_version 4), with data.
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
            CREATE TABLE ContentItems (
                Id             INTEGER PRIMARY KEY,
                SourceId       INTEGER NOT NULL REFERENCES Sources (Id),
                ExternalId     TEXT    NOT NULL,
                Title          TEXT    NOT NULL,
                Url            TEXT    NOT NULL,
                Description    TEXT    NULL,
                PublishedAtUtc TEXT    NULL,
                DetectedAtUtc  TEXT    NOT NULL,
                IsRead         INTEGER NOT NULL DEFAULT 0,
                UNIQUE (SourceId, ExternalId)
            );
            CREATE TABLE ContentExplanations (
                ContentItemId INTEGER PRIMARY KEY REFERENCES ContentItems (Id),
                Text          TEXT    NOT NULL,
                Model         TEXT    NOT NULL,
                CreatedAtUtc  TEXT    NOT NULL
            );
            CREATE TABLE StudyTopics (
                Id              INTEGER PRIMARY KEY,
                Key             TEXT    NOT NULL UNIQUE,
                Category        TEXT    NOT NULL,
                Title           TEXT    NOT NULL,
                SortOrder       INTEGER NOT NULL,
                WhatItIs        TEXT    NOT NULL,
                WhatItIsFor     TEXT    NOT NULL,
                HowItWorks      TEXT    NOT NULL,
                Example         TEXT    NOT NULL,
                Differences     TEXT    NOT NULL,
                ConfirmedFacts  TEXT    NOT NULL,
                SourceUrl       TEXT    NOT NULL,
                SourceReference TEXT    NOT NULL
            );
            INSERT INTO Sources (Id, SourceType, ExternalId, Name, Url, IsActive, CreatedAtUtc)
            VALUES (1, 'GitHub', 'example-org/example-repo', 'Example repo', 'https://github.com/example-org/example-repo', 1, '2026-01-01T00:00:00.0000000Z');
            INSERT INTO ContentItems (Id, SourceId, ExternalId, Title, Url, Description, PublishedAtUtc, DetectedAtUtc, IsRead)
            VALUES (1, 1, '111', 'v1', 'https://example.invalid/v1', 'Notes', NULL, '2026-01-02T00:00:00.0000000Z', 1);
            INSERT INTO ContentExplanations (ContentItemId, Text, Model, CreatedAtUtc)
            VALUES (1, 'Resumen', 'gemini-test', '2026-01-03T00:00:00.0000000Z');
            PRAGMA user_version = 4;
            """);

        _database.Initialize();

        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM Sources;"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM ContentExplanations WHERE Text = 'Resumen';"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ContentSummaries';"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DailyDigests';"));
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

    [Fact]
    public async Task Concurrent_initialization_from_two_processes_applies_each_migration_once()
    {
        // Desktop and Worker both initialize the same file on first start.
        var path = _database.DatabasePath;
        Directory.CreateDirectory(_directory);

        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => new RadarDatabase(path).Initialize()));
        await Task.WhenAll(tasks);

        Assert.Equal(RadarDatabase.SchemaVersion, Scalar("PRAGMA user_version;"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ContentItems';"));
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
