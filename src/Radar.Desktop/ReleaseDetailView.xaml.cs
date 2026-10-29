using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Radar.Core.Contents;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Gemini;

namespace Radar.Desktop;

/// <summary>
/// The embedded detail view for a release or video: shows the AI-generated "ficha" (generating it once if
/// missing), or the raw notes when Gemini is not configured or generation fails.
/// </summary>
public partial class ReleaseDetailView : UserControl
{
    private readonly ContentItemStore _contentItems;
    private readonly ReleaseDetailGenerator _generator;
    private readonly bool _geminiConfigured;
    private readonly Action _goBack;

    private NewsItemView? _current;
    private bool _notesExpanded;

    public ReleaseDetailView(ContentItemStore contentItems, ReleaseDetailGenerator generator, bool geminiConfigured, Action goBack)
    {
        InitializeComponent();
        _contentItems = contentItems;
        _generator = generator;
        _geminiConfigured = geminiConfigured;
        _goBack = goBack;
    }

    /// <summary>Shows an item's detail. Opening a detail never marks it as read.</summary>
    public async Task ShowAsync(NewsItemView item)
    {
        _current = item;
        _notesExpanded = false;

        if (item.IsVideo)
        {
            DataContext = BuildVideoModel(item);
            return;
        }

        DataContext = BuildLoadingModel(item);
        ReleaseDetail? detail;
        try
        {
            detail = await _generator.EnsureGeneratedAsync(item.Item, item.SourceName);
        }
        catch (Exception)
        {
            detail = null;
        }

        if (_current != item)
        {
            return; // The user navigated to something else while this was generating.
        }

        Render(item, detail, forceNotesExpanded: detail is null);
    }

    private async void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { IsVideo: false } item)
        {
            return;
        }

        DataContext = BuildLoadingModel(item);
        ReleaseDetail? detail;
        try
        {
            detail = await _generator.RegenerateAsync(item.Item, item.SourceName);
        }
        catch (Exception)
        {
            detail = null;
        }

        if (_current != item)
        {
            return;
        }

        Render(item, detail, forceNotesExpanded: detail is null);
    }

    private void Render(NewsItemView item, ReleaseDetail? detail, bool forceNotesExpanded)
    {
        _notesExpanded = forceNotesExpanded;
        DataContext = detail is not null ? BuildReadyModel(item, detail) : BuildUnavailableModel(item);
    }

    private ReleaseDetailViewModel BuildLoadingModel(NewsItemView item) => new(
        MetaLine: $"{item.SourceName} · {item.DateText}",
        Title: item.Title,
        IsVideo: false,
        Resumen: null, AffectsLead: null, AffectsText: null,
        Cambios: [], Instalacion: [], InstallSystemLabel: "", PlanDeUso: [], Versiones: [],
        ShowAiBadge: false, ShowRegenerate: false, UnavailableReason: null, IsLoading: true,
        Notes: item.Item.Description, NotesExpanded: false,
        OpenLinkLabel: "Ver en GitHub ↗", Url: item.Url);

    private ReleaseDetailViewModel BuildReadyModel(NewsItemView item, ReleaseDetail detail)
    {
        var metaLine = (detail.VersionAnterior, detail.VersionNueva) switch
        {
            ({ } before, { } after) => $"{item.SourceName} · {before} → {after} · {item.DateText}",
            (null, { } after) => $"{item.SourceName} · {after} · {item.DateText}",
            _ => $"{item.SourceName} · {item.DateText}",
        };
        var lead = detail.TeAfecta.Nivel switch
        {
            AffectsLevel.Si => "Te afecta.",
            AffectsLevel.Opcional => "Te afecta si usás una función opcional.",
            _ => "No te afecta.",
        };
        var plan = detail.PlanDeUso.Select((step, i) => $"{i + 1}. {step}").ToList();

        return new ReleaseDetailViewModel(
            MetaLine: metaLine,
            Title: item.Title,
            IsVideo: false,
            Resumen: detail.Resumen,
            AffectsLead: lead,
            AffectsText: detail.TeAfecta.Texto,
            Cambios: detail.Cambios,
            Instalacion: detail.Instalacion.Select(InstallCommandView.From).ToList(),
            InstallSystemLabel: $"{UserEnvironment.DetectOs()} · {UserEnvironment.DetectShell()}",
            PlanDeUso: plan,
            Versiones: detail.Versiones,
            ShowAiBadge: true,
            ShowRegenerate: true,
            UnavailableReason: null,
            IsLoading: false,
            Notes: item.Item.Description,
            NotesExpanded: _notesExpanded,
            OpenLinkLabel: "Ver en GitHub ↗",
            Url: item.Url);
    }

    private ReleaseDetailViewModel BuildUnavailableModel(NewsItemView item)
    {
        var reason = _geminiConfigured
            ? "No se pudo generar la ficha para esta novedad."
            : $"Configura {GeminiClient.ApiKeyVariable} para ver la ficha.";

        return new ReleaseDetailViewModel(
            MetaLine: $"{item.SourceName} · {item.DateText}",
            Title: item.Title,
            IsVideo: false,
            Resumen: null, AffectsLead: null, AffectsText: null,
            Cambios: [], Instalacion: [], InstallSystemLabel: "", PlanDeUso: [], Versiones: [],
            ShowAiBadge: false,
            ShowRegenerate: _geminiConfigured,
            UnavailableReason: reason,
            IsLoading: false,
            Notes: item.Item.Description,
            NotesExpanded: _notesExpanded,
            OpenLinkLabel: "Ver en GitHub ↗",
            Url: item.Url);
    }

    private ReleaseDetailViewModel BuildVideoModel(NewsItemView item) => new(
        MetaLine: $"{item.SourceName} · Video · {item.DateText}",
        Title: item.Title,
        IsVideo: true,
        Resumen: item.VideoSummaryText,
        AffectsLead: null, AffectsText: null,
        Cambios: [], Instalacion: [], InstallSystemLabel: "", PlanDeUso: [], Versiones: [],
        ShowAiBadge: item.News.SummaryStatus == SummaryDisplayStatus.Ready,
        ShowRegenerate: false,
        UnavailableReason: null,
        IsLoading: false,
        Notes: item.Item.Description,
        NotesExpanded: false,
        OpenLinkLabel: "Ver en YouTube ↗",
        Url: item.Url);

    private void Back_Click(object sender, RoutedEventArgs e) => _goBack();

    private void ToggleNotes_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReleaseDetailViewModel model || _current is null)
        {
            return;
        }

        _notesExpanded = !model.NotesExpanded;
        DataContext = model with { NotesExpanded = _notesExpanded };
    }

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is InstallCommandView command)
        {
            Clipboard.SetText(command.Comando);
        }
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReleaseDetailViewModel model)
        {
            return;
        }

        if (!Uri.TryCreate(model.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening the browser is best-effort here; the Novedades list reports the same kind of failure
            // via StatusText when the user opens a link from there instead.
        }
    }
}
