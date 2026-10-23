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
}

/// <summary>
/// Periodically imports new releases for every active GitHub source, one source at a time.
/// Stops the cycle and waits for the reset when GitHub reports a rate limit.
/// </summary>
public sealed class GitHubReleaseMonitor(
    GitHubReleaseImporter importer,
    SourceStore sources,
    IOptions<GitHubMonitorOptions> options,
    ILogger<GitHubReleaseMonitor> logger) : BackgroundService
{
    // Lower bound for waiting after a rate limit, in case the reported reset time is already past.
    private static readonly TimeSpan MinimumRateLimitWait = TimeSpan.FromMinutes(1);

    private readonly TimeSpan _pollingInterval = options.Value.PollingInterval > TimeSpan.Zero
        ? options.Value.PollingInterval
        : throw new ArgumentOutOfRangeException(nameof(options), "GitHubMonitor:PollingInterval must be positive.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GitHub monitor started. Polling interval: {Interval}.", _pollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                wait = await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // For example, the database is unavailable. Try again on the next normal cycle.
                logger.LogError(ex, "GitHub monitoring cycle failed.");
                wait = _pollingInterval;
            }

            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("GitHub monitor stopped.");
    }

    /// <summary>Processes all active GitHub sources once and returns how long to wait before the next cycle.</summary>
    public async Task<TimeSpan> RunCycleAsync(CancellationToken cancellationToken)
    {
        var activeSources = sources.GetActive(SourceType.GitHub);
        if (activeSources.Count == 0)
        {
            logger.LogDebug("No active GitHub sources.");
            return _pollingInterval;
        }

        foreach (var source in activeSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (source.ExternalId.Split('/') is not [var owner, var repository])
            {
                logger.LogWarning("Skipping GitHub source {SourceId}: '{ExternalId}' is not owner/repository.", source.Id, source.ExternalId);
                continue;
            }

            try
            {
                var result = await importer.ImportAsync(owner, repository, cancellationToken);
                logger.LogInformation(
                    "{Repository}: {Found} releases found, {Added} new.", source.ExternalId, result.ReleasesFound, result.ReleasesAdded);
            }
            catch (GitHubApiException ex) when (ex.IsRateLimited)
            {
                var untilReset = ex.RateLimitResetUtc!.Value - DateTime.UtcNow;
                var wait = untilReset > MinimumRateLimitWait ? untilReset : MinimumRateLimitWait;
                logger.LogWarning("GitHub rate limit reached while checking {Repository}. Pausing for {Wait}.", source.ExternalId, wait);
                return wait;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not check GitHub source {Repository}.", source.ExternalId);
            }
        }

        return _pollingInterval;
    }
}
