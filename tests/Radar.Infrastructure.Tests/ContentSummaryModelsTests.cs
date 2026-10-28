using Radar.Core.Contents;

namespace Radar.Infrastructure.Tests;

public sealed class ContentSummaryModelsTests
{
    [Fact]
    public void ReleaseSummary_parses_valid_json_with_action()
    {
        var ok = ReleaseSummary.TryParse("""{"puntos":["Agrega X","Corrige Y"],"accion":"npm install"}""", out var summary, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(["Agrega X", "Corrige Y"], summary!.Puntos);
        Assert.Equal("npm install", summary.Accion);
    }

    [Fact]
    public void ReleaseSummary_parses_a_null_action_as_null()
    {
        var ok = ReleaseSummary.TryParse("""{"puntos":["Agrega X"],"accion":null}""", out var summary, out _);

        Assert.True(ok);
        Assert.Null(summary!.Accion);
    }

    [Fact]
    public void ReleaseSummary_rejects_zero_puntos()
    {
        var ok = ReleaseSummary.TryParse("""{"puntos":[]}""", out var summary, out var error);

        Assert.False(ok);
        Assert.Null(summary);
        Assert.NotNull(error);
    }

    [Fact]
    public void ReleaseSummary_rejects_more_than_three_puntos()
    {
        var ok = ReleaseSummary.TryParse("""{"puntos":["a","b","c","d"]}""", out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void ReleaseSummary_rejects_a_punto_longer_than_140_characters()
    {
        var ok = ReleaseSummary.TryParse($$"""{"puntos":["{{new string('x', 141)}}"]}""", out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void ReleaseSummary_rejects_invalid_json()
    {
        var ok = ReleaseSummary.TryParse("no es json", out var summary, out var error);

        Assert.False(ok);
        Assert.Null(summary);
        Assert.NotNull(error);
    }

    [Fact]
    public void VideoSummary_parses_valid_json()
    {
        var ok = VideoSummary.TryParse("""{"deQueTrata":"Explica cómo usar X."}""", out var summary, out _);

        Assert.True(ok);
        Assert.Equal("Explica cómo usar X.", summary!.DeQueTrata);
    }

    [Fact]
    public void VideoSummary_rejects_empty_text()
    {
        var ok = VideoSummary.TryParse("""{"deQueTrata":""}""", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void VideoSummary_rejects_text_longer_than_180_characters()
    {
        var ok = VideoSummary.TryParse($$"""{"deQueTrata":"{{new string('x', 181)}}"}""", out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void DailyDigest_TryParseFrase_parses_valid_json()
    {
        var ok = DailyDigest.TryParseFrase("""{"frase":"Hoy salió jq 1.8.2."}""", out var frase, out _);

        Assert.True(ok);
        Assert.Equal("Hoy salió jq 1.8.2.", frase);
    }

    [Fact]
    public void DailyDigest_TryParseFrase_rejects_text_longer_than_200_characters()
    {
        var ok = DailyDigest.TryParseFrase($$"""{"frase":"{{new string('x', 201)}}"}""", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void DailyDigest_Frase_reads_the_sentence_stored_in_Json()
    {
        var digest = new DailyDigest(new DateOnly(2026, 10, 27), """{"frase":"Hoy salió jq 1.8.2."}""", "hash", "gemini-test", DateTime.UtcNow);

        Assert.Equal("Hoy salió jq 1.8.2.", digest.Frase);
    }
}
