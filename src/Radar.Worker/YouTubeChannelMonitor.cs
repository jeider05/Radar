using Microsoft.Extensions.Options;
using Radar.Core.Sources;
using Radar.Infrastructure.Sources;
using Radar.Infrastructure.YouTube;

namespace Radar.Worker;

public sealed class YouTubeMonitorOptions
{
    public const string SectionName = "YouTubeMonitor";

    /// <summary>Time between normal monitoring cycles. The RSS feed needs no API key or quota.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How often the local database is checked for channels added or resumed in Desktop.</summary>
    public TimeSpan NewSourceCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Imports new videos of active YouTube channels from their public RSS feed.</summary>
public sealed class YouTubeChannelMonitor(
    YouTubeVideoImporter importer,
    SourceStore sources,
    IOptions<YouTubeMonitorOptions> options,
    ILogger<YouTubeChannelMonitor> logger)
    : SourceMonitor(sources, logger, YouTubeMonitorOptions.SectionName, options.Value.PollingInterval, options.Value.NewSourceCheckInterval)
{
    protected override SourceType SourceType => SourceType.YouTube;

    protected override async Task CheckSourceAsync(Source source, CancellationToken cancellationToken)
    {
        try
        {
            var result = await importer.ImportAsync(source.ExternalId, cancellationToken);
            logger.LogInformation("{Channel}: {Found} videos found, {Added} new.", source.Name, result.VideosFound, result.VideosAdded);
        }
        catch (YouTubeApiException ex) when (ex.RetryAfterUtc is { } retryAfterUtc)
        {
            throw new SourceRateLimitedException(retryAfterUtc);
        }
    }
}
