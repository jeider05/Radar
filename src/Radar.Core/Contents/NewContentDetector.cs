using Radar.Core.Sources;

namespace Radar.Core.Contents;

/// <summary>Decides which newly detected content deserves a notification.</summary>
public static class NewContentDetector
{
    /// <summary>
    /// Unread YouTube videos present in <paramref name="current"/> but not in <paramref name="previous"/>, from channels
    /// that already had videos before. The first load (null previous) and the first import of a newly added channel
    /// notify nothing, so following a channel does not produce one notification per existing video.
    /// </summary>
    public static IReadOnlyList<(ContentItem Item, Source Source)> FindVideosToNotify(
        IReadOnlyList<ContentItem>? previous, IReadOnlyList<ContentItem> current, IReadOnlyList<Source> sources)
    {
        if (previous is null)
        {
            return [];
        }

        var knownIds = previous.Select(i => i.Id).ToHashSet();
        var sourcesWithContent = previous.Select(i => i.SourceId).ToHashSet();
        var youTubeChannels = sources.Where(s => s.SourceType == SourceType.YouTube).ToDictionary(s => s.Id);

        return current
            .Where(i => !i.IsRead && !knownIds.Contains(i.Id) && sourcesWithContent.Contains(i.SourceId))
            .Where(i => youTubeChannels.ContainsKey(i.SourceId))
            .Select(i => (i, youTubeChannels[i.SourceId]))
            .ToList();
    }
}
