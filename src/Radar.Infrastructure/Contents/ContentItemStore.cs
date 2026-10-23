using System.Globalization;
using Microsoft.Data.Sqlite;
using Radar.Core.Contents;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Contents;

public sealed class ContentItemStore(RadarDatabase database)
{
    /// <summary>
    /// Inserts a new content item, initially unread. Returns null when the source already has an item with the same external id.
    /// Throws <see cref="SqliteException"/> when <paramref name="sourceId"/> does not exist.
    /// </summary>
    public ContentItem? TryAdd(
        long sourceId, string externalId, string title, string url, string? description = null, DateTime? publishedAtUtc = null)
    {
        var detectedAtUtc = DateTime.UtcNow;
        publishedAtUtc = publishedAtUtc?.ToUniversalTime();

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ContentItems (SourceId, ExternalId, Title, Url, Description, PublishedAtUtc, DetectedAtUtc, IsRead)
            VALUES ($sourceId, $externalId, $title, $url, $description, $publishedAtUtc, $detectedAtUtc, 0)
            ON CONFLICT (SourceId, ExternalId) DO NOTHING
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$sourceId", sourceId);
        command.Parameters.AddWithValue("$externalId", externalId);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$description", (object?)description ?? DBNull.Value);
        command.Parameters.AddWithValue("$publishedAtUtc", publishedAtUtc is { } published ? Format(published) : DBNull.Value);
        command.Parameters.AddWithValue("$detectedAtUtc", Format(detectedAtUtc));

        if (command.ExecuteScalar() is not long id)
        {
            return null;
        }

        return new ContentItem(id, sourceId, externalId, title, url, description, publishedAtUtc, detectedAtUtc, IsRead: false);
    }

    /// <summary>Returns the content items of a source, most recently detected first.</summary>
    public IReadOnlyList<ContentItem> GetBySource(long sourceId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceId, ExternalId, Title, Url, Description, PublishedAtUtc, DetectedAtUtc, IsRead
            FROM ContentItems
            WHERE SourceId = $sourceId
            ORDER BY Id DESC;
            """;
        command.Parameters.AddWithValue("$sourceId", sourceId);
        return ReadAll(command);
    }

    /// <summary>Returns the content items of all sources, newest first by publication date (detection date when unknown).</summary>
    public IReadOnlyList<ContentItem> GetAll()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        // Dates are stored as round-trip UTC strings, so text order matches chronological order.
        command.CommandText = """
            SELECT Id, SourceId, ExternalId, Title, Url, Description, PublishedAtUtc, DetectedAtUtc, IsRead
            FROM ContentItems
            ORDER BY COALESCE(PublishedAtUtc, DetectedAtUtc) DESC, Id DESC;
            """;
        return ReadAll(command);
    }

    private static List<ContentItem> ReadAll(SqliteCommand command)
    {
        var items = new List<ContentItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(Read(reader));
        }

        return items;
    }

    /// <summary>Marks a content item as read. Returns false when the item does not exist.</summary>
    public bool MarkAsRead(long contentItemId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ContentItems SET IsRead = 1 WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", contentItemId);
        return command.ExecuteNonQuery() > 0;
    }

    private static string Format(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime Parse(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static ContentItem Read(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetInt64(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : Parse(reader.GetString(6)),
        Parse(reader.GetString(7)),
        reader.GetBoolean(8));
}
