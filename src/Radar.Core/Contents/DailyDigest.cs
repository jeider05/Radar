using System.Text.Json;

namespace Radar.Core.Contents;

/// <summary>
/// "Tu radar hoy": one AI-generated sentence per local date, built only from the day's already-generated
/// <see cref="ContentSummary"/> rows. Regenerated only when <see cref="ItemsHash"/> changes.
/// </summary>
public sealed record DailyDigest(DateOnly Date, string Json, string ItemsHash, string Model, DateTime CreatedAtUtc)
{
    public const int MaxFraseLength = 200;

    /// <summary>The validated sentence held in <see cref="Json"/>. Digests are only ever stored when valid.</summary>
    public string Frase => TryParseFrase(Json, out var frase, out _) && frase is not null ? frase : Json;

    /// <summary>Parses and validates the JSON Gemini returns for the daily-digest prompt.</summary>
    public static bool TryParseFrase(string json, out string? frase, out string? error)
    {
        frase = null;

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(json, JsonOptions);
        }
        catch (JsonException)
        {
            error = "La respuesta no es JSON válido.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload?.Frase) || payload.Frase.Length > MaxFraseLength)
        {
            error = $"\"frase\" debe ser un texto no vacío de hasta {MaxFraseLength} caracteres.";
            return false;
        }

        error = null;
        frase = payload.Frase.Trim();
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record Payload(string? Frase);
}
