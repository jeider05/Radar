namespace Radar.Core.Sources;

/// <summary>
/// An external source that Radar can monitor, such as a GitHub repository or a YouTube channel.
/// </summary>
public sealed record Source(
    long Id,
    SourceType SourceType,
    string ExternalId,
    string Name,
    string Url,
    bool IsActive,
    DateTime CreatedAtUtc);
