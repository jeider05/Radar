using Microsoft.Data.Sqlite;

namespace Radar.Infrastructure.Persistence;

/// <summary>
/// Single place that defines where the Radar SQLite database lives and how its schema is created.
/// Shared by Desktop and Worker.
/// </summary>
public sealed class RadarDatabase
{
    /// <summary>
    /// Ordered schema migrations. Migration N upgrades the database from user_version N-1 to N.
    /// Only append new entries; never edit or reorder existing ones.
    /// </summary>
    private static readonly string[] Migrations =
    [
        // 1: Sources. IF NOT EXISTS because databases created before versioning already have it at user_version 0.
        """
        CREATE TABLE IF NOT EXISTS Sources (
            Id           INTEGER PRIMARY KEY,
            SourceType   TEXT    NOT NULL,
            ExternalId   TEXT    NOT NULL,
            Name         TEXT    NOT NULL,
            Url          TEXT    NOT NULL,
            IsActive     INTEGER NOT NULL,
            CreatedAtUtc TEXT    NOT NULL,
            UNIQUE (SourceType, ExternalId)
        );
        """,

        // 2: ContentItems.
        """
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
        """,

        // 3: ContentExplanations. One AI explanation per content item, kept separate from the detected content.
        """
        CREATE TABLE ContentExplanations (
            ContentItemId INTEGER PRIMARY KEY REFERENCES ContentItems (Id),
            Text          TEXT    NOT NULL,
            Model         TEXT    NOT NULL,
            CreatedAtUtc  TEXT    NOT NULL
        );
        """,

        // 4: Study module. Topic and question content is separate from the user's progress and exam history.
        """
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
        CREATE TABLE StudyQuestions (
            Id            INTEGER PRIMARY KEY,
            TopicId       INTEGER NOT NULL REFERENCES StudyTopics (Id),
            Kind          TEXT    NOT NULL,
            Difficulty    TEXT    NOT NULL,
            Prompt        TEXT    NOT NULL,
            OptionsJson   TEXT    NOT NULL,
            CorrectOption INTEGER NOT NULL,
            Explanation   TEXT    NOT NULL,
            Origin        TEXT    NOT NULL,
            Fingerprint   TEXT    NOT NULL UNIQUE,
            CreatedAtUtc  TEXT    NOT NULL
        );
        CREATE TABLE StudyTopicProgress (
            TopicId      INTEGER PRIMARY KEY REFERENCES StudyTopics (Id),
            Status       TEXT    NOT NULL,
            UpdatedAtUtc TEXT    NOT NULL
        );
        CREATE TABLE ExamAttempts (
            Id            INTEGER PRIMARY KEY,
            TopicId       INTEGER NULL REFERENCES StudyTopics (Id),
            Difficulty    TEXT    NOT NULL,
            QuestionCount INTEGER NOT NULL,
            StartedAtUtc  TEXT    NOT NULL,
            FinishedAtUtc TEXT    NULL
        );
        CREATE TABLE ExamAnswers (
            Id             INTEGER PRIMARY KEY,
            AttemptId      INTEGER NOT NULL REFERENCES ExamAttempts (Id),
            QuestionId     INTEGER NOT NULL REFERENCES StudyQuestions (Id),
            SelectedOption INTEGER NOT NULL,
            IsCorrect      INTEGER NOT NULL,
            AnsweredAtUtc  TEXT    NOT NULL,
            UNIQUE (AttemptId, QuestionId)
        );
        """,

        // 5: AI summaries. One short ContentSummary per item, plus one DailyDigest sentence per local date
        // built from that day's already-generated summaries. Separate from ContentExplanations.
        """
        CREATE TABLE ContentSummaries (
            ContentItemId INTEGER PRIMARY KEY REFERENCES ContentItems (Id),
            Kind          TEXT    NOT NULL,
            Json          TEXT    NULL,
            PromptVersion INTEGER NOT NULL,
            Model         TEXT    NOT NULL,
            Status        TEXT    NOT NULL,
            Error         TEXT    NULL,
            CreatedAtUtc  TEXT    NOT NULL
        );
        CREATE TABLE DailyDigests (
            Date         TEXT PRIMARY KEY,
            Json         TEXT NOT NULL,
            ItemsHash    TEXT NOT NULL,
            Model        TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL
        );
        """,

        // 6: Release detail "ficha" (embedded detail view). One row per content item; separate from
        // ContentSummaries because that table already has one row per item for the short list summary.
        """
        CREATE TABLE ContentDetails (
            ContentItemId INTEGER PRIMARY KEY REFERENCES ContentItems (Id),
            Json          TEXT    NULL,
            PromptVersion INTEGER NOT NULL,
            Model         TEXT    NOT NULL,
            Status        TEXT    NOT NULL,
            Error         TEXT    NULL,
            CreatedAtUtc  TEXT    NOT NULL
        );
        """,
    ];

    private readonly string _connectionString;

    public RadarDatabase(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
        }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>Schema version (PRAGMA user_version) that <see cref="Initialize"/> brings the database to.</summary>
    public static long SchemaVersion => Migrations.Length;

    /// <summary>%LOCALAPPDATA%\Radar\radar.db</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Radar", "radar.db");

    public static RadarDatabase CreateDefault() => new(DefaultPath);

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Creates the database file if needed and applies pending migrations. Existing data is kept. Safe to call repeatedly.
    /// </summary>
    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        using var connection = OpenConnection();

        while (true)
        {
            // BEGIN IMMEDIATE takes the write lock before reading the version, so when Desktop and Worker
            // initialize at the same time, each migration is applied by only one of them.
            using var transaction = connection.BeginTransaction(deferred: false);
            var version = GetUserVersion(connection, transaction);
            if (version >= Migrations.Length)
            {
                transaction.Commit();
                return;
            }

            var next = version + 1;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // PRAGMA does not accept parameters; next is an internal integer.
            command.CommandText = $"{Migrations[next - 1]}\nPRAGMA user_version = {next};";
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    private static long GetUserVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        return (long)command.ExecuteScalar()!;
    }
}
