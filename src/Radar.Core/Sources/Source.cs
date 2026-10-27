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
    DateTime CreatedAtUtc)
{
    /// <summary>
    /// Short name for lists: a GitHub "owner/repository" shows only the repository; other sources show their name.
    /// </summary>
    public string DisplayName => SourceType == SourceType.GitHub && Name.LastIndexOf('/') is var slash and >= 0 && slash < Name.Length - 1
        ? Name[(slash + 1)..]
        : Name;
}
