using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Radar.Core.Contents;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Gemini;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Prompts;
using Radar.Infrastructure.Sources;

namespace Radar.Infrastructure.Tests;

public sealed class ReleaseDetailGeneratorTests : IDisposable
{
    private const string TestApiKey = "test-key-not-real";
    private const string ValidFichaJson = """
        {"resumen":"x","teAfecta":{"nivel":"no","texto":"x"},"cambios":[],"instalacion":[],"planDeUso":[],"versiones":[]}
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests.DetailGenerator", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _items;
    private readonly ContentDetailStore _details;
    private readonly IPromptProvider _prompts = new PromptLibrary();

    public ReleaseDetailGeneratorTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _database.Initialize();
        _sources = new SourceStore(_database);
        _items = new ContentItemStore(_database);
        _details = new ContentDetailStore(_database);
    }

    [Fact]
    public async Task EnsureGeneratedAsync_without_gemini_returns_null_and_saves_nothing()
    {
        var item = AddItem();
        var generator = CreateGenerator(gemini: null, new FakeHttpMessageHandler(HttpStatusCode.OK));

        var detail = await generator.EnsureGeneratedAsync(item, "org/repo");

        Assert.Null(detail);
        Assert.Null(_details.Get(item.Id));
    }

    [Fact]
    public async Task EnsureGeneratedAsync_generates_and_saves_a_ready_ficha()
    {
        var item = AddItem();
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, GeminiResponse(ValidFichaJson));
        var generator = CreateGenerator(handler);

        var detail = await generator.EnsureGeneratedAsync(item, "org/repo");

        Assert.NotNull(detail);
        var stored = _details.Get(item.Id);
        Assert.Equal(ContentSummaryStatus.Ready, stored!.Status);
        var request = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(handler.RequestBodies[0]!);
        var user = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains("Notas del release.", user);
        Assert.Contains(UserEnvironment.DetectOs(), user);
        Assert.Equal(HttpMethod.Post, request.Method);
    }

    [Fact]
    public async Task EnsureGeneratedAsync_returns_the_cached_ficha_without_a_new_request()
    {
        var item = AddItem();
        var currentVersion = _prompts.Load(ReleaseDetailGenerator.PromptName).Version;
        _details.SaveReady(item.Id, ValidFichaJson, currentVersion, "gemini-test");
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, GeminiResponse(ValidFichaJson));
        var generator = CreateGenerator(handler);

        var detail = await generator.EnsureGeneratedAsync(item, "org/repo");

        Assert.NotNull(detail);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EnsureGeneratedAsync_regenerates_when_the_stored_ficha_used_an_older_prompt_version()
    {
        var item = AddItem();
        var currentVersion = _prompts.Load(ReleaseDetailGenerator.PromptName).Version;
        _details.SaveReady(item.Id, ValidFichaJson, currentVersion - 1, "gemini-old");
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, GeminiResponse(ValidFichaJson));
        var generator = CreateGenerator(handler);

        await generator.EnsureGeneratedAsync(item, "org/repo");

        Assert.Single(handler.Requests);
        Assert.Equal(currentVersion, _details.Get(item.Id)!.PromptVersion);
    }

    [Fact]
    public async Task EnsureGeneratedAsync_saves_error_status_on_an_invalid_response()
    {
        var item = AddItem();
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, GeminiResponse("""{"resumen":""}"""));
        var generator = CreateGenerator(handler);

        var detail = await generator.EnsureGeneratedAsync(item, "org/repo");

        Assert.Null(detail);
        Assert.Equal(ContentSummaryStatus.Error, _details.Get(item.Id)!.Status);
    }

    [Fact]
    public async Task EnsureGeneratedAsync_without_notes_returns_null_and_saves_nothing()
    {
        var source = _sources.TryAdd(SourceType.GitHub, "org/repo", "org/repo", "https://github.com/org/repo")!;
        var item = _items.TryAdd(source.Id, "v1", "v1", "https://github.com/org/repo/releases/v1", description: null)!;
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, GeminiResponse(ValidFichaJson));
        var generator = CreateGenerator(handler);

        var detail = await generator.EnsureGeneratedAsync(item, "org/repo");

        Assert.Null(detail);
        Assert.Null(_details.Get(item.Id));
        Assert.Empty(handler.Requests);
    }

    private ContentItem AddItem()
    {
        var source = _sources.TryAdd(SourceType.GitHub, "org/repo", "org/repo", "https://github.com/org/repo")!;
        return _items.TryAdd(source.Id, "v1", "v1", "https://github.com/org/repo/releases/v1", "Notas del release.")!;
    }

    private ReleaseDetailGenerator CreateGenerator(FakeHttpMessageHandler handler) =>
        CreateGenerator(new GeminiClient(new HttpClient(handler), TestApiKey, "gemini-test-model"), handler);

    private ReleaseDetailGenerator CreateGenerator(GeminiClient? gemini, FakeHttpMessageHandler handler) =>
        new(gemini, _details, _prompts);

    private static string GeminiResponse(string text) =>
        JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } } } } });

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
