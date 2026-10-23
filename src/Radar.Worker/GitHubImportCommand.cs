using Radar.Infrastructure.Contents;
using Radar.Infrastructure.GitHub;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;

namespace Radar.Worker;

/// <summary>
/// One-shot command: <c>Radar.Worker import-github-releases owner/repository</c>.
/// Imports the releases of a public repository into the Radar database and exits. It does not start the monitor.
/// </summary>
internal static class GitHubImportCommand
{
    public const string Name = "import-github-releases";

    public static async Task<int> RunAsync(string repositoryArgument)
    {
        var parts = repositoryArgument.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            Console.Error.WriteLine($"Usage: Radar.Worker {Name} <owner>/<repository>");
            return 2;
        }

        var (owner, repository) = (parts[0], parts[1]);

        var database = RadarDatabase.CreateDefault();
        database.Initialize();

        using var httpClient = GitHubReleasesClient.CreateHttpClient();
        var importer = new GitHubReleaseImporter(
            new GitHubReleasesClient(httpClient), new SourceStore(database), new ContentItemStore(database));

        try
        {
            var result = await importer.ImportAsync(owner, repository);
            Console.WriteLine(
                $"{owner}/{repository}: {result.ReleasesFound} releases found, {result.ReleasesAdded} new (source id {result.SourceId}).");
            return 0;
        }
        catch (GitHubApiException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine("GitHub did not respond in time.");
            return 1;
        }
    }
}
