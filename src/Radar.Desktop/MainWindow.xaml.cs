using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Radar.Core.Contents;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Gemini;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;

namespace Radar.Desktop;

/// <summary>
/// App shell: the sidebar (sources, navigation) on the left, and a swappable content area on the right that
/// shows either <see cref="NovedadesView"/> or <see cref="ReleaseDetailView"/>. Reads the same database as the
/// Worker through RadarDatabase.CreateDefault(). Never calls Gemini itself for the Novedades list; the detail
/// view may, through <see cref="ReleaseDetailGenerator"/>, the same Infrastructure service the Worker uses.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _contentItems;
    private readonly ContentSummaryStore _summaries;
    private readonly DispatcherTimer _autoRefreshTimer;

    private readonly NovedadesView _novedadesView;
    private readonly ReleaseDetailGenerator _detailGenerator;
    private ReleaseDetailView? _detailView;

    // Whether this Desktop process has GEMINI_API_KEY configured, checked once at startup.
    private readonly bool _geminiConfigured;

    // Last data shown, used to skip redrawing when nothing changed.
    private IReadOnlyList<Source>? _loadedSources;
    private IReadOnlyList<ContentItem>? _loadedItems;
    private IReadOnlyList<ContentSummary>? _loadedSummaries;
    private DailyDigest? _loadedDigest;
    private IReadOnlyList<NewsItem> _allNews = [];
    private IReadOnlyList<SourceFilterInfo> _sourceFilters = [];
    private SourceFilterInfo? _allFilter;

    // null = "Novedades" (all sources). The single source of truth for what the sidebar has selected.
    private long? _selectedSourceId;

    private bool _isLoading;
    private bool _reloadPending;
    private bool _isClosed;

    public MainWindow()
    {
        InitializeComponent();

        var database = _database = RadarDatabase.CreateDefault();
        _sources = new SourceStore(database);
        _contentItems = new ContentItemStore(database);
        _summaries = new ContentSummaryStore(database);
        var details = new ContentDetailStore(database);
        var prompts = new Radar.Infrastructure.Prompts.PromptLibrary();
        var gemini = GeminiClient.FromEnvironment(GeminiClient.CreateHttpClient());
        _geminiConfigured = gemini is not null;
        _detailGenerator = new ReleaseDetailGenerator(gemini, details, prompts);

        _novedadesView = new NovedadesView(_contentItems, ReloadAsync, ShowDetails);
        MainContent.Content = _novedadesView;

        _autoRefreshTimer = new DispatcherTimer { Interval = AutoRefreshInterval };
        _autoRefreshTimer.Tick += async (_, _) => await ReloadAsync();

        Loaded += async (_, _) =>
        {
            try
            {
                await Task.Run(database.Initialize);
            }
            catch (Exception ex)
            {
                _novedadesView.SetStatus($"No se pudo abrir la base de datos: {ex.Message}");
                return;
            }

            if (_isClosed)
            {
                return;
            }

            await ReloadAsync();
            _autoRefreshTimer.Start();
        };

        Closed += (_, _) =>
        {
            _isClosed = true;
            _autoRefreshTimer.Stop();
        };
    }

    /// <summary>
    /// Reads sources, content and AI summaries off the UI thread and redraws only when something changed,
    /// keeping the selected source, period, type and page. Requests made while a reload is running are queued,
    /// so reloads never overlap. Never writes to the database and never contacts Gemini.
    /// </summary>
    private async Task ReloadAsync()
    {
        if (_isClosed)
        {
            return;
        }

        if (_isLoading)
        {
            _reloadPending = true;
            return;
        }

        _isLoading = true;
        try
        {
            do
            {
                _reloadPending = false;
                await LoadOnceAsync();
            }
            while (_reloadPending && !_isClosed);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadOnceAsync()
    {
        IReadOnlyList<Source> sources;
        IReadOnlyList<ContentItem> items;
        IReadOnlyList<ContentSummary> summaries;
        DailyDigest? digest;
        try
        {
            (sources, items, summaries, digest) = await Task.Run(() => (
                _sources.GetAll(),
                _contentItems.GetAll(),
                _summaries.GetReady(),
                _summaries.GetDailyDigest(DateOnly.FromDateTime(DateTime.Now))));
        }
        catch (Exception ex)
        {
            if (!_isClosed)
            {
                _novedadesView.SetStatus($"No se pudieron cargar los datos: {ex.Message}");
            }

            return;
        }

        if (_isClosed)
        {
            return;
        }

        if (NewsListing.HasChanges(_loadedSources, sources, _loadedItems, items, _loadedSummaries, summaries, _loadedDigest, digest))
        {
            Show(sources, items, summaries, digest);
        }

        _novedadesView.SetStatus($"Revisado {DateTime.Now:HH:mm:ss}");
    }

    private void Show(IReadOnlyList<Source> sources, IReadOnlyList<ContentItem> items, IReadOnlyList<ContentSummary> summaries, DailyDigest? digest)
    {
        NotifyNewVideos(NewContentDetector.FindVideosToNotify(_loadedItems, items, sources));

        _loadedSources = sources;
        _loadedItems = items;
        _loadedSummaries = summaries;
        _loadedDigest = digest;

        _allNews = NewsListing.BuildRows(items, sources, summaries, _geminiConfigured);
        _allFilter = SourceFilterInfo.All(_allNews);
        NovedadesUnreadText.Text = _allFilter.UnreadText;

        var filters = new List<SourceFilterInfo>();
        foreach (var group in SourceGrouping.GroupByType(sources))
        {
            var groupName = GroupLabel(group.SourceType);
            filters.AddRange(group.Sources.Select(s => SourceFilterInfo.For(s, _allNews, groupName)));
        }
        _sourceFilters = filters;

        SourcesList.ItemsSource = filters;
        // ItemsSource is a new list every reload, so its default view needs the grouping re-applied each time.
        CollectionViewSource.GetDefaultView(filters).GroupDescriptions.Add(new PropertyGroupDescription(nameof(SourceFilterInfo.GroupName)));

        SyncSelectionVisuals();
        _novedadesView.Show(_allNews, CurrentFilter(), digest, resetToFirstPage: false);
    }

    private SourceFilterInfo CurrentFilter() =>
        _sourceFilters.FirstOrDefault(f => f.SourceId == _selectedSourceId) ?? _allFilter ?? SourceFilterInfo.Empty;

    /// <summary>Makes the sidebar's selected entry (Novedades or a source) match <see cref="_selectedSourceId"/>.</summary>
    private void SyncSelectionVisuals()
    {
        var matching = _sourceFilters.FirstOrDefault(f => f.SourceId == _selectedSourceId);
        SourcesList.SelectionChanged -= SourcesList_SelectionChanged;
        SourcesList.SelectedItem = matching;
        SourcesList.SelectionChanged += SourcesList_SelectionChanged;
        NovedadesToggle.IsChecked = matching is null;
    }

    /// <summary>Spanish sidebar label for a source group. Presentation-only; the grouping itself is Core logic.</summary>
    private static string GroupLabel(SourceType sourceType) => sourceType switch
    {
        SourceType.GitHub => "Repositorios",
        SourceType.YouTube => "Canales",
        _ => sourceType.ToString(),
    };

    private void NotifyNewVideos(IReadOnlyList<(ContentItem Item, Source Source)> videos)
    {
        try
        {
            VideoNotifier.NotifyNewVideos(videos.Select(v => (v.Source.Name, v.Item.Title, v.Item.Url)).ToList());
        }
        catch (Exception ex)
        {
            // Notifications are a convenience: the videos are still listed as unread.
            _novedadesView.SetStatus($"No se pudo mostrar la notificación: {ex.Message}");
        }
    }

    /// <summary>Selects Novedades (null) or a source, resetting to page 1 only when the selection actually changes.</summary>
    private void Navigate(long? sourceId)
    {
        var changed = sourceId != _selectedSourceId;
        _selectedSourceId = sourceId;

        SyncSelectionVisuals();
        _novedadesView.Show(_allNews, CurrentFilter(), _loadedDigest, resetToFirstPage: changed);
    }

    private void Novedades_Click(object sender, RoutedEventArgs e)
    {
        GoToNovedades();
        Navigate(null);
    }

    private void SourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourcesList.SelectedItem is SourceFilterInfo filter)
        {
            GoToNovedades();
            Navigate(filter.SourceId);
        }
    }

    private void GoToNovedades() => MainContent.Content = _novedadesView;

    private async void ShowDetails(NewsItemView item)
    {
        var detailView = _detailView ??= new ReleaseDetailView(_contentItems, _detailGenerator, _geminiConfigured, GoToNovedades);
        MainContent.Content = detailView;
        await detailView.ShowAsync(item);
    }

    private async void ManageSources_Click(object sender, RoutedEventArgs e)
    {
        new SourcesWindow(_database) { Owner = this }.ShowDialog();
        await ReloadAsync();
    }

    private void Learn_Click(object sender, RoutedEventArgs e) =>
        new LearningWindow(_database) { Owner = this }.ShowDialog();
}
