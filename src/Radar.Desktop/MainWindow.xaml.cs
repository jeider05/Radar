using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Radar.Core.Contents;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;

namespace Radar.Desktop;

/// <summary>
/// Main screen: registered sources on the left, detected content on the right.
/// Reads the same database as the Worker through RadarDatabase.CreateDefault().
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly RadarDatabase _database;
    private readonly SourceStore _sources;
    private readonly ContentItemStore _contentItems;
    private readonly DispatcherTimer _autoRefreshTimer;

    // Last data shown, used to skip redrawing when nothing changed.
    private IReadOnlyList<Source>? _loadedSources;
    private IReadOnlyList<ContentItem>? _loadedItems;
    private IReadOnlyList<ContentItemRow> _allRows = [];

    private const int PageSize = 10;
    private ContentPeriod _period = ContentPeriod.All;
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
                StatusText.Text = $"Could not open the database: {ex.Message}";
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
    /// Reads sources and content off the UI thread and redraws only when the data changed, keeping the selected source.
    /// Requests made while a reload is running are queued, so reloads never overlap. Never writes to the database.
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
        try
        {
            (sources, items) = await Task.Run(() => (_sources.GetAll(), _contentItems.GetAll()));
        }
        catch (Exception ex)
        {
            if (!_isClosed)
            {
                StatusText.Text = $"Could not load data: {ex.Message}";
            }

            return;
        }

        if (_isClosed)
        {
            return;
        }

        var changed = _loadedSources is null
            || !sources.SequenceEqual(_loadedSources)
            || !items.SequenceEqual(_loadedItems!);
        if (changed)
        {
            Show(sources, items);
        }

        StatusText.Text = $"Checked {DateTime.Now:HH:mm:ss}";
    }

    private void Show(IReadOnlyList<Source> sources, IReadOnlyList<ContentItem> items)
    {
        NotifyNewVideos(NewContentDetector.FindVideosToNotify(_loadedItems, items, sources));

        _loadedSources = sources;
        _loadedItems = items;

        var selectedSourceId = (SourcesList.SelectedItem as SourceFilter)?.SourceId;

        var sourcesById = sources.ToDictionary(s => s.Id);
        _allRows = items
            .Select(item => sourcesById.TryGetValue(item.SourceId, out var source)
                ? new ContentItemRow(item, source.DisplayName, source.SourceType)
                : new ContentItemRow(item, "Unknown source", SourceType.GitHub))
            .ToList();

        var filters = new List<SourceFilter> { SourceFilter.All(_allRows) };
        filters.AddRange(sources.Select(s => SourceFilter.For(s, _allRows)));

        SourcesList.ItemsSource = filters;
        SourcesList.SelectedItem = filters.FirstOrDefault(f => f.SourceId == selectedSourceId) ?? filters[0];
    }

    /// <summary>Shows the current page of the selected source's items in the selected period.</summary>
    private void NotifyNewVideos(IReadOnlyList<(ContentItem Item, Source Source)> videos)
    {
        try
        {
            VideoNotifier.NotifyNewVideos(videos.Select(v => (v.Source.Name, v.Item.Title, v.Item.Url)).ToList());
        }
        catch (Exception ex)
        {
            // Notifications are a convenience: the videos are still listed as unread.
            StatusText.Text = $"Could not show a notification: {ex.Message}";
        }
    }

    private void ShowItems(SourceFilter filter)
    {
        var nowUtc = DateTime.UtcNow;
        var rows = _allRows
            .Where(r => filter.SourceId is not { } sourceId || r.Item.SourceId == sourceId)
            .Where(r => ContentListing.IsInPeriod(r.Item, _period, nowUtc, TimeZoneInfo.Local))
            .ToList();

        var page = ContentListing.GetPage(rows, _pageNumber, PageSize);
        _pageNumber = page.PageNumber;

        ItemsList.ItemsSource = page.Items;
        ListTitle.Text = filter.Name;
        ListSummary.Text = $"{rows.Count} items · {rows.Count(r => !r.IsRead)} unread";

        EmptyText.Text = _period == ContentPeriod.All
            ? "No content yet. The Worker adds new releases as it finds them."
            : "No releases in this period.";
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Pager.Visibility = page.PageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        PageText.Text = $"Page {page.PageNumber} of {page.PageCount}";
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

    private async void MarkAsRead_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ContentItemRow row)
        {
            return;
        }

        try
        {
            await Task.Run(() => _contentItems.MarkAsRead(row.Item.Id));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not mark as read: {ex.Message}";
            return;
        }

        await ReloadAsync();
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ContentItemRow row)
        {
            new ReleaseDetailsWindow(_contentItems, row.Item, row.SourceName, row.SourceType) { Owner = this }.ShowDialog();
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ContentItemRow row)
        {
            return;
        }

        // Only web links are opened; anything else in the database is never passed to the shell.
        if (!Uri.TryCreate(row.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            StatusText.Text = "This item does not have a valid web link.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open the browser: {ex.Message}";
        }
    }

    /// <summary>One content item as displayed in the list.</summary>
    private sealed record ContentItemRow(ContentItem Item, string SourceName, SourceType SourceType)
    {
        public string Title => Item.Title;

        public string Url => Item.Url;

        public bool IsRead => Item.IsRead;

        public string DateText => Item.PublishedAtUtc is { } published
            ? FormatDate(published)
            : $"Detected {FormatDate(Item.DetectedAtUtc)}";

        private static string FormatDate(DateTime utc) =>
            utc.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>An entry of the sources sidebar. SourceId is null for "All sources".</summary>
    private sealed record SourceFilter(long? SourceId, string Name, string Detail, int Unread)
    {
        public string UnreadText => Unread > 0 ? Unread.ToString(CultureInfo.InvariantCulture) : "";

        public static SourceFilter All(IReadOnlyList<ContentItemRow> rows) =>
            new(null, "All sources", $"{rows.Count} items", rows.Count(r => !r.IsRead));

        public static SourceFilter For(Source source, IReadOnlyList<ContentItemRow> rows)
        {
            var sourceRows = rows.Where(r => r.Item.SourceId == source.Id).ToList();
            var detail = source.IsActive ? source.SourceType.ToString() : $"{source.SourceType} · paused";
            return new(source.Id, source.DisplayName, detail, sourceRows.Count(r => !r.IsRead));
        }
    }
}
