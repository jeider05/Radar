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

    private readonly SourceStore _sources;
    private readonly ContentItemStore _contentItems;
    private readonly DispatcherTimer _autoRefreshTimer;

    // Last data shown, used to skip redrawing when nothing changed.
    private IReadOnlyList<Source>? _loadedSources;
    private IReadOnlyList<ContentItem>? _loadedItems;
    private IReadOnlyList<ContentItemRow> _allRows = [];

    private bool _isLoading;
    private bool _reloadPending;
    private bool _isClosed;

    public MainWindow()
    {
        InitializeComponent();

        var database = RadarDatabase.CreateDefault();
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
        _loadedSources = sources;
        _loadedItems = items;

        var selectedSourceId = (SourcesList.SelectedItem as SourceFilter)?.SourceId;

        var sourceNames = sources.ToDictionary(s => s.Id, s => s.Name);
        _allRows = items
            .Select(item => new ContentItemRow(item, sourceNames.GetValueOrDefault(item.SourceId, "Unknown source")))
            .ToList();

        var filters = new List<SourceFilter> { SourceFilter.All(_allRows) };
        filters.AddRange(sources.Select(s => SourceFilter.For(s, _allRows)));

        SourcesList.ItemsSource = filters;
        SourcesList.SelectedItem = filters.FirstOrDefault(f => f.SourceId == selectedSourceId) ?? filters[0];
    }

    private void ShowItems(SourceFilter filter)
    {
        var rows = filter.SourceId is { } sourceId
            ? _allRows.Where(r => r.Item.SourceId == sourceId).ToList()
            : _allRows;

        ItemsList.ItemsSource = rows;
        ListTitle.Text = filter.Name;
        var unread = rows.Count(r => !r.IsRead);
        ListSummary.Text = $"{rows.Count} items · {unread} unread";
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourcesList.SelectedItem is SourceFilter filter)
        {
            ShowItems(filter);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

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
    private sealed record ContentItemRow(ContentItem Item, string SourceName)
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
            return new(source.Id, source.Name, detail, sourceRows.Count(r => !r.IsRead));
        }
    }
}
