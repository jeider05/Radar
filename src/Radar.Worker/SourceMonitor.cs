using Radar.Core.Sources;
using Radar.Infrastructure.Sources;

namespace Radar.Worker;

/// <summary>Thrown by a monitor when the external service asks to stop requesting until a given time.</summary>
public sealed class SourceRateLimitedException(DateTime resumeAtUtc) : Exception("Rate limited.")
{
    public DateTime ResumeAtUtc { get; } = resumeAtUtc;
}

/// <summary>
/// Shared monitoring loop for one type of source: processes every active source of that type one at a time,
/// checks sources added or resumed in Desktop between normal cycles, and pauses while rate limited.
/// </summary>
public abstract class SourceMonitor : BackgroundService
{
    // Lower bound for waiting after a rate limit, in case the reported reset time is already past.
    private static readonly TimeSpan MinimumRateLimitWait = TimeSpan.FromMinutes(1);

    private readonly SourceStore _sources;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollingInterval;
    private readonly TimeSpan _newSourceCheckInterval;

    // Sources checked (successfully or not) since the Worker started. Others are checked by CheckNewSourcesAsync.
    private readonly HashSet<long> _checkedSourceIds = [];
    private DateTime _rateLimitedUntilUtc = DateTime.MinValue;

    protected SourceMonitor(
        SourceStore sources, ILogger logger, string optionsSection, TimeSpan pollingInterval, TimeSpan newSourceCheckInterval)
    {
        _sources = sources;
        _logger = logger;
        _pollingInterval = pollingInterval > TimeSpan.Zero
            ? pollingInterval
            : throw new ArgumentOutOfRangeException(nameof(pollingInterval), $"{optionsSection}:PollingInterval must be positive.");
        _newSourceCheckInterval = newSourceCheckInterval > TimeSpan.Zero
            ? newSourceCheckInterval
            : throw new ArgumentOutOfRangeException(nameof(newSourceCheckInterval), $"{optionsSection}:NewSourceCheckInterval must be positive.");
    }

    protected abstract SourceType SourceType { get; }

    /// <summary>
    /// Fetches and stores new content for one source. Throws <see cref="SourceRateLimitedException"/> to pause monitoring;
    /// any other exception is logged and the next source is processed.
    /// </summary>
    protected abstract Task CheckSourceAsync(Source source, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{SourceType} monitor started. Polling interval: {Interval}.", SourceType, _pollingInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan wait;
                try
                {
                    wait = await RunCycleAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // For example, the database is unavailable. Try again on the next normal cycle.
                    _logger.LogError(ex, "{SourceType} monitoring cycle failed.", SourceType);
                    wait = _pollingInterval;
                }

                await WaitForNextCycleAsync(DateTime.UtcNow + wait, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Orderly shutdown.
        }

        _logger.LogInformation("{SourceType} monitor stopped.", SourceType);
    }

    /// <summary>Waits until the next normal cycle, checking for new sources in between (except while rate limited).</summary>
    private async Task WaitForNextCycleAsync(DateTime nextCycleUtc, CancellationToken stoppingToken)
    {
        while (true)
        {
            var remaining = nextCycleUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(remaining < _newSourceCheckInterval ? remaining : _newSourceCheckInterval, stoppingToken);

            if (DateTime.UtcNow >= nextCycleUtc || DateTime.UtcNow < _rateLimitedUntilUtc)
            {
                continue;
            }

            try
            {
                if (await CheckNewSourcesAsync(stoppingToken) is { } rateLimitWait)
                {
                    var resumeUtc = DateTime.UtcNow + rateLimitWait;
                    nextCycleUtc = resumeUtc > nextCycleUtc ? resumeUtc : nextCycleUtc;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Checking new {SourceType} sources failed.", SourceType);
            }
        }
    }

    /// <summary>Processes all active sources once and returns how long to wait before the next cycle.</summary>
    public async Task<TimeSpan> RunCycleAsync(CancellationToken cancellationToken)
    {
        var activeSources = _sources.GetActive(SourceType);
        if (activeSources.Count == 0)
        {
            _logger.LogDebug("No active {SourceType} sources.", SourceType);
            return _pollingInterval;
        }

        return await CheckSourcesAsync(activeSources, cancellationToken) ?? _pollingInterval;
    }

    /// <summary>
    /// Processes active sources that have not been checked since the Worker started, such as sources added or resumed
    /// in Desktop. Returns the wait imposed by a rate limit, or null.
    /// </summary>
    public async Task<TimeSpan?> CheckNewSourcesAsync(CancellationToken cancellationToken)
    {
        var activeSources = _sources.GetActive(SourceType);

        // Forget paused or removed sources, so a source resumed in Desktop counts as new again.
        _checkedSourceIds.IntersectWith(activeSources.Select(s => s.Id));

        var newSources = activeSources.Where(s => !_checkedSourceIds.Contains(s.Id)).ToList();
        if (newSources.Count == 0)
        {
            return null;
        }

        _logger.LogInformation("Checking {Count} new {SourceType} source(s).", newSources.Count, SourceType);
        return await CheckSourcesAsync(newSources, cancellationToken);
    }

    private async Task<TimeSpan?> CheckSourcesAsync(IReadOnlyList<Source> sourcesToCheck, CancellationToken cancellationToken)
    {
        foreach (var source in sourcesToCheck)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Failed sources count as checked too: they are retried on the next normal cycle, not on every new-source check.
            _checkedSourceIds.Add(source.Id);

            try
            {
                await CheckSourceAsync(source, cancellationToken);
            }
            catch (SourceRateLimitedException ex)
            {
                // Not actually checked: let it be picked up again once the limit resets.
                _checkedSourceIds.Remove(source.Id);
                var untilReset = ex.ResumeAtUtc - DateTime.UtcNow;
                var wait = untilReset > MinimumRateLimitWait ? untilReset : MinimumRateLimitWait;
                _rateLimitedUntilUtc = DateTime.UtcNow + wait;
                _logger.LogWarning("{SourceType} rate limit reached while checking {Source}. Pausing for {Wait}.", SourceType, source.ExternalId, wait);
                return wait;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not check {SourceType} source {Source}.", SourceType, source.ExternalId);
            }
        }

        return null;
    }
}
