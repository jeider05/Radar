using System.Text.Json;

namespace Radar.Core.Contents;

/// <summary>Short summary of a GitHub release, grounded only in its notes.</summary>
public sealed record ReleaseSummary(IReadOnlyList<string> Puntos, string? Accion)
{
    public const int MaxPuntos = 3;
    public const int MaxPuntoLength = 140;

    /// <summary>Parses and validates the JSON Gemini returns for the release-summary prompt.</summary>
    public static bool TryParse(string json, out ReleaseSummary? summary, out string? error)
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

        if (payload?.Puntos is not { Count: >= 1 and <= MaxPuntos } puntos
            || puntos.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > MaxPuntoLength))
        {
            error = $"\"puntos\" debe tener entre 1 y {MaxPuntos} frases no vacías de hasta {MaxPuntoLength} caracteres.";
            return false;
        }

        error = null;
        summary = new ReleaseSummary(
            puntos.Select(p => p.Trim()).ToList(),
            string.IsNullOrWhiteSpace(payload.Accion) ? null : payload.Accion.Trim());
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record Payload(IReadOnlyList<string>? Puntos, string? Accion);
}
