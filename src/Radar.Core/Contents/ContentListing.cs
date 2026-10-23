namespace Radar.Core.Contents;

/// <summary>Period of the calendar, relative to now, used to filter content.</summary>
public enum ContentPeriod
{
    All,
    Today,
    ThisMonth,
    ThisYear,
}

/// <summary>One page of a list. PageNumber is 1-based.</summary>
public sealed record ContentPage<T>(IReadOnlyList<T> Items, int PageNumber, int PageCount);

/// <summary>Filtering by period and pagination for content lists.</summary>
public static class ContentListing
{
    /// <summary>
    /// Whether the item was published (or detected, when the publication date is unknown) in the given period,
    /// using the calendar of <paramref name="timeZone"/>.
    /// </summary>
    public static bool IsInPeriod(ContentItem item, ContentPeriod period, DateTime nowUtc, TimeZoneInfo timeZone)
    {
        if (period == ContentPeriod.All)
        {
            return true;
        }

        var date = TimeZoneInfo.ConvertTimeFromUtc(item.PublishedAtUtc ?? item.DetectedAtUtc, timeZone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone);

        return period switch
        {
            ContentPeriod.Today => date.Date == now.Date,
            ContentPeriod.ThisMonth => date.Year == now.Year && date.Month == now.Month,
            ContentPeriod.ThisYear => date.Year == now.Year,
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
        };
    }

    /// <summary>Returns the requested page, clamping the page number to the available pages. An empty list has one empty page.</summary>
    public static ContentPage<T> GetPage<T>(IReadOnlyList<T> items, int pageNumber, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        var pageCount = Math.Max(1, (items.Count + pageSize - 1) / pageSize);
        var page = Math.Clamp(pageNumber, 1, pageCount);
        var pageItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new ContentPage<T>(pageItems, page, pageCount);
    }
}
