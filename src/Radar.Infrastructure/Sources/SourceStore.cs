using System.Globalization;
using Microsoft.Data.Sqlite;
using Radar.Core.Sources;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Sources;

public enum SourceDeleteResult
{
    Deleted,
    HasContent,
    NotFound,
}

public sealed class SourceStore(RadarDatabase database)
{
    /// <summary>
    /// Inserts a new source. Returns null when a source with the same type and external id already exists.
    /// </summary>
    public Source? TryAdd(SourceType sourceType, string externalId, string name, string url, bool isActive = true)
    {
        var createdAtUtc = DateTime.UtcNow;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Sources (SourceType, ExternalId, Name, Url, IsActive, CreatedAtUtc)
            VALUES ($sourceType, $externalId, $name, $url, $isActive, $createdAtUtc)
            ON CONFLICT (SourceType, ExternalId) DO NOTHING
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$sourceType", sourceType.ToString());
        command.Parameters.AddWithValue("$externalId", externalId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$isActive", isActive);
        command.Parameters.AddWithValue("$createdAtUtc", createdAtUtc.ToString("O", CultureInfo.InvariantCulture));

        if (command.ExecuteScalar() is not long id)
        {
            return null;
        }

        return new Source(id, sourceType, externalId, name, url, isActive, createdAtUtc);
    }

    public Source? Find(SourceType sourceType, string externalId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceType, ExternalId, Name, Url, IsActive, CreatedAtUtc
            FROM Sources
            WHERE SourceType = $sourceType AND ExternalId = $externalId;
            """;
        command.Parameters.AddWithValue("$sourceType", sourceType.ToString());
        command.Parameters.AddWithValue("$externalId", externalId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<Source> GetActive(SourceType sourceType)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceType, ExternalId, Name, Url, IsActive, CreatedAtUtc
            FROM Sources
            WHERE SourceType = $sourceType AND IsActive = 1
            ORDER BY Id;
            """;
        command.Parameters.AddWithValue("$sourceType", sourceType.ToString());
        return ReadAll(command);
    }

    /// <summary>Returns every registered source, active or not, in registration order.</summary>
    public IReadOnlyList<Source> GetAll()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceType, ExternalId, Name, Url, IsActive, CreatedAtUtc
            FROM Sources
            ORDER BY Id;
            """;
        return ReadAll(command);
    }

    /// <summary>Pauses or resumes monitoring of a source. Returns false when the source does not exist.</summary>
    public bool SetActive(long sourceId, bool isActive)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Sources SET IsActive = $isActive WHERE Id = $id;";
        command.Parameters.AddWithValue("$isActive", isActive);
        command.Parameters.AddWithValue("$id", sourceId);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Deletes a source only when it has no content items, so detected history is never lost.
    /// Sources with content should be paused instead.
    /// </summary>
    public SourceDeleteResult TryDelete(long sourceId)
    {
        using var connection = database.OpenConnection();
        // IMMEDIATE: the Worker cannot add content between the check and the delete.
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (SELECT 1 FROM Sources WHERE Id = $id),
                   EXISTS (SELECT 1 FROM ContentItems WHERE SourceId = $id);
            """;
        command.Parameters.AddWithValue("$id", sourceId);

        bool exists, hasContent;
        using (var reader = command.ExecuteReader())
        {
            reader.Read();
            exists = reader.GetBoolean(0);
            hasContent = reader.GetBoolean(1);
        }

        if (!exists)
        {
            return SourceDeleteResult.NotFound;
        }

        if (hasContent)
        {
            return SourceDeleteResult.HasContent;
        }

        command.CommandText = "DELETE FROM Sources WHERE Id = $id;";
        command.ExecuteNonQuery();
        transaction.Commit();
        return SourceDeleteResult.Deleted;
    }

    private static List<Source> ReadAll(SqliteCommand command)
    {
        var sources = new List<Source>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sources.Add(Read(reader));
        }

        return sources;
    }

    private static Source Read(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        Enum.Parse<SourceType>(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetBoolean(5),
        DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
