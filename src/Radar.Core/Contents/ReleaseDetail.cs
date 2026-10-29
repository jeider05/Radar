using System.Text.Json;

namespace Radar.Core.Contents;

public enum AffectsLevel
{
    Si,
    Opcional,
    No,
}

public enum ReleaseChangeType
{
    Nuevo,
    Cambio,
    Arreglo,
    Importante,
}

public sealed record AffectsInfo(AffectsLevel Nivel, string Texto);

public sealed record ReleaseChange(ReleaseChangeType Tipo, string Texto);

public sealed record InstallCommand(string Comando, bool Opcional, bool Deducido);

public sealed record VersionEntry(string Etiqueta, string Valor);

/// <summary>The full AI-generated "ficha" of a release: impact, changes, installation, usage plan and versions.</summary>
public sealed record ReleaseDetail(
    string Resumen,
    AffectsInfo TeAfecta,
    IReadOnlyList<ReleaseChange> Cambios,
    IReadOnlyList<InstallCommand> Instalacion,
    IReadOnlyList<string> PlanDeUso,
    IReadOnlyList<VersionEntry> Versiones)
{
    public const int MaxResumenLength = 220;

    /// <summary>"Antes"/"Ahora" entries from <see cref="Versiones"/>, when the model found them in the notes.</summary>
    public string? VersionAnterior => Versiones.FirstOrDefault(v => v.Etiqueta.Equals("Antes", StringComparison.OrdinalIgnoreCase))?.Valor;

    public string? VersionNueva => Versiones.FirstOrDefault(v => v.Etiqueta.Equals("Ahora", StringComparison.OrdinalIgnoreCase))?.Valor;

    /// <summary>Parses and validates the JSON Gemini returns for the release-detail prompt.</summary>
    public static bool TryParse(string json, out ReleaseDetail? detail, out string? error)
    {
        detail = null;

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

        if (string.IsNullOrWhiteSpace(payload?.Resumen) || payload.Resumen.Length > MaxResumenLength)
        {
            error = $"\"resumen\" debe ser un texto no vacío de hasta {MaxResumenLength} caracteres.";
            return false;
        }

        if (payload.TeAfecta is not { } affectsPayload
            || !Enum.TryParse<AffectsLevel>(affectsPayload.Nivel, ignoreCase: true, out var nivel)
            || string.IsNullOrWhiteSpace(affectsPayload.Texto))
        {
            error = "\"teAfecta\" debe tener un \"nivel\" válido (si, opcional o no) y un \"texto\" no vacío.";
            return false;
        }

        if (!TryParseChanges(payload.Cambios, out var cambios))
        {
            error = "\"cambios\" tiene un elemento con \"tipo\" o \"texto\" inválido.";
            return false;
        }

        if (!TryParseInstallCommands(payload.Instalacion, out var instalacion))
        {
            error = "\"instalacion\" tiene un elemento con \"comando\" vacío.";
            return false;
        }

        if (!TryParseVersions(payload.Versiones, out var versiones))
        {
            error = "\"versiones\" tiene un elemento con \"etiqueta\" o \"valor\" vacío.";
            return false;
        }

        var planDeUso = (payload.PlanDeUso ?? [])
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .Select(step => step.Trim())
            .ToList();

        error = null;
        detail = new ReleaseDetail(
            payload.Resumen.Trim(),
            new AffectsInfo(nivel, affectsPayload.Texto.Trim()),
            cambios,
            instalacion,
            planDeUso,
            versiones);
        return true;
    }

    private static bool TryParseChanges(IReadOnlyList<ChangePayload>? payload, out IReadOnlyList<ReleaseChange> changes)
    {
        changes = [];
        var list = new List<ReleaseChange>();
        foreach (var item in payload ?? [])
        {
            if (!Enum.TryParse<ReleaseChangeType>(item.Tipo, ignoreCase: true, out var tipo) || string.IsNullOrWhiteSpace(item.Texto))
            {
                return false;
            }

            list.Add(new ReleaseChange(tipo, item.Texto.Trim()));
        }

        changes = list;
        return true;
    }

    private static bool TryParseInstallCommands(IReadOnlyList<InstallPayload>? payload, out IReadOnlyList<InstallCommand> commands)
    {
        commands = [];
        var list = new List<InstallCommand>();
        foreach (var item in payload ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Comando))
            {
                return false;
            }

            list.Add(new InstallCommand(item.Comando.Trim(), item.Opcional, item.Deducido));
        }

        commands = list;
        return true;
    }

    private static bool TryParseVersions(IReadOnlyList<VersionPayload>? payload, out IReadOnlyList<VersionEntry> versions)
    {
        versions = [];
        var list = new List<VersionEntry>();
        foreach (var item in payload ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Etiqueta) || string.IsNullOrWhiteSpace(item.Valor))
            {
                return false;
            }

            list.Add(new VersionEntry(item.Etiqueta.Trim(), item.Valor.Trim()));
        }

        versions = list;
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record Payload(
        string? Resumen,
        AffectsPayload? TeAfecta,
        IReadOnlyList<ChangePayload>? Cambios,
        IReadOnlyList<InstallPayload>? Instalacion,
        IReadOnlyList<string>? PlanDeUso,
        IReadOnlyList<VersionPayload>? Versiones);

    private sealed record AffectsPayload(string? Nivel, string? Texto);

    private sealed record ChangePayload(string? Tipo, string? Texto);

    private sealed record InstallPayload(string? Comando, bool Opcional, bool Deducido);

    private sealed record VersionPayload(string? Etiqueta, string? Valor);
}
