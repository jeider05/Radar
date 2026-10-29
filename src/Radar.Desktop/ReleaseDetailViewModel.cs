using Radar.Core.Contents;

namespace Radar.Desktop;

/// <summary>One installation command, with its copy-ready text already including the "# opcional" marker.</summary>
internal sealed record InstallCommandView(string Comando, string DisplayText)
{
    public static InstallCommandView From(InstallCommand command) =>
        new(command.Comando, command.Opcional ? $"{command.Comando}  # opcional" : command.Comando);
}

/// <summary>Everything <see cref="ReleaseDetailView"/> needs to render, already resolved (ready, unavailable, or a video).</summary>
internal sealed record ReleaseDetailViewModel(
    string MetaLine,
    string Title,
    bool IsVideo,
    string? Resumen,
    string? AffectsLead,
    string? AffectsText,
    IReadOnlyList<ReleaseChange> Cambios,
    IReadOnlyList<InstallCommandView> Instalacion,
    string InstallSystemLabel,
    IReadOnlyList<string> PlanDeUso,
    IReadOnlyList<VersionEntry> Versiones,
    bool ShowAiBadge,
    bool ShowRegenerate,
    string? UnavailableReason,
    bool IsLoading,
    string? Notes,
    bool NotesExpanded,
    string OpenLinkLabel,
    string Url)
{
    public bool ShowResumen => !string.IsNullOrWhiteSpace(Resumen);

    public bool ShowAffects => !string.IsNullOrWhiteSpace(AffectsText);

    public bool ShowCambios => Cambios.Count > 0;

    public bool ShowInstalacion => Instalacion.Count > 0;

    public bool ShowPlanDeUso => PlanDeUso.Count > 0;

    public bool ShowVersiones => Versiones.Count > 0;

    public bool ShowNotesToggle => !string.IsNullOrWhiteSpace(Notes);

    public bool ShowNotes => NotesExpanded && !string.IsNullOrWhiteSpace(Notes);

    public bool ShowUnavailableReason => !string.IsNullOrWhiteSpace(UnavailableReason);

    public string NotesToggleLabel => NotesExpanded ? "Ocultar notas originales" : "Ver notas originales";
}
