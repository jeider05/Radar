using System.Globalization;
using Microsoft.Data.Sqlite;
using Radar.Core.Contents;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Contents;

public sealed class ContentDetailStore(RadarDatabase database)
{
    /// <summary>Returns the stored "ficha" of a content item, or null when none was generated yet.</summary>
    public ContentDetail? Get(long contentItemId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ContentItemId, Json, PromptVersion, Model, Status, Error, CreatedAtUtc
            FROM ContentDetails
            WHERE ContentItemId = $contentItemId;
            """;
        command.Parameters.AddWithValue("$contentItemId", contentItemId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDetail(reader) : null;
    }

    /// <summary>
    /// Active GitHub content items whose "ficha" is missing or used an older prompt version than
    /// <paramref name="promptVersion"/>. Most recently published (or detected) first.
    /// </summary>
    public IReadOnlyList<ContentItem> GetCandidates(int promptVersion, bool onlyUnread)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ci.Id, ci.SourceId, ci.ExternalId, ci.Title, ci.Url, ci.Description, ci.PublishedAtUtc, ci.DetectedAtUtc, ci.IsRead
            FROM ContentItems ci
            JOIN Sources s ON s.Id = ci.SourceId
            LEFT JOIN ContentDetails cd ON cd.ContentItemId = ci.Id
            WHERE s.SourceType = 'GitHub'
              AND (NOT $onlyUnread OR ci.IsRead = 0)
              AND (cd.ContentItemId IS NULL OR cd.PromptVersion < $promptVersion)
            ORDER BY COALESCE(ci.PublishedAtUtc, ci.DetectedAtUtc) DESC, ci.Id DESC;
            """;
        command.Parameters.AddWithValue("$onlyUnread", onlyUnread);
        command.Parameters.AddWithValue("$promptVersion", promptVersion);

        var items = new List<ContentItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new ContentItem(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : Parse(reader.GetString(6)),
                Parse(reader.GetString(7)), reader.GetBoolean(8)));
        }

        return items;
    }

    /// <summary>Stores a successfully generated "ficha", replacing a previous one for the same content item.</summary>
    public ContentDetail SaveReady(long contentItemId, string json, int promptVersion, string model) =>
        Save(contentItemId, json, promptVersion, model, ContentSummaryStatus.Ready, error: null);

    /// <summary>Records that generating a "ficha" failed, so it is not retried until the prompt version changes.</summary>
    public ContentDetail SaveError(long contentItemId, int promptVersion, string model, string error, string? json = null) =>
        Save(contentItemId, json, promptVersion, model, ContentSummaryStatus.Error, error);

    private ContentDetail Save(
        long contentItemId, string? json, int promptVersion, string model, ContentSummaryStatus status, string? error)
    {
        var createdAtUtc = DateTime.UtcNow;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ContentDetails (ContentItemId, Json, PromptVersion, Model, Status, Error, CreatedAtUtc)
            VALUES ($contentItemId, $json, $promptVersion, $model, $status, $error, $createdAtUtc)
            ON CONFLICT (ContentItemId) DO UPDATE SET
                Json = excluded.Json, PromptVersion = excluded.PromptVersion, Model = excluded.Model,
                Status = excluded.Status, Error = excluded.Error, CreatedAtUtc = excluded.CreatedAtUtc;
            """;
        command.Parameters.AddWithValue("$contentItemId", contentItemId);
        command.Parameters.AddWithValue("$json", (object?)json ?? DBNull.Value);
        command.Parameters.AddWithValue("$promptVersion", promptVersion);
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$status", status == ContentSummaryStatus.Ready ? "ready" : "error");
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", Format(createdAtUtc));
        command.ExecuteNonQuery();

        return new ContentDetail(contentItemId, json, promptVersion, model, status, error, createdAtUtc);
    }

    private static ContentDetail ReadDetail(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        reader.GetInt32(2),
        reader.GetString(3),
        reader.GetString(4) == "ready" ? ContentSummaryStatus.Ready : ContentSummaryStatus.Error,
        reader.IsDBNull(5) ? null : reader.GetString(5),
        Parse(reader.GetString(6)));

    private static string Format(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime Parse(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
