using Radar.Core.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class SourceTests
{
    [Theory]
    [InlineData("Gentleman-Programming/gentle-ai", "gentle-ai")]
    [InlineData("Gentleman-Programming/engram", "engram")]
    public void GitHub_display_name_is_the_repository_without_its_owner(string name, string expected)
    {
        var source = new Source(1, SourceType.GitHub, name, name, $"https://github.com/{name}", true, DateTime.UtcNow);

        Assert.Equal(expected, source.DisplayName);
    }

    [Fact]
    public void YouTube_display_name_is_the_channel_name()
    {
        var source = new Source(1, SourceType.YouTube, "UCchannel", "Gentleman / Programming", "https://www.youtube.com/channel/UCchannel", true, DateTime.UtcNow);

        Assert.Equal("Gentleman / Programming", source.DisplayName);
    }
}
