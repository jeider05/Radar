using System.Net;
using Microsoft.Data.Sqlite;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.GitHub;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class GitHubReleaseImporterTests : IDisposable
{
    private const string ReleasesJson = """
        [
          { "id": 222, "tag_name": "v2.0.0", "name": "Version 2", "html_url": "https://github.com/example-org/example-repo/releases/tag/v2.0.0", "body": null, "published_at": "2026-10-02T08:15:00Z" },
          { "id": 111, "tag_name": "v1.0.0", "name": "Version 1", "html_url": "https://github.com/example-org/example-repo/releases/tag/v1.0.0", "body": null, "published_at": "2026-09-01T08:15:00Z" }
        ]
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _contentItems;

    public GitHubReleaseImporterTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _database.Initialize();
        _sources = new SourceStore(_database);
        _contentItems = new ContentItemStore(_database);
    }

    [Fact]
    public async Task Import_registers_source_and_stores_releases_by_release_id()
    {
        var result = await CreateImporter(ReleasesJson).ImportAsync("example-org", "example-repo");

        var source = _sources.Find(SourceType.GitHub, "example-org/example-repo");
        Assert.NotNull(source);
        Assert.Equal("https://github.com/example-org/example-repo", source.Url);
        Assert.Equal(new GitHubImportResult(source.Id, ReleasesFound: 2, ReleasesAdded: 2), result);

        var items = _contentItems.GetBySource(source.Id);
        Assert.Equal(["222", "111"], items.Select(i => i.ExternalId)); // newest release is the most recently detected
        Assert.Equal(new DateTime(2026, 10, 2, 8, 15, 0, DateTimeKind.Utc), items[0].PublishedAtUtc);
    }

    [Fact]
    public async Task Importing_again_does_not_duplicate_source_or_releases()
    {
        await CreateImporter(ReleasesJson).ImportAsync("example-org", "example-repo");

        var second = await CreateImporter(ReleasesJson).ImportAsync("Example-Org", "Example-Repo");

        Assert.Equal(0, second.ReleasesAdded);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM Sources;"));
        Assert.Equal(2L, Scalar("SELECT COUNT(*) FROM ContentItems;"));
    }

    [Fact]
    public async Task Failed_request_stores_nothing()
    {
        var importer = CreateImporter("{}", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<GitHubApiException>(() => importer.ImportAsync("example-org", "missing"));

        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM Sources;"));
    }

    private GitHubReleaseImporter CreateImporter(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new GitHubReleasesClient(new HttpClient(new FakeHttpMessageHandler(status, json))), _sources, _contentItems);

    private long Scalar(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
