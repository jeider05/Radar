using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Radar.Core.Contents;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Gemini;
using Radar.Infrastructure.Prompts;

namespace Radar.Worker;

public sealed class SummaryOptions
{
    public const string SectionName = "Summaries";

    public bool Enabled { get; set; } = true;

    /// <summary>Time between summary cycles.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Maximum items summarized per cycle, to stay within the Gemini quota.</summary>
    public int MaxItemsPerCycle { get; set; } = 5;

    /// <summary>Items older than this (by publication, or detection when unknown) are never summarized.</summary>
    public int MaxAgeDays { get; set; } = 30;

    /// <summary>When true, only unread items are summarized.</summary>
    public bool OnlyUnread { get; set; } = true;

    /// <summary>Minimum time between two daily digests of the same date, even when its items changed.</summary>
    public TimeSpan DailyDigestMinInterval { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Generates short Spanish AI summaries of new releases and videos, and refreshes the "Tu radar hoy" daily digest.
/// Does nothing beyond a one-time warning when Gemini is not configured; the rest of the Worker is unaffected.
/// </summary>
public sealed class ContentSummaryMonitor(
    GeminiClient? gemini,
    ContentSummaryStore summaries,
    ContentItemStore contentItems,
    IPromptProvider prompts,
    IOptions<SummaryOptions> options,
    ILogger<ContentSummaryMonitor> logger)
    : BackgroundService
{
    private const string ReleasePromptName = "Novedades/release-summary";
    private const string VideoPromptName = "Novedades/video-summary";
    private const string DailyDigestPromptName = "Novedades/daily-digest";

    private readonly SummaryOptions _options = Validate(options.Value);
    private bool _loggedMissingApiKey;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Content summary monitor disabled via configuration.");
            return;
        }

        logger.LogInformation("Content summary monitor started. Polling interval: {Interval}.", _options.PollingInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCycleAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Content summary monitoring cycle failed.");
                }

                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Orderly shutdown.
        }

