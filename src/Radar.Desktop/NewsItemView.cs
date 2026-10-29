using System.Globalization;
using Radar.Core.Contents;
using Radar.Core.Sources;
using Radar.Infrastructure.YouTube;

namespace Radar.Desktop;

/// <summary>A Core <see cref="NewsItem"/> plus the thin, Infrastructure-dependent bits only the UI needs (dates, thumbnail URL).</summary>
public sealed record NewsItemView(NewsItem News)
{
    public ContentItem Item => News.Item;

    public string Title => News.Item.Title;

    public string Url => News.Item.Url;

    public bool IsRead => News.IsRead;

    public string SourceName => News.SourceName;

    public SourceType SourceType => News.SourceType;

    public bool IsVideo => SourceType == SourceType.YouTube;

    public string KindLabel => IsVideo ? "Video" : "Release";

    public string DateText => Item.PublishedAtUtc is { } published
        ? FormatDate(published)
        : $"Detectado {FormatDate(Item.DetectedAtUtc)}";

    public string? ThumbnailUrl => IsVideo ? string.Format(YouTubeClient.ThumbnailUrlFormat, Item.ExternalId) : null;

    public IReadOnlyList<string>? SummaryBullets => News.ReleaseSummaryBullets;

    public string? VideoSummaryText => News.VideoSummary?.DeQueTrata;

    public bool ShowReleaseBullets => News.SummaryStatus == SummaryDisplayStatus.Ready && !IsVideo && SummaryBullets is { Count: > 0 };

    public bool ShowVideoSummary => News.SummaryStatus == SummaryDisplayStatus.Ready && IsVideo && !string.IsNullOrEmpty(VideoSummaryText);

    public bool ShowPending => News.SummaryStatus == SummaryDisplayStatus.Pending;

    public static string FormatDate(DateTime utc) =>
        utc.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
