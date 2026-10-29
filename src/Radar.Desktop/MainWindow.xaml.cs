using System.Diagnostics;
using System.Globalization;
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
using Radar.Infrastructure.YouTube;

namespace Radar.Desktop;

/// <summary>
/// Main screen: registered sources on the left, detected content (with its AI summary) on the right.
/// Reads the same database as the Worker through RadarDatabase.CreateDefault(). Never calls Gemini itself.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _contentItems;
    private readonly ContentSummaryStore _summaries;
    private readonly DispatcherTimer _autoRefreshTimer;

    // Whether this Desktop process has GEMINI_API_KEY configured, checked once at startup. Used only to tell
    // apart "summary pending" from "summaries are off" for items that have no ContentSummaries row yet.
    private readonly bool _geminiConfigured;

    // Last data shown, used to skip redrawing when nothing changed.
    private IReadOnlyList<Source>? _loadedSources;
    private IReadOnlyList<ContentItem>? _loadedItems;
    private IReadOnlyList<ContentSummary>? _loadedSummaries;
    private DailyDigest? _loadedDigest;
    private IReadOnlyList<NewsItem> _allNews = [];

    private const int PageSize = 10;
    private ContentPeriod _period = ContentPeriod.All;
    private ContentTypeFilter _typeFilter = ContentTypeFilter.All;
    private int _pageNumber = 1;
    private long? _shownSourceId;

    private bool _isLoading;
    private bool _reloadPending;
    private bool _isClosed;

    public MainWindow()
    {
        InitializeComponent();
        UpdateThemeButton();

        var database = _database = RadarDatabase.CreateDefault();
        _sources = new SourceStore(database);
        _contentItems = new ContentItemStore(database);
        _summaries = new ContentSummaryStore(database);
        _geminiConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GeminiClient.ApiKeyVariable));

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
                StatusText.Text = $"No se pudo abrir la base de datos: {ex.Message}";
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
    /// keeping the selected source. Requests made while a reload is running are queued, so reloads never
    /// overlap. Never writes to the database and never contacts Gemini.
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
                StatusText.Text = $"No se pudieron cargar los datos: {ex.Message}";
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

        StatusText.Text = $"Revisado {DateTime.Now:HH:mm:ss}";
    }

    private void Show(IReadOnlyList<Source> sources, IReadOnlyList<ContentItem> items, IReadOnlyList<ContentSummary> summaries, DailyDigest? digest)
    {
        NotifyNewVideos(NewContentDetector.FindVideosToNotify(_loadedItems, items, sources));

        _loadedSources = sources;
        _loadedItems = items;
        _loadedSummaries = summaries;
        _loadedDigest = digest;

        _allNews = NewsListing.BuildRows(items, sources, summaries, _geminiConfigured);

        var selectedSourceId = (SourcesList.SelectedItem as SourceFilter)?.SourceId;

        var filters = new List<SourceFilter> { SourceFilter.All(_allNews) };
        foreach (var group in SourceGrouping.GroupByType(sources))
        {
            var groupName = GroupLabel(group.SourceType);
            filters.AddRange(group.Sources.Select(s => SourceFilter.For(s, _allNews, groupName)));
        }

        SourcesList.ItemsSource = filters;
        // ItemsSource is a new list every reload, so its default view needs the grouping re-applied each time.
        CollectionViewSource.GetDefaultView(filters).GroupDescriptions.Add(new PropertyGroupDescription(nameof(SourceFilter.GroupName)));
        SourcesList.SelectedItem = filters.FirstOrDefault(f => f.SourceId == selectedSourceId) ?? filters[0];
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
            StatusText.Text = $"No se pudo mostrar la notificación: {ex.Message}";
        }
    }

    /// <summary>
    /// Shows the current page of the selected source's items in the selected period and type, and refreshes
    /// the header title/unread count and the daily digest line.
    /// </summary>
    private void ShowItems(SourceFilter filter)
    {
        var nowUtc = DateTime.UtcNow;
        var rows = _allNews
            .Where(r => filter.SourceId is not { } sourceId || r.SourceId == sourceId)
            .Where(r => ContentListing.IsInPeriod(r.Item, _period, nowUtc, TimeZoneInfo.Local))
            .ToList();
        rows = ContentListing.FilterByType(rows, _typeFilter).ToList();

        var page = ContentListing.GetPage(rows, _pageNumber, PageSize);
        _pageNumber = page.PageNumber;

        ItemsList.ItemsSource = page.Items.Select(n => new NewsItemView(n)).ToList();

        HeaderTitleText.Text = filter.Name;
        HeaderUnreadText.Text = $"{rows.Count(r => !r.IsRead)} sin leer";

        var showDigest = filter.IsAllFilter && _loadedDigest is not null;
        DigestPanel.Visibility = showDigest ? Visibility.Visible : Visibility.Collapsed;
        if (showDigest)
        {
            DigestText.Text = _loadedDigest!.Frase;
        }

        EmptyText.Text = _period == ContentPeriod.All
            ? "Todavía no hay novedades. El Worker las agrega a medida que las encuentra."
            : "No hay novedades en este período.";
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Pager.Visibility = page.PageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        PageText.Text = $"Página {page.PageNumber} de {page.PageCount}";
        PreviousPageButton.IsEnabled = page.PageNumber > 1;
        NextPageButton.IsEnabled = page.PageNumber < page.PageCount;
    }

    private void ShowCurrentItems()
    {
        if (SourcesList.SelectedItem is SourceFilter filter)
        {
            ShowItems(filter);
        }
    }

    private void SourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourcesList.SelectedItem is not SourceFilter filter)
        {
            return;
        }

        // A different source starts at page 1; re-selecting the same source after a reload keeps the page.
        if (filter.SourceId != _shownSourceId)
        {
            _shownSourceId = filter.SourceId;
            _pageNumber = 1;
        }

        ShowItems(filter);
    }

    private void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        _period = Enum.Parse<ContentPeriod>(tag);
        _pageNumber = 1;
        ShowCurrentItems();
    }

    private void TypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeFilterCombo.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        _typeFilter = Enum.Parse<ContentTypeFilter>(tag);
        _pageNumber = 1;
        ShowCurrentItems();
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        _pageNumber--;
        ShowCurrentItems();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _pageNumber++;
        ShowCurrentItems();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        UpdateThemeButton();
    }

    private void UpdateThemeButton() =>
        ThemeButton.Content = ThemeManager.Current == AppTheme.Dark ? "Light mode" : "Dark mode";

    private async void ManageSources_Click(object sender, RoutedEventArgs e)
    {
        new SourcesWindow(_database) { Owner = this }.ShowDialog();
        await ReloadAsync();
    }

    private void Learn_Click(object sender, RoutedEventArgs e) =>
        new LearningWindow(_database) { Owner = this }.ShowDialog();

    private async void MarkAsRead_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NewsItemView row)
        {
            return;
        }

        try
        {
            await Task.Run(() => _contentItems.MarkAsRead(row.Item.Id));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No se pudo marcar como leído: {ex.Message}";
            return;
        }

        await ReloadAsync();
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NewsItemView row)
        {
            new ReleaseDetailsWindow(_contentItems, row.Item, row.SourceName, row.SourceType) { Owner = this }.ShowDialog();
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NewsItemView row)
        {
            return;
        }

        // Only web links are opened; anything else in the database is never passed to the shell.
        if (!Uri.TryCreate(row.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            StatusText.Text = "Esta novedad no tiene un enlace web válido.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No se pudo abrir el navegador: {ex.Message}";
        }
    }

    /// <summary>A Core <see cref="NewsItem"/> plus the thin, Infrastructure-dependent bits only the UI needs (dates, thumbnail URL).</summary>
    private sealed record NewsItemView(NewsItem News)
    {
        public ContentItem Item => News.Item;

        public string Title => News.Item.Title;

        public string Url => News.Item.Url;

        public bool IsRead => News.IsRead;

        public string SourceName => News.SourceName;

        public SourceType SourceType => News.SourceType;

        public bool IsVideo => SourceType == SourceType.YouTube;

        public string KindLabel => IsVideo ? "Video" : "Release";

        public string DateText => Item.PublishedAtUtc is { } published
            ? FormatDate(published)
            : $"Detectado {FormatDate(Item.DetectedAtUtc)}";

        public string? ThumbnailUrl => IsVideo ? string.Format(YouTubeClient.ThumbnailUrlFormat, Item.ExternalId) : null;

        public IReadOnlyList<string>? SummaryBullets => News.ReleaseSummaryBullets;

        public string? VideoSummaryText => News.VideoSummary?.DeQueTrata;

        public bool ShowReleaseBullets => News.SummaryStatus == SummaryDisplayStatus.Ready && !IsVideo && SummaryBullets is { Count: > 0 };

        public bool ShowVideoSummary => News.SummaryStatus == SummaryDisplayStatus.Ready && IsVideo && !string.IsNullOrEmpty(VideoSummaryText);

        public bool ShowPending => News.SummaryStatus == SummaryDisplayStatus.Pending;

        private static string FormatDate(DateTime utc) =>
            utc.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// An entry of the sources sidebar. <see cref="SourceId"/> is null for "Novedades" (all sources);
    /// <see cref="GroupName"/> is empty for it too, so it renders above the "Repositorios"/"Canales" groups without a header.
    /// </summary>
    private sealed record SourceFilter(long? SourceId, string Name, int Unread, string GroupName, bool IsPaused)
    {
        public bool IsAllFilter => SourceId is null;

        public string UnreadText => Unread > 0 ? Unread.ToString(CultureInfo.InvariantCulture) : "";

        public bool ShowUnreadDot => !IsAllFilter && Unread > 0;

        public string DisplayName => IsPaused ? $"{Name} · pausada" : Name;

        public static SourceFilter All(IReadOnlyList<NewsItem> rows) =>
            new(null, "Novedades", rows.Count(r => !r.IsRead), GroupName: "", IsPaused: false);

        public static SourceFilter For(Source source, IReadOnlyList<NewsItem> rows, string groupName)
        {
            var sourceRows = rows.Where(r => r.SourceId == source.Id).ToList();
            return new(source.Id, source.DisplayName, sourceRows.Count(r => !r.IsRead), groupName, IsPaused: !source.IsActive);
        }
    }
}