        logger.LogInformation("Content summary monitor stopped.");
    }

    /// <summary>Summarizes pending items (bounded by MaxItemsPerCycle) and then refreshes the daily digest.</summary>
    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        if (gemini is null)
        {
            if (!_loggedMissingApiKey)
            {
                logger.LogWarning("{Variable} is not configured; content summaries will not be generated.", GeminiClient.ApiKeyVariable);
                _loggedMissingApiKey = true;
            }

            return;
        }

        var quotaExhausted = await SummarizePendingItemsAsync(gemini, cancellationToken);
        if (!quotaExhausted)
        {
            await RefreshDailyDigestAsync(gemini, cancellationToken);
        }
    }

    /// <summary>Returns true when Gemini's quota was reached and the rest of this cycle should be skipped.</summary>
    private async Task<bool> SummarizePendingItemsAsync(GeminiClient client, CancellationToken cancellationToken)
    {
        var releasePrompt = prompts.Load(ReleasePromptName);
        var videoPrompt = prompts.Load(VideoPromptName);

        var candidates = summaries.GetCandidates(releasePrompt.Version, videoPrompt.Version, _options.OnlyUnread)
            .Where(candidate => IsWithinMaxAge(candidate.Item, _options.MaxAgeDays))
            .Take(_options.MaxItemsPerCycle)
            .ToList();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prompt = candidate.Kind == ContentSummaryKind.Release ? releasePrompt : videoPrompt;
            if (!await SummarizeAsync(client, candidate, prompt, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns false when Gemini's quota was reached, so the caller stops the cycle.</summary>
    private async Task<bool> SummarizeAsync(
        GeminiClient client, SummaryCandidate candidate, PromptDefinition prompt, CancellationToken cancellationToken)
    {
        var schemaName = candidate.Kind == ContentSummaryKind.Release ? ReleasePromptName : VideoPromptName;
        var schema = prompts.LoadSchema(schemaName);
        var userPrompt = prompts.Render(prompt.UserTemplate, BuildVariables(candidate));

        string json;
        try
        {
            json = await client.GenerateJsonAsync(prompt.System, userPrompt, schema, "a content summary", cancellationToken);
        }
        catch (GeminiApiException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning("Gemini quota reached while summarizing content; resuming on the next cycle.");
            return false;
        }
        catch (GeminiApiException ex)
        {
            summaries.SaveError(candidate.Item.Id, candidate.Kind, prompt.Version, client.Model, ex.Message);
            logger.LogError(ex, "Could not generate a summary for content item {ContentItemId}.", candidate.Item.Id);
            return true;
        }

        if (!TryValidate(candidate.Kind, json, out var error))
        {
            summaries.SaveError(candidate.Item.Id, candidate.Kind, prompt.Version, client.Model, error ?? "Respuesta inválida.", json);
            logger.LogWarning("Gemini returned an invalid summary for content item {ContentItemId}: {Error}", candidate.Item.Id, error);
            return true;
        }

        summaries.SaveReady(candidate.Item.Id, candidate.Kind, json, prompt.Version, client.Model);
        return true;
    }

    private async Task RefreshDailyDigestAsync(GeminiClient client, CancellationToken cancellationToken)
    {
        var localZone = TimeZoneInfo.Local;
        var nowUtc = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, localZone));

        var todaysItemIds = contentItems.GetAll()
            .Where(item => ContentListing.IsInPeriod(item, ContentPeriod.Today, nowUtc, localZone))
            .Select(item => item.Id)
            .OrderBy(id => id)
            .ToList();
        if (todaysItemIds.Count == 0)
        {
            return;
        }

        var itemsHash = ComputeHash(todaysItemIds);
        var existing = summaries.GetDailyDigest(today);
        if (existing is not null && (existing.ItemsHash == itemsHash || nowUtc - existing.CreatedAtUtc < _options.DailyDigestMinInterval))
        {
            return;
        }

        var readySummaries = summaries.GetReady().Where(summary => todaysItemIds.Contains(summary.ContentItemId)).ToList();
        if (readySummaries.Count == 0)
        {
            return;
        }

        var digestPrompt = prompts.Load(DailyDigestPromptName);
        var schema = prompts.LoadSchema(DailyDigestPromptName);
        var variables = new Dictionary<string, string>
        {
            ["date"] = today.ToString("yyyy-MM-dd"),
            ["summaries"] = BuildDigestSummariesText(readySummaries),
        };
        var userPrompt = prompts.Render(digestPrompt.UserTemplate, variables);

        string json;
        try
        {
            json = await client.GenerateJsonAsync(digestPrompt.System, userPrompt, schema, "a daily digest", cancellationToken);
        }
        catch (GeminiApiException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning("Gemini quota reached while generating the daily digest; resuming on the next cycle.");
            return;
        }
        catch (GeminiApiException ex)
        {
            logger.LogError(ex, "Could not generate the daily digest.");
            return;
        }

        if (!DailyDigest.TryParseFrase(json, out _, out var error))
        {
            logger.LogWarning("Gemini returned an invalid daily digest: {Error}", error);
            return;
        }

        summaries.SaveDailyDigest(today, json, itemsHash, client.Model);
    }

    private static bool TryValidate(ContentSummaryKind kind, string json, out string? error) => kind switch
    {
        ContentSummaryKind.Release => ReleaseSummary.TryParse(json, out _, out error),
        ContentSummaryKind.Video => VideoSummary.TryParse(json, out _, out error),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static bool IsWithinMaxAge(ContentItem item, int maxAgeDays) =>
        DateTime.UtcNow - (item.PublishedAtUtc ?? item.DetectedAtUtc) <= TimeSpan.FromDays(maxAgeDays);

    private static Dictionary<string, string> BuildVariables(SummaryCandidate candidate)
    {
        var publishedAt = candidate.Item.PublishedAtUtc?.ToString("yyyy-MM-dd") ?? "desconocido";
        var isRelease = candidate.Kind == ContentSummaryKind.Release;
        var notes = string.IsNullOrWhiteSpace(candidate.Item.Description)
            ? isRelease ? "(este release no tiene notas)" : "(este video no tiene descripción)"
            : candidate.Item.Description;

        return isRelease
            ? new Dictionary<string, string>
            {
                ["projectName"] = candidate.SourceName,
                ["title"] = candidate.Item.Title,
                ["publishedAt"] = publishedAt,
                ["url"] = candidate.Item.Url,
                ["notes"] = notes,
            }
            : new Dictionary<string, string>
            {
                ["channelName"] = candidate.SourceName,
                ["title"] = candidate.Item.Title,
                ["publishedAt"] = publishedAt,
                ["url"] = candidate.Item.Url,
                ["notes"] = notes,
            };
    }

    private static string BuildDigestSummariesText(IReadOnlyList<ContentSummary> readySummaries)
    {
        var lines = new List<string>();
        foreach (var summary in readySummaries)
        {
            if (summary.Json is not { } json)
            {
                continue;
            }

            if (summary.Kind == ContentSummaryKind.Release && ReleaseSummary.TryParse(json, out var release, out _))
            {
                var accion = release!.Accion is { } action ? $" (acción: {action})" : "";
                lines.Add($"- {string.Join(" ", release.Puntos)}{accion}");
            }
            else if (summary.Kind == ContentSummaryKind.Video && VideoSummary.TryParse(json, out var video, out _))
            {
                lines.Add($"- {video!.DeQueTrata}");
            }
        }

        return string.Join("\n", lines);
    }

    private static string ComputeHash(IReadOnlyList<long> ids) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", ids))));

    private static SummaryOptions Validate(SummaryOptions options)
    {
        if (options.PollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), $"{SummaryOptions.SectionName}:PollingInterval must be positive.");
        }

        return options;
    }
}
