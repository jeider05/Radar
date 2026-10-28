using System.Text.Json;

namespace Radar.Core.Contents;

/// <summary>Short summary of what a video is about, grounded only in its title and description.</summary>
public sealed record VideoSummary(string DeQueTrata)
{
    public const int MaxLength = 180;

    /// <summary>Parses and validates the JSON Gemini returns for the video-summary prompt.</summary>
    public static bool TryParse(string json, out VideoSummary? summary, out string? error)
    {
        summary = null;

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

        if (string.IsNullOrWhiteSpace(payload?.DeQueTrata) || payload.DeQueTrata.Length > MaxLength)
        {
            error = $"\"deQueTrata\" debe ser una frase no vacía de hasta {MaxLength} caracteres.";
            return false;
        }

        error = null;
        summary = new VideoSummary(payload.DeQueTrata.Trim());
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record Payload(string? DeQueTrata);
}
