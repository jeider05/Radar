using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radar.Core.Contents;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Gemini;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Prompts;
using Radar.Infrastructure.Sources;
using Radar.Worker;

namespace Radar.Infrastructure.Tests;

public sealed class ContentSummaryMonitorTests : IDisposable
{
    private const string TestApiKey = "test-key-not-real";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests.SummaryMonitor", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _items;
    private readonly ContentSummaryStore _summaries;
    private readonly ContentDetailStore _details;
    private readonly IPromptProvider _prompts = new PromptLibrary();

    public ContentSummaryMonitorTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _database.Initialize();
        _sources = new SourceStore(_database);
        _items = new ContentItemStore(_database);
        _summaries = new ContentSummaryStore(_database);
        _details = new ContentDetailStore(_database);
    }

    [Fact]
    public async Task RunCycleAsync_without_gemini_configured_does_nothing()
    {
        var item = AddGitHubItem("v1");
        var monitor = CreateMonitor(gemini: null, new FakeHttpMessageHandler(HttpStatusCode.OK));

        await monitor.RunCycleAsync(CancellationToken.None);
        await monitor.RunCycleAsync(CancellationToken.None);

        Assert.Null(_summaries.Get(item.Id));
    }

    [Fact]
    public async Task RunCycleAsync_generates_and_saves_a_ready_release_summary()
    {
        var item = AddGitHubItem("v1", description: "Notas del release.");
        var monitor = CreateMonitor(CreateSmartHandler());

        await monitor.RunCycleAsync(CancellationToken.None);

        var summary = _summaries.Get(item.Id);
        Assert.NotNull(summary);
        Assert.Equal(ContentSummaryKind.Release, summary.Kind);
        Assert.Equal(ContentSummaryStatus.Ready, summary.Status);
        Assert.Contains("puntos", summary.Json);
    }

    [Fact]
    public async Task RunCycleAsync_generates_and_saves_a_ready_video_summary()
    {
        var item = AddYouTubeItem("abc", description: "Descripción del video.");
        var monitor = CreateMonitor(CreateSmartHandler());

        await monitor.RunCycleAsync(CancellationToken.None);

        var summary = _summaries.Get(item.Id);
        Assert.NotNull(summary);
        Assert.Equal(ContentSummaryKind.Video, summary.Kind);
        Assert.Equal(ContentSummaryStatus.Ready, summary.Status);
    }

    [Fact]
    public async Task RunCycleAsync_respects_MaxItemsPerCycle()
    {
        var oldest = AddGitHubItem("v1", publishedAtUtc: DateTime.UtcNow.AddDays(-3));
        var middle = AddGitHubItem("v2", publishedAtUtc: DateTime.UtcNow.AddDays(-2));
        var newest = AddGitHubItem("v3", publishedAtUtc: DateTime.UtcNow.AddDays(-1));
        var monitor = CreateMonitor(CreateSmartHandler(), maxItemsPerCycle: 2);

        await monitor.RunCycleAsync(CancellationToken.None);

        Assert.NotNull(_summaries.Get(newest.Id));
        Assert.NotNull(_summaries.Get(middle.Id));
        Assert.Null(_summaries.Get(oldest.Id));
    }

    [Fact]
    public async Task RunCycleAsync_skips_an_item_already_summarized_with_the_current_prompt_version()
    {
        var item = AddGitHubItem("v1");
        var currentVersion = _prompts.Load("Novedades/release-summary").Version;
        var original = _summaries.SaveReady(item.Id, ContentSummaryKind.Release, """{"puntos":["Original"]}""", currentVersion, "gemini-test");
        var monitor = CreateMonitor(CreateSmartHandler());

        await monitor.RunCycleAsync(CancellationToken.None);

        var summary = _summaries.Get(item.Id);
        Assert.Equal(original.Json, summary!.Json);
        Assert.Equal(original.CreatedAtUtc, summary.CreatedAtUtc);
    }

    [Fact]
    public async Task RunCycleAsync_regenerates_an_item_summarized_with_an_older_prompt_version()
    {
        var item = AddGitHubItem("v1");
        var currentVersion = _prompts.Load("Novedades/release-summary").Version;
        _summaries.SaveReady(item.Id, ContentSummaryKind.Release, """{"puntos":["Viejo"]}""", currentVersion - 1, "gemini-old");
        var monitor = CreateMonitor(CreateSmartHandler());

        await monitor.RunCycleAsync(CancellationToken.None);

        var summary = _summaries.Get(item.Id);
        Assert.Equal(currentVersion, summary!.PromptVersion);
        Assert.Contains("Resumen generado", summary.Json);
    }

    [Fact]
    public async Task RunCycleAsync_saves_error_status_when_gemini_returns_an_invalid_summary()
    {
        var item = AddGitHubItem("v1");
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, GeminiResponse("""{"puntos":[]}"""));
        var monitor = CreateMonitor(handler);

        await monitor.RunCycleAsync(CancellationToken.None);

        var summary = _summaries.Get(item.Id);
        Assert.NotNull(summary);
        Assert.Equal(ContentSummaryStatus.Error, summary.Status);
        Assert.NotNull(summary.Error);
        Assert.Single(handler.Requests); // the invalid summary produced no ready item, so no digest was attempted
    }

    [Fact]
    public async Task RunCycleAsync_stops_the_cycle_without_saving_an_error_on_quota_limit()
    {
        var first = AddGitHubItem("v1", publishedAtUtc: DateTime.UtcNow.AddDays(-1));
        var second = AddGitHubItem("v2", publishedAtUtc: DateTime.UtcNow.AddDays(-2));
        var handler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests, """{"error":{"message":"quota"}}""");
        var monitor = CreateMonitor(handler);

        await monitor.RunCycleAsync(CancellationToken.None);

        Assert.Single(handler.Requests); // stops after the first 429, the second item is not attempted
        Assert.Null(_summaries.Get(first.Id));
        Assert.Null(_summaries.Get(second.Id));
    }

    [Fact]
    public async Task RunCycleAsync_generates_the_daily_digest_from_todays_ready_summaries()
    {
        AddGitHubItem("v1", description: "Notas del release.", publishedAtUtc: DateTime.UtcNow);
        var monitor = CreateMonitor(CreateSmartHandler());

        await monitor.RunCycleAsync(CancellationToken.None);

        var digest = _summaries.GetDailyDigest(DateOnly.FromDateTime(DateTime.Now));
        Assert.NotNull(digest);
        Assert.Equal("Resumen del día", digest.Frase);
    }

    [Fact]
    public async Task RunCycleAsync_does_not_regenerate_the_digest_when_its_items_do_not_change()
    {
        AddGitHubItem("v1", description: "Notas del release.", publishedAtUtc: DateTime.UtcNow);
        var monitor = CreateMonitor(CreateSmartHandler());
        await monitor.RunCycleAsync(CancellationToken.None);
        var firstDigest = _summaries.GetDailyDigest(DateOnly.FromDateTime(DateTime.Now));

        await monitor.RunCycleAsync(CancellationToken.None);

        var secondDigest = _summaries.GetDailyDigest(DateOnly.FromDateTime(DateTime.Now));
        Assert.Equal(firstDigest!.CreatedAtUtc, secondDigest!.CreatedAtUtc);
    }

    [Fact]
    public async Task RunCycleAsync_generates_detail_fichas_bounded_by_MaxDetailsPerCycle()
    {
        var older = AddGitHubItem("v1", description: "Notas 1.", publishedAtUtc: DateTime.UtcNow.AddDays(-2));
        var newer = AddGitHubItem("v2", description: "Notas 2.", publishedAtUtc: DateTime.UtcNow.AddDays(-1));
        var monitor = CreateMonitor(CreateSmartHandler(), maxDetailsPerCycle: 1);

        await monitor.RunCycleAsync(CancellationToken.None);

        Assert.NotNull(_details.Get(newer.Id));
        Assert.Null(_details.Get(older.Id));
    }

    [Fact]
    public async Task RunCycleAsync_skips_a_detail_ficha_already_generated_with_the_current_prompt_version()
    {
        var item = AddGitHubItem("v1", description: "Notas 1.", publishedAtUtc: DateTime.UtcNow);
        var currentVersion = _prompts.Load(ReleaseDetailGenerator.PromptName).Version;
        var original = _details.SaveReady(item.Id, """{"resumen":"original"}""", currentVersion, "gemini-test");
        var monitor = CreateMonitor(CreateSmartHandler(), maxDetailsPerCycle: 5);

        await monitor.RunCycleAsync(CancellationToken.None);

        var detail = _details.Get(item.Id);
        Assert.Equal(original.Json, detail!.Json);
        Assert.Equal(original.CreatedAtUtc, detail.CreatedAtUtc);
    }

    [Fact]
    public void Non_positive_polling_interval_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateMonitor(new FakeHttpMessageHandler(HttpStatusCode.OK), pollingInterval: TimeSpan.Zero));
    }

    /// <summary>
    /// Responds to whichever prompt was sent, inferred from the response schema embedded in the request
    /// (each prompt's schema names its own field: "puntos", "deQueTrata" or "frase").
    /// </summary>
    private static FakeHttpMessageHandler CreateSmartHandler()
    {
        FakeHttpMessageHandler? handler = null;
        handler = new FakeHttpMessageHandler(_ =>
        {
            var body = handler!.RequestBodies[^1] ?? "";
            var json = body.Contains("\"puntos\"")
                ? """{"puntos":["Resumen generado"]}"""
                : body.Contains("\"deQueTrata\"")
                    ? """{"deQueTrata":"Video resumido"}"""
                    : body.Contains("\"resumen\"")
                        ? """{"resumen":"Ficha generada","teAfecta":{"nivel":"no","texto":"x"},"cambios":[],"instalacion":[],"planDeUso":[],"versiones":[]}"""
                        : """{"frase":"Resumen del día"}""";
            return FakeHttpMessageHandler.Response(HttpStatusCode.OK, GeminiResponse(json));
        });
        return handler;
    }

    /// <summary>Wraps a JSON payload the way Gemini's real generateContent response carries its text.</summary>
    private static string GeminiResponse(string text) =>
        JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } } } } });

    private ContentSummaryMonitor CreateMonitor(
        FakeHttpMessageHandler handler, int maxItemsPerCycle = 5, TimeSpan? pollingInterval = null, int maxDetailsPerCycle = 0) =>
        CreateMonitor(new GeminiClient(new HttpClient(handler), TestApiKey, "gemini-test-model"), handler, maxItemsPerCycle, pollingInterval, maxDetailsPerCycle);

    private ContentSummaryMonitor CreateMonitor(
        GeminiClient? gemini, FakeHttpMessageHandler handler, int maxItemsPerCycle = 5, TimeSpan? pollingInterval = null, int maxDetailsPerCycle = 0)
    {
        // maxDetailsPerCycle defaults to 0 so existing summary-focused tests are not affected by detail generation;
        // tests that exercise it explicitly raise the limit.
        var options = new SummaryOptions
        {
            MaxItemsPerCycle = maxItemsPerCycle,
            PollingInterval = pollingInterval ?? TimeSpan.FromMinutes(2),
            MaxDetailsPerCycle = maxDetailsPerCycle,
        };
        var generator = new ReleaseDetailGenerator(gemini, _details, _prompts);
        return new ContentSummaryMonitor(
            gemini, _summaries, _items, _details, generator, _prompts, Options.Create(options), NullLogger<ContentSummaryMonitor>.Instance);
    }

    private ContentItem AddGitHubItem(string externalId, string? description = null, DateTime? publishedAtUtc = null)
    {
        var source = _sources.TryAdd(SourceType.GitHub, $"org/{externalId}", $"org/{externalId}", $"https://github.com/org/{externalId}")!;
        return _items.TryAdd(source.Id, externalId, externalId, $"https://github.com/org/{externalId}/releases/{externalId}", description, publishedAtUtc)!;
    }

    private ContentItem AddYouTubeItem(string externalId, string? description = null, DateTime? publishedAtUtc = null)
    {
        var source = _sources.TryAdd(SourceType.YouTube, $"channel-{externalId}", $"Channel {externalId}", $"https://youtube.com/{externalId}")!;
        return _items.TryAdd(source.Id, externalId, externalId, $"https://youtube.com/watch?v={externalId}", description, publishedAtUtc)!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
