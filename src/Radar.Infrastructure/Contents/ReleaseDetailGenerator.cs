using System.Net;
using Radar.Core.Contents;
using Radar.Infrastructure.Gemini;
using Radar.Infrastructure.Prompts;

namespace Radar.Infrastructure.Contents;

/// <summary>
/// Generates (and caches) a release's AI detail "ficha". Used both by the Worker's background cycle and by the
/// Desktop view on demand, so there is exactly one place that knows how to build one.
/// </summary>
public sealed class ReleaseDetailGenerator(GeminiClient? gemini, ContentDetailStore details, IPromptProvider prompts)
{
    public const string PromptName = "Detalle/release-detail";

    /// <summary>
    /// Returns the item's ready ficha, generating and saving one first when it is missing or used an older
    /// prompt version. Returns null when Gemini is not configured, there are no notes to work from, the quota
    /// was reached, or the response is invalid; callers fall back to the raw release notes.
    /// </summary>
    public async Task<ReleaseDetail?> EnsureGeneratedAsync(ContentItem item, string sourceName, CancellationToken cancellationToken = default)
    {
        var prompt = prompts.Load(PromptName);
        var existing = details.Get(item.Id);
        if (existing is { Status: ContentSummaryStatus.Ready, Json: { } readyJson } && existing.PromptVersion == prompt.Version)
        {
            return ReleaseDetail.TryParse(readyJson, out var cached, out _) ? cached : null;
        }

        if (gemini is null || string.IsNullOrWhiteSpace(item.Description))
        {
            return null;
        }

        var schema = prompts.LoadSchema(PromptName);
        var variables = new Dictionary<string, string>
        {
            ["projectName"] = sourceName,
            ["title"] = item.Title,
            ["publishedAt"] = item.PublishedAtUtc?.ToString("yyyy-MM-dd") ?? "desconocido",
            ["url"] = item.Url,
            ["userOs"] = UserEnvironment.DetectOs(),
            ["userShell"] = UserEnvironment.DetectShell(),
            ["notes"] = item.Description,
        };
        var userPrompt = prompts.Render(prompt.UserTemplate, variables);

        string json;
        try
        {
            json = await gemini.GenerateJsonAsync(prompt.System, userPrompt, schema, "a release detail", cancellationToken);
        }
        catch (GeminiApiException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Quota reached: try again later, without recording an error.
            return null;
        }
        catch (GeminiApiException ex)
        {
            details.SaveError(item.Id, prompt.Version, gemini.Model, ex.Message);
            return null;
        }

        if (!ReleaseDetail.TryParse(json, out var detail, out var error))
        {
            details.SaveError(item.Id, prompt.Version, gemini.Model, error ?? "Respuesta inválida.", json);
            return null;
        }

        details.SaveReady(item.Id, json, prompt.Version, gemini.Model);
        return detail;
    }
}
