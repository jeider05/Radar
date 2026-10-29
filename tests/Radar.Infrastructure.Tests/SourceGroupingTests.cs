using Radar.Core.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class SourceGroupingTests
{
    [Fact]
    public void Groups_sources_by_type_preserving_relative_order_within_each_group()
    {
        var sources = new List<Source>
        {
            new(1, SourceType.YouTube, "channel-a", "Channel A", "https://youtube.com/a", true, DateTime.UtcNow),
            new(2, SourceType.GitHub, "org/repo-a", "org/repo-a", "https://github.com/org/repo-a", true, DateTime.UtcNow),
            new(3, SourceType.YouTube, "channel-b", "Channel B", "https://youtube.com/b", true, DateTime.UtcNow),
            new(4, SourceType.GitHub, "org/repo-b", "org/repo-b", "https://github.com/org/repo-b", true, DateTime.UtcNow),
        };

        var groups = SourceGrouping.GroupByType(sources);

        Assert.Equal(2, groups.Count);
        Assert.Equal(SourceType.GitHub, groups[0].SourceType);
        Assert.Equal([2L, 4L], groups[0].Sources.Select(s => s.Id));
        Assert.Equal(SourceType.YouTube, groups[1].SourceType);
        Assert.Equal([1L, 3L], groups[1].Sources.Select(s => s.Id));
    }

    [Fact]
    public void Omits_a_type_with_no_sources()
    {
        var sources = new List<Source>
        {
            new(1, SourceType.GitHub, "org/repo", "org/repo", "https://github.com/org/repo", true, DateTime.UtcNow),
        };

        var groups = SourceGrouping.GroupByType(sources);

        var group = Assert.Single(groups);
        Assert.Equal(SourceType.GitHub, group.SourceType);
    }

    [Fact]
    public void Empty_input_produces_no_groups()
    {
        Assert.Empty(SourceGrouping.GroupByType([]));
    }
}
