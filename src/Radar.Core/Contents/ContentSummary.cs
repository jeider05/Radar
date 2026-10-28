namespace Radar.Core.Contents;

public enum ContentSummaryKind
{
    Release,
    Video,
}

public enum ContentSummaryStatus
{
    Ready,
    Error,
}

/// <summary>
/// A short AI-generated summary of a content item, one per item. <see cref="Json"/> holds the raw Gemini response
/// (parsed with <see cref="ReleaseSummary.TryParse"/> or <see cref="VideoSummary.TryParse"/> depending on <see cref="Kind"/>)
/// when <see cref="Status"/> is <see cref="ContentSummaryStatus.Ready"/>, or diagnostic text otherwise.
/// </summary>
public sealed record ContentSummary(
    long ContentItemId,
    ContentSummaryKind Kind,
    string? Json,
    int PromptVersion,
    string Model,
    ContentSummaryStatus Status,
    string? Error,
    DateTime CreatedAtUtc);
