using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Radar.Core.Contents;
using Radar.Infrastructure.Contents;

namespace Radar.Desktop;

/// <summary>
/// The Novedades list: header, daily digest line, period/type filters, items and pagination.
/// Owns its own period/type/page state; <see cref="MainWindow"/> owns data loading and the sidebar.
/// </summary>
public partial class NovedadesView : UserControl
{
    private const int PageSize = 10;

    private readonly ContentItemStore _contentItems;
    private readonly Func<Task> _reloadAsync;
    private readonly Action<NewsItemView> _showDetails;

    private IReadOnlyList<NewsItem> _allNews = [];
    private SourceFilterInfo _filter = SourceFilterInfo.Empty;
    private DailyDigest? _digest;

    private ContentPeriod _period = ContentPeriod.All;
    private ContentTypeFilter _typeFilter = ContentTypeFilter.All;
    private int _pageNumber = 1;

    // The "Todo" RadioButton has IsChecked="True" in XAML, which fires Checked synchronously
    // while InitializeComponent() is still parsing the tree below it (ItemsList, Pager, etc. do
    // not exist yet). This guard makes that premature event a no-op, same as the real one once
    // the control is actually ready to render.
    private bool _isReady;

    public NovedadesView(ContentItemStore contentItems, Func<Task> reloadAsync, Action<NewsItemView> showDetails)
    {
        InitializeComponent();
        _contentItems = contentItems;
        _reloadAsync = reloadAsync;
        _showDetails = showDetails;
        _isReady = true;
        UpdateThemeButton();
    }

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>
    /// Updates the data shown. <paramref name="resetToFirstPage"/> is true only when the selected source
    /// actually changed (a plain data reload keeps the current page).
    /// </summary>
    public void Show(IReadOnlyList<NewsItem> allNews, SourceFilterInfo filter, DailyDigest? digest, bool resetToFirstPage)
    {
        _allNews = allNews;
        _filter = filter;
        _digest = digest;
        if (resetToFirstPage)
        {
            _pageNumber = 1;
        }

        RenderCurrentView();
    }

    public void UpdateThemeButton() =>
        ThemeButton.Content = ThemeManager.Current == AppTheme.Dark ? "Light mode" : "Dark mode";

    private void RenderCurrentView()
    {
        if (!_isReady)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var rows = _allNews
            .Where(r => _filter.SourceId is not { } sourceId || r.SourceId == sourceId)
            .Where(r => ContentListing.IsInPeriod(r.Item, _period, nowUtc, TimeZoneInfo.Local))
            .ToList();
        rows = ContentListing.FilterByType(rows, _typeFilter).ToList();

        var page = ContentListing.GetPage(rows, _pageNumber, PageSize);
        _pageNumber = page.PageNumber;

        ItemsList.ItemsSource = page.Items.Select(n => new NewsItemView(n)).ToList();

        HeaderTitleText.Text = _filter.Name;
        HeaderUnreadText.Text = $"{rows.Count(r => !r.IsRead)} sin leer";

        var showDigest = _filter.IsAllFilter && _digest is not null;
        DigestPanel.Visibility = showDigest ? Visibility.Visible : Visibility.Collapsed;
        if (showDigest)
        {
            DigestText.Text = _digest!.Frase;
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

    private void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        _period = Enum.Parse<ContentPeriod>(tag);
        _pageNumber = 1;
        RenderCurrentView();
    }

    private void TypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeFilterCombo.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        _typeFilter = Enum.Parse<ContentTypeFilter>(tag);
        _pageNumber = 1;
        RenderCurrentView();
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        _pageNumber--;
        RenderCurrentView();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _pageNumber++;
        RenderCurrentView();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _reloadAsync();

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        UpdateThemeButton();
    }

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
            SetStatus($"No se pudo marcar como leído: {ex.Message}");
            return;
        }

        await _reloadAsync();
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NewsItemView row)
        {
            _showDetails(row);
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
            SetStatus("Esta novedad no tiene un enlace web válido.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir el navegador: {ex.Message}");
        }
    }
}
