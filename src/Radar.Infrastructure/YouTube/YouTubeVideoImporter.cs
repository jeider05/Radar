using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Sources;

namespace Radar.Infrastructure.YouTube;

public sealed record YouTubeImportResult(long SourceId, int VideosFound, int VideosAdded);

/// <summary>Stores the latest videos of a YouTube channel as unread content items, skipping known ones.</summary>
public sealed class YouTubeVideoImporter(YouTubeClient client, SourceStore sources, ContentItemStore contentItems)
{
    public async Task<YouTubeImportResult> ImportAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var feed = await client.GetFeedAsync(channelId, cancellationToken);

        var source = sources.Find(SourceType.YouTube, channelId)
            ?? sources.TryAdd(SourceType.YouTube, channelId, feed.ChannelTitle, YouTubeChannelLink.ChannelUrl(channelId))
            ?? throw new InvalidOperationException($"Could not register YouTube channel '{channelId}'.");

        // The feed is newest first; insert oldest first so the newest video is the most recently detected item.
        var added = 0;
        foreach (var video in feed.Videos.Reverse())
        {
            if (contentItems.TryAdd(source.Id, video.VideoId, video.Title, video.Url, video.Description, video.PublishedAtUtc) is not null)
            {
                added++;
            }
        }

        return new YouTubeImportResult(source.Id, feed.Videos.Count, added);
    }
}
