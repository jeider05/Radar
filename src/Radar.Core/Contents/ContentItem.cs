namespace Radar.Core.Contents;

/// <summary>
/// A piece of content detected in a source, such as a GitHub release or a YouTube video.
/// </summary>
public sealed record ContentItem(
    long Id,
    long SourceId,
    string ExternalId,
    string Title,
    string Url,
    string? Description,
    DateTime? PublishedAtUtc,
    DateTime DetectedAtUtc,
    bool IsRead);
