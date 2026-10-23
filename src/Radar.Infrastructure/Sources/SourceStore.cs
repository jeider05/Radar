using System.Globalization;
using Microsoft.Data.Sqlite;
using Radar.Core.Sources;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Sources;

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
