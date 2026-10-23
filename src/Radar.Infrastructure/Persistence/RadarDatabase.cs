using Microsoft.Data.Sqlite;

namespace Radar.Infrastructure.Persistence;

/// <summary>
/// Single place that defines where the Radar SQLite database lives and how its schema is created.
/// Shared by Desktop and Worker.
/// </summary>
public sealed class RadarDatabase
{
    private readonly string _connectionString;

    public RadarDatabase(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
    }

    public string DatabasePath { get; }

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

    /// <summary>Creates the database file and schema if they do not exist yet. Safe to call repeatedly.</summary>
    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
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
            """;
        command.ExecuteNonQuery();
    }
}
