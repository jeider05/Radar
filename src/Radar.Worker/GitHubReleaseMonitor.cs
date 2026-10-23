using Microsoft.Extensions.Options;
using Radar.Core.Sources;
using Radar.Infrastructure.GitHub;
using Radar.Infrastructure.Sources;

namespace Radar.Worker;

public sealed class GitHubMonitorOptions
{
    public const string SectionName = "GitHubMonitor";

    /// <summary>Time between normal monitoring cycles.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// How often the local database is checked for sources added or resumed since they were last checked.
    /// Only those sources are requested from GitHub; this check itself makes no network requests.
    /// </summary>
    public TimeSpan NewSourceCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Imports new releases of active GitHub sources. Pauses until the reset when GitHub reports a rate limit.</summary>
public sealed class GitHubReleaseMonitor(
    GitHubReleaseImporter importer,
    SourceStore sources,
    IOptions<GitHubMonitorOptions> options,
    ILogger<GitHubReleaseMonitor> logger)
    : SourceMonitor(sources, logger, GitHubMonitorOptions.SectionName, options.Value.PollingInterval, options.Value.NewSourceCheckInterval)
{
    protected override SourceType SourceType => SourceType.GitHub;

    protected override async Task CheckSourceAsync(Source source, CancellationToken cancellationToken)
    {
        if (!GitHubRepository.TryParse(source.ExternalId, out var repository, out _))
        {
            logger.LogWarning("Skipping GitHub source {SourceId}: '{ExternalId}' is not owner/repository.", source.Id, source.ExternalId);
            return;
        }

        try
        {
            var result = await importer.ImportAsync(repository.Owner, repository.Name, cancellationToken);
            logger.LogInformation(
                "{Repository}: {Found} releases found, {Added} new.", source.ExternalId, result.ReleasesFound, result.ReleasesAdded);
        }
        catch (GitHubApiException ex) when (ex.IsRateLimited)
        {
            throw new SourceRateLimitedException(ex.RateLimitResetUtc!.Value);
        }
    }
}
