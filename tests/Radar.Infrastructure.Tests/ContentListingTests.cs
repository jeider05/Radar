using Radar.Core.Contents;
using Radar.Core.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class ContentListingTests
{
    // Fixed UTC-5 zone (like Colombia) so the tests do not depend on the machine's time zone.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test-0500", TimeSpan.FromHours(-5), "Test", "Test");

    // 2026-10-23 10:00 local = 15:00 UTC.
    private static readonly DateTime NowUtc = new(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-10-23T14:00:00Z", true, true, true)]   // today, 09:00 local
    [InlineData("2026-10-23T05:00:00Z", true, true, true)]   // today, 00:00 local (start of the local day)
    [InlineData("2026-10-23T04:59:59Z", false, true, true)]  // yesterday 23:59:59 local, although it is the 23rd in UTC
    [InlineData("2026-10-01T05:00:00Z", false, true, true)]  // first moment of this month, local
    [InlineData("2026-10-01T04:59:59Z", false, false, true)] // last moment of September, local
    [InlineData("2026-01-01T05:00:00Z", false, false, true)] // first moment of this year, local
    [InlineData("2026-01-01T04:59:59Z", false, false, false)] // last moment of 2025, local
    [InlineData("2025-10-23T15:00:00Z", false, false, false)] // same day and month, previous year
    public void IsInPeriod_uses_local_calendar_day_month_and_year(string publishedUtc, bool today, bool thisMonth, bool thisYear)
    {
        var item = Item(DateTime.Parse(publishedUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal));

        Assert.Equal(today, ContentListing.IsInPeriod(item, ContentPeriod.Today, NowUtc, Zone));
        Assert.Equal(thisMonth, ContentListing.IsInPeriod(item, ContentPeriod.ThisMonth, NowUtc, Zone));
        Assert.Equal(thisYear, ContentListing.IsInPeriod(item, ContentPeriod.ThisYear, NowUtc, Zone));
        Assert.True(ContentListing.IsInPeriod(item, ContentPeriod.All, NowUtc, Zone));
    }

    [Fact]
    public void IsInPeriod_uses_detection_date_when_publication_date_is_unknown()
    {
        var detectedToday = Item(publishedAtUtc: null, detectedAtUtc: NowUtc.AddHours(-1));
        var detectedLastYear = Item(publishedAtUtc: null, detectedAtUtc: NowUtc.AddYears(-1));

        Assert.True(ContentListing.IsInPeriod(detectedToday, ContentPeriod.Today, NowUtc, Zone));
        Assert.False(ContentListing.IsInPeriod(detectedLastYear, ContentPeriod.ThisYear, NowUtc, Zone));
    }

    [Fact]
    public void GetPage_returns_the_requested_page_and_page_count()
    {
        var items = Enumerable.Range(1, 25).ToList();

        var page = ContentListing.GetPage(items, pageNumber: 2, pageSize: 10);

        Assert.Equal(Enumerable.Range(11, 10), page.Items);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(3, page.PageCount);
    }

    [Fact]
    public void GetPage_last_page_can_be_partial()
    {
        var page = ContentListing.GetPage(Enumerable.Range(1, 25).ToList(), pageNumber: 3, pageSize: 10);

        Assert.Equal(Enumerable.Range(21, 5), page.Items);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(4, 3)]
    [InlineData(99, 3)]
    public void GetPage_clamps_out_of_range_page_numbers(int requested, int expected)
    {
        var page = ContentListing.GetPage(Enumerable.Range(1, 25).ToList(), requested, pageSize: 10);

        Assert.Equal(expected, page.PageNumber);
    }

    [Fact]
    public void GetPage_of_an_empty_list_is_a_single_empty_page()
    {
        var page = ContentListing.GetPage(new List<int>(), pageNumber: 1, pageSize: 10);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(1, page.PageCount);
    }

    [Fact]
    public void GetPage_rejects_a_non_positive_page_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContentListing.GetPage(new List<int> { 1 }, 1, pageSize: 0));
    }

    [Fact]
    public void FilterByType_All_returns_every_item()
    {
        var items = new List<NewsItem> { NewsRow(SourceType.GitHub), NewsRow(SourceType.YouTube) };

        Assert.Equal(2, ContentListing.FilterByType(items, ContentTypeFilter.All).Count);
    }

    [Fact]
    public void FilterByType_Release_keeps_only_GitHub_items()
    {
        var release = NewsRow(SourceType.GitHub);
        var video = NewsRow(SourceType.YouTube);

        var result = ContentListing.FilterByType([release, video], ContentTypeFilter.Release);

        Assert.Equal([release], result);
    }

    [Fact]
    public void FilterByType_Video_keeps_only_YouTube_items()
    {
        var release = NewsRow(SourceType.GitHub);
        var video = NewsRow(SourceType.YouTube);

        var result = ContentListing.FilterByType([release, video], ContentTypeFilter.Video);

        Assert.Equal([video], result);
    }

    private static NewsItem NewsRow(SourceType sourceType) =>
        new(Item(publishedAtUtc: NowUtc), "Source", sourceType, SummaryDisplayStatus.None, null, null);

    private static ContentItem Item(DateTime? publishedAtUtc, DateTime? detectedAtUtc = null) =>
        new(1, 1, "id", "Title", "https://example.invalid", null, publishedAtUtc, detectedAtUtc ?? NowUtc, IsRead: false);
}
