using System.Globalization;
using Microsoft.Data.Sqlite;
using Radar.Core.Contents;
using Radar.Core.Sources;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Contents;

/// <summary>A content item that still needs (or needs a newer) AI summary, with the kind to generate for it.</summary>
public sealed record SummaryCandidate(ContentItem Item, string SourceName, ContentSummaryKind Kind);

public sealed class ContentSummaryStore(RadarDatabase database)
{
    /// <summary>
    /// Active GitHub/YouTube content items that have no <see cref="ContentSummary"/> yet, or whose summary used an
    /// older prompt version than the one given for its kind. Most recently published (or detected) first.
    /// Callers apply their own <c>MaxAgeDays</c>/<c>MaxItemsPerCycle</c> limits.
    /// </summary>
    public IReadOnlyList<SummaryCandidate> GetCandidates(int releasePromptVersion, int videoPromptVersion, bool onlyUnread)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ci.Id, ci.SourceId, ci.ExternalId, ci.Title, ci.Url, ci.Description, ci.PublishedAtUtc, ci.DetectedAtUtc, ci.IsRead,
                   s.SourceType, s.Name
            FROM ContentItems ci
            JOIN Sources s ON s.Id = ci.SourceId
            LEFT JOIN ContentSummaries cs ON cs.ContentItemId = ci.Id
            WHERE s.SourceType IN ('GitHub', 'YouTube')
              AND (NOT $onlyUnread OR ci.IsRead = 0)
              AND (
                    cs.ContentItemId IS NULL
                    OR cs.PromptVersion < (CASE s.SourceType WHEN 'GitHub' THEN $releaseVersion ELSE $videoVersion END)
                  )
            ORDER BY COALESCE(ci.PublishedAtUtc, ci.DetectedAtUtc) DESC, ci.Id DESC;
            """;
        command.Parameters.AddWithValue("$onlyUnread", onlyUnread);
        command.Parameters.AddWithValue("$releaseVersion", releasePromptVersion);
        command.Parameters.AddWithValue("$videoVersion", videoPromptVersion);

        var candidates = new List<SummaryCandidate>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var item = new ContentItem(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : Parse(reader.GetString(6)),
                Parse(reader.GetString(7)), reader.GetBoolean(8));
            var kind = reader.GetString(9) == nameof(SourceType.GitHub) ? ContentSummaryKind.Release : ContentSummaryKind.Video;
            candidates.Add(new SummaryCandidate(item, reader.GetString(10), kind));
        }

        return candidates;
    }

    /// <summary>Returns the stored summary of a content item, or null when none was generated yet.</summary>
    public ContentSummary? Get(long contentItemId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ContentItemId, Kind, Json, PromptVersion, Model, Status, Error, CreatedAtUtc
            FROM ContentSummaries
            WHERE ContentItemId = $contentItemId;
            """;
        command.Parameters.AddWithValue("$contentItemId", contentItemId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSummary(reader) : null;
    }

