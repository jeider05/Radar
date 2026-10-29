using Radar.Core.Sources;

namespace Radar.Core.Contents;

/// <summary>How a content item's AI summary should be shown, independent of whether it parsed correctly.</summary>
public enum SummaryDisplayStatus
{
    /// <summary>Nothing to show: no summary row and Gemini is not configured, or the row is an error.</summary>
    None,

    /// <summary>No summary row yet, but Gemini is configured, so one is expected soon.</summary>
    Pending,

    /// <summary>A valid summary is ready to display.</summary>
    Ready,
}

/// <summary>One item of the Novedades list, with its source and AI summary already resolved for display.</summary>
public sealed record NewsItem(
    ContentItem Item,
    string SourceName,
    SourceType SourceType,
    SummaryDisplayStatus SummaryStatus,
    ReleaseSummary? ReleaseSummary,
    VideoSummary? VideoSummary)
{
    public long SourceId => Item.SourceId;

    public bool IsRead => Item.IsRead;

    /// <summary>The release summary's points plus its action (if any) as the last bullet, ready to list.</summary>
    public IReadOnlyList<string>? ReleaseSummaryBullets => ReleaseSummary is { } summary
        ? summary.Accion is { } accion ? [.. summary.Puntos, accion] : summary.Puntos
        : null;
}

/// <summary>Builds display-ready <see cref="NewsItem"/> rows and detects when the Novedades list needs redrawing.</summary>
public static class NewsListing
{
    /// <summary>Joins each content item with its source and (if any) AI summary.</summary>
    public static IReadOnlyList<NewsItem> BuildRows(
        IReadOnlyList<ContentItem> items, IReadOnlyList<Source> sources, IReadOnlyList<ContentSummary> summaries, bool geminiConfigured)
    {
        var sourcesById = sources.ToDictionary(s => s.Id);
        var summariesByItemId = summaries.ToDictionary(s => s.ContentItemId);

        return items.Select(item =>
        {
            var source = sourcesById.GetValueOrDefault(item.SourceId);
            summariesByItemId.TryGetValue(item.Id, out var summary);
            var (status, release, video) = ResolveSummary(summary, geminiConfigured);

            return new NewsItem(
                item, source?.DisplayName ?? "Unknown source", source?.SourceType ?? SourceType.GitHub, status, release, video);
        }).ToList();
    }

    private static (SummaryDisplayStatus Status, ReleaseSummary? Release, VideoSummary? Video) ResolveSummary(
        ContentSummary? summary, bool geminiConfigured)
    {
        if (summary is { Status: ContentSummaryStatus.Ready, Json: { } json })
        {
            if (summary.Kind == ContentSummaryKind.Release && ReleaseSummary.TryParse(json, out var release, out _))
            {
                return (SummaryDisplayStatus.Ready, release, null);
            }

            if (summary.Kind == ContentSummaryKind.Video && VideoSummary.TryParse(json, out var video, out _))
            {
                return (SummaryDisplayStatus.Ready, null, video);
            }

            // Stored as ready but does not parse right now (for example a future prompt format change): show nothing.
            return (SummaryDisplayStatus.None, null, null);
        }

        if (summary is not null)
        {
            // Status is Error.
            return (SummaryDisplayStatus.None, null, null);
        }

        return (geminiConfigured ? SummaryDisplayStatus.Pending : SummaryDisplayStatus.None, null, null);
    }

    /// <summary>
    /// Whether the Novedades list should redraw: the first load, or any difference in sources, items, ready
    /// summaries, or the daily digest (each compared by structural/record equality).
    /// </summary>
    public static bool HasChanges(
        IReadOnlyList<Source>? previousSources, IReadOnlyList<Source> sources,
        IReadOnlyList<ContentItem>? previousItems, IReadOnlyList<ContentItem> items,
        IReadOnlyList<ContentSummary>? previousSummaries, IReadOnlyList<ContentSummary> summaries,
        DailyDigest? previousDigest, DailyDigest? digest) =>
        previousSources is null
        || previousItems is null
        || previousSummaries is null
        || !sources.SequenceEqual(previousSources)
        || !items.SequenceEqual(previousItems)
        || !summaries.SequenceEqual(previousSummaries)
        || !Equals(previousDigest, digest);
}
