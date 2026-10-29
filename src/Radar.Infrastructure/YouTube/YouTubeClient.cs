using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Radar.Infrastructure.YouTube;

public sealed record YouTubeVideo(string VideoId, string Title, string Url, string? Description, DateTime? PublishedAtUtc);

public sealed record YouTubeFeed(string ChannelId, string ChannelTitle, IReadOnlyList<YouTubeVideo> Videos);

public sealed class YouTubeApiException(string message, HttpStatusCode? statusCode = null, DateTime? retryAfterUtc = null)
    : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>Set when YouTube asked to slow down (429).</summary>
    public DateTime? RetryAfterUtc { get; } = retryAfterUtc;
}

/// <summary>
/// Reads public YouTube data without an API key: the channel's RSS feed (latest videos) and,
/// to resolve handles such as @channel, the channel page's canonical link.
/// </summary>
public sealed partial class YouTubeClient(HttpClient httpClient)
{
    private const string FeedUrl = "https://www.youtube.com/feeds/videos.xml?channel_id=";

    /// <summary>Format string for a video's thumbnail image; {0} is the video id.</summary>
    public const string ThumbnailUrlFormat = "https://i.ytimg.com/vi/{0}/hqdefault.jpg";

    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(15);

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Yt = "http://www.youtube.com/xml/schemas/2015";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";

    /// <summary>HttpClient settings used by Radar for YouTube requests.</summary>
    public static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Finds the channel id (UC…) of a channel page such as https://www.youtube.com/@channel.</summary>
    public async Task<string> ResolveChannelIdAsync(Uri channelPage, CancellationToken cancellationToken = default)
    {
        var html = await GetStringAsync(channelPage, "Channel page", cancellationToken);

        var match = CanonicalChannelPattern().Match(html);
        if (!match.Success)
        {
            match = ExternalIdPattern().Match(html);
        }

        return match.Success
            ? match.Groups["id"].Value
            : throw new YouTubeApiException($"Could not find a YouTube channel at {channelPage}.");
    }

    /// <summary>Returns the channel title and its latest videos (YouTube lists up to 15), newest first.</summary>
    public async Task<YouTubeFeed> GetFeedAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var xml = await GetStringAsync(new Uri(FeedUrl + Uri.EscapeDataString(channelId)), "Channel", cancellationToken);

        try
        {
            var feed = XDocument.Parse(xml).Root ?? throw new XmlException("Empty feed.");
            // The feed-level yt:channelId omits the "UC" prefix, so the id is taken from the request instead.
            var title = feed.Element(Atom + "title")?.Value ?? channelId;
            var videos = feed.Elements(Atom + "entry").Select(ReadVideo).ToList();
            return new YouTubeFeed(channelId, title, videos);
        }
        catch (Exception ex) when (ex is XmlException or FormatException or InvalidOperationException)
        {
            throw new YouTubeApiException("YouTube returned an unexpected feed.");
        }
    }

    private static YouTubeVideo ReadVideo(XElement entry)
    {
        var videoId = entry.Element(Yt + "videoId")?.Value ?? throw new XmlException("Entry without videoId.");
        var url = entry.Elements(Atom + "link").FirstOrDefault(l => (string?)l.Attribute("rel") == "alternate")?.Attribute("href")?.Value
            ?? $"https://www.youtube.com/watch?v={videoId}";
        var description = entry.Element(Media + "group")?.Element(Media + "description")?.Value;
        var published = entry.Element(Atom + "published")?.Value;

        return new YouTubeVideo(
            videoId,
            entry.Element(Atom + "title")?.Value ?? videoId,
            url,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            published is null ? null : DateTimeOffset.Parse(published, CultureInfo.InvariantCulture).UtcDateTime);
    }

    private async Task<string> GetStringAsync(Uri url, string what, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Radar");
        request.Headers.AcceptLanguage.ParseAdd("en-US");
        // Skips the cookie consent interstitial that some regions get instead of the page.
        request.Headers.Add("Cookie", "CONSENT=YES+cb");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new YouTubeApiException($"Could not reach YouTube: {ex.Message}");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? DefaultRetryAfter;
                throw new YouTubeApiException("YouTube asked to slow down (429).", response.StatusCode, DateTime.UtcNow + retryAfter);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new YouTubeApiException(
                    response.StatusCode == HttpStatusCode.NotFound
                        ? $"{what} not found on YouTube (404)."
                        : $"YouTube returned {(int)response.StatusCode}.",
                    response.StatusCode);
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }

    [GeneratedRegex(@"<link rel=""canonical"" href=""https://www\.youtube\.com/channel/(?<id>UC[A-Za-z0-9_-]{22})""")]
    private static partial Regex CanonicalChannelPattern();

    [GeneratedRegex(@"""externalId"":""(?<id>UC[A-Za-z0-9_-]{22})""")]
    private static partial Regex ExternalIdPattern();
}