    /// <summary>Every summary generated successfully, newest first. Used by the UI (session 3).</summary>
    public IReadOnlyList<ContentSummary> GetReady()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ContentItemId, Kind, Json, PromptVersion, Model, Status, Error, CreatedAtUtc
            FROM ContentSummaries
            WHERE Status = 'ready'
            ORDER BY CreatedAtUtc DESC;
            """;

        var summaries = new List<ContentSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            summaries.Add(ReadSummary(reader));
        }

        return summaries;
    }

    /// <summary>Stores a successfully generated summary, replacing a previous one for the same content item.</summary>
    public ContentSummary SaveReady(long contentItemId, ContentSummaryKind kind, string json, int promptVersion, string model) =>
        Save(contentItemId, kind, json, promptVersion, model, ContentSummaryStatus.Ready, error: null);

    /// <summary>
    /// Records that generating a summary failed, so it is not retried until the prompt version changes.
    /// <paramref name="json"/> may hold the raw (invalid) response for diagnostics, when there was one.
    /// </summary>
    public ContentSummary SaveError(long contentItemId, ContentSummaryKind kind, int promptVersion, string model, string error, string? json = null) =>
        Save(contentItemId, kind, json, promptVersion, model, ContentSummaryStatus.Error, error);

    private ContentSummary Save(
        long contentItemId, ContentSummaryKind kind, string? json, int promptVersion, string model, ContentSummaryStatus status, string? error)
    {
        var createdAtUtc = DateTime.UtcNow;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ContentSummaries (ContentItemId, Kind, Json, PromptVersion, Model, Status, Error, CreatedAtUtc)
            VALUES ($contentItemId, $kind, $json, $promptVersion, $model, $status, $error, $createdAtUtc)
            ON CONFLICT (ContentItemId) DO UPDATE SET
                Kind = excluded.Kind, Json = excluded.Json, PromptVersion = excluded.PromptVersion,
                Model = excluded.Model, Status = excluded.Status, Error = excluded.Error, CreatedAtUtc = excluded.CreatedAtUtc;
            """;
        command.Parameters.AddWithValue("$contentItemId", contentItemId);
        command.Parameters.AddWithValue("$kind", ToDbString(kind));
        command.Parameters.AddWithValue("$json", (object?)json ?? DBNull.Value);
        command.Parameters.AddWithValue("$promptVersion", promptVersion);
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$status", ToDbString(status));
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", Format(createdAtUtc));
        command.ExecuteNonQuery();

        return new ContentSummary(contentItemId, kind, json, promptVersion, model, status, error, createdAtUtc);
    }

    /// <summary>Returns the daily digest of a local date, or null when none was generated yet.</summary>
    public DailyDigest? GetDailyDigest(DateOnly date)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Date, Json, ItemsHash, Model, CreatedAtUtc FROM DailyDigests WHERE Date = $date;";
        command.Parameters.AddWithValue("$date", Format(date));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDailyDigest(reader) : null;
    }

    /// <summary>Stores the daily digest of a local date, replacing a previous one for the same date.</summary>
    public DailyDigest SaveDailyDigest(DateOnly date, string json, string itemsHash, string model)
    {
        var createdAtUtc = DateTime.UtcNow;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DailyDigests (Date, Json, ItemsHash, Model, CreatedAtUtc)
            VALUES ($date, $json, $itemsHash, $model, $createdAtUtc)
            ON CONFLICT (Date) DO UPDATE SET
                Json = excluded.Json, ItemsHash = excluded.ItemsHash, Model = excluded.Model, CreatedAtUtc = excluded.CreatedAtUtc;
            """;
        command.Parameters.AddWithValue("$date", Format(date));
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$itemsHash", itemsHash);
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$createdAtUtc", Format(createdAtUtc));
        command.ExecuteNonQuery();

        return new DailyDigest(date, json, itemsHash, model, createdAtUtc);
    }

    private static string ToDbString(ContentSummaryKind kind) => kind switch
    {
        ContentSummaryKind.Release => "release",
        ContentSummaryKind.Video => "video",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static ContentSummaryKind FromDbKind(string value) => value switch
    {
        "release" => ContentSummaryKind.Release,
        "video" => ContentSummaryKind.Video,
        _ => throw new InvalidOperationException($"Unknown ContentSummaries.Kind '{value}'."),
    };

    private static string ToDbString(ContentSummaryStatus status) => status switch
    {
        ContentSummaryStatus.Ready => "ready",
        ContentSummaryStatus.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static ContentSummaryStatus FromDbStatus(string value) => value switch
    {
        "ready" => ContentSummaryStatus.Ready,
        "error" => ContentSummaryStatus.Error,
        _ => throw new InvalidOperationException($"Unknown ContentSummaries.Status '{value}'."),
    };

    private static ContentSummary ReadSummary(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        FromDbKind(reader.GetString(1)),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetInt32(3),
        reader.GetString(4),
        FromDbStatus(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        Parse(reader.GetString(7)));

    private static DailyDigest ReadDailyDigest(SqliteDataReader reader) => new(
        DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        Parse(reader.GetString(4)));

    private static string Format(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime Parse(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
