using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Sources;

namespace Radar.Infrastructure.GitHub;

public sealed record GitHubImportResult(long SourceId, int ReleasesFound, int ReleasesAdded);

/// <summary>Registers a GitHub repository as a source and stores its releases as content items, skipping known ones.</summary>
public sealed class GitHubReleaseImporter(GitHubReleasesClient client, SourceStore sources, ContentItemStore contentItems)
{
    public async Task<GitHubImportResult> ImportAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        var releases = await client.GetReleasesAsync(owner, repository, cancellationToken);

        // GitHub owner and repository names are case-insensitive.
        var externalId = $"{owner}/{repository}".ToLowerInvariant();
        var source = sources.Find(SourceType.GitHub, externalId)
            ?? sources.TryAdd(SourceType.GitHub, externalId, $"{owner}/{repository}", $"https://github.com/{owner}/{repository}")
            ?? throw new InvalidOperationException($"Could not register source '{externalId}'.");

        // GitHub returns newest first; insert oldest first so the newest release is the most recently detected item.
        var added = 0;
        foreach (var release in releases.Reverse())
        {
            var item = contentItems.TryAdd(
                source.Id,
                release.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                release.Title,
                release.Url,
                release.Description,
                release.PublishedAtUtc);
            if (item is not null)
            {
                added++;
            }
        }

        return new GitHubImportResult(source.Id, releases.Count, added);
    }
}
