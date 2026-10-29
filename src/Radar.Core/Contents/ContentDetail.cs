namespace Radar.Core.Contents;

/// <summary>
/// A release's AI-generated detail "ficha", one per content item. <see cref="Json"/> holds the raw Gemini
/// response (parsed with <see cref="ReleaseDetail.TryParse"/>) when <see cref="Status"/> is
/// <see cref="ContentSummaryStatus.Ready"/>, or diagnostic text otherwise. Reuses <see cref="ContentSummaryStatus"/>:
/// the same two states (ready/error) apply here.
/// </summary>
public sealed record ContentDetail(
    long ContentItemId,
    string? Json,
    int PromptVersion,
    string Model,
    ContentSummaryStatus Status,
    string? Error,
    DateTime CreatedAtUtc);
