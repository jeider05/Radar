using System.Text.RegularExpressions;

namespace Radar.Infrastructure.YouTube;

/// <summary>
/// Parses what a user types to identify a YouTube channel. Channel ids are known directly from
/// "/channel/UC…" links or raw ids; handles and legacy names need their page resolved by <see cref="YouTubeClient"/>.
/// </summary>
public static partial class YouTubeChannelLink
{
    public static string ChannelUrl(string channelId) => $"https://www.youtube.com/channel/{channelId}";

    /// <summary>
    /// On success, exactly one of <paramref name="channelId"/> (known id) or <paramref name="pageUrl"/> (page to resolve) is set.
    /// </summary>
    public static bool TryParse(string? input, out string? channelId, out Uri? pageUrl, out string error)
    {
        channelId = null;
        pageUrl = null;
        var text = input?.Trim() ?? "";
        if (text.Length == 0)
        {
            error = "Enter a YouTube channel link, for example https://www.youtube.com/@channel.";
            return false;
        }

        if (ChannelIdPattern().IsMatch(text))
        {
            channelId = text;
            error = "";
            return true;
        }

        if (HandlePattern().IsMatch(text))
        {
            pageUrl = new Uri($"https://www.youtube.com/{text}");
            error = "";
            return true;
        }

        var match = ChannelUrlPattern().Match(text);
        if (!match.Success)
        {
            error = text.Contains("watch?v=", StringComparison.OrdinalIgnoreCase) || text.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase)
                ? "That is a video link. Use the channel link instead, for example https://www.youtube.com/@channel."
                : "Use a YouTube channel link: https://www.youtube.com/@channel or https://www.youtube.com/channel/UC…";
            return false;
        }

        if (match.Groups["id"].Success)
        {
            channelId = match.Groups["id"].Value;
        }
        else
        {
            pageUrl = new Uri($"https://www.youtube.com/{match.Groups["path"].Value}");
        }

        error = "";
        return true;
    }

    [GeneratedRegex("^UC[A-Za-z0-9_-]{22}$")]
    private static partial Regex ChannelIdPattern();

    [GeneratedRegex("^@[A-Za-z0-9._-]{3,30}$")]
    private static partial Regex HandlePattern();

    // youtube.com/channel/UC…, /@handle, /c/name or /user/name, optionally followed by a tab such as /videos.
    [GeneratedRegex(
        @"^(https?://)?((www|m)\.)?youtube\.com/(channel/(?<id>UC[A-Za-z0-9_-]{22})|(?<path>@[A-Za-z0-9._-]{3,30}|(c|user)/[A-Za-z0-9._-]+))(/[A-Za-z]*)?/?(\?.*)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ChannelUrlPattern();
}
