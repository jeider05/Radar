using Radar.Core.Contents;
using Radar.Core.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class NewContentDetectorTests
{
    private static readonly Source Channel = new(1, SourceType.YouTube, "UCchannel", "Channel", "https://www.youtube.com/channel/UCchannel", true, DateTime.UtcNow);
    private static readonly Source NewChannel = new(2, SourceType.YouTube, "UCnew", "New channel", "https://www.youtube.com/channel/UCnew", true, DateTime.UtcNow);
    private static readonly Source Repository = new(3, SourceType.GitHub, "org/repo", "org/repo", "https://github.com/org/repo", true, DateTime.UtcNow);
    private static readonly Source[] Sources = [Channel, NewChannel, Repository];

    [Fact]
    public void First_load_notifies_nothing()
    {
        var current = new[] { Item(10, Channel) };

        Assert.Empty(NewContentDetector.FindVideosToNotify(previous: null, current, Sources));
    }

    [Fact]
    public void New_video_of_a_channel_that_already_had_videos_is_notified()
    {
        var previous = new[] { Item(10, Channel) };
        var current = new[] { Item(11, Channel), Item(10, Channel) };

        var result = NewContentDetector.FindVideosToNotify(previous, current, Sources);

        var (item, source) = Assert.Single(result);
        Assert.Equal(11, item.Id);
        Assert.Equal(Channel, source);
    }

    [Fact]
    public void First_videos_of_a_newly_added_channel_are_not_notified()
    {
        var previous = new[] { Item(10, Channel) };
        var current = new[] { Item(20, NewChannel), Item(21, NewChannel), Item(10, Channel) };

        Assert.Empty(NewContentDetector.FindVideosToNotify(previous, current, Sources));
    }

    [Fact]
    public void New_github_releases_are_not_notified()
    {
        var previous = new[] { Item(30, Repository) };
        var current = new[] { Item(31, Repository), Item(30, Repository) };

        Assert.Empty(NewContentDetector.FindVideosToNotify(previous, current, Sources));
    }

    [Fact]
    public void Already_known_videos_are_not_notified_again()
    {
        var items = new[] { Item(11, Channel), Item(10, Channel) };

        Assert.Empty(NewContentDetector.FindVideosToNotify(items, items, Sources));
    }

    [Fact]
    public void Video_already_read_is_not_notified()
    {
        var previous = new[] { Item(10, Channel) };
        var current = new[] { Item(11, Channel) with { IsRead = true }, Item(10, Channel) };

        Assert.Empty(NewContentDetector.FindVideosToNotify(previous, current, Sources));
    }

    private static ContentItem Item(long id, Source source) =>
        new(id, source.Id, $"ext-{id}", $"Title {id}", $"https://example.invalid/{id}", null, DateTime.UtcNow, DateTime.UtcNow, IsRead: false);
}
