using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.GitHub;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;
using Radar.Infrastructure.YouTube;

namespace Radar.Desktop;

/// <summary>
/// "My sources": add GitHub repositories or YouTube channels, pause or resume them, and remove sources that have no content.
/// GitHub sources are only written to SQLite. YouTube links are looked up once on YouTube to get the channel id and name.
/// The Worker checks new and resumed sources and fetches their releases or videos.
/// </summary>
public partial class SourcesWindow : Window
{
    private readonly SourceStore _sources;
    private readonly ContentItemStore _contentItems;

    public SourcesWindow(RadarDatabase database)
    {
        InitializeComponent();
        _sources = new SourceStore(database);
        _contentItems = new ContentItemStore(database);

        Loaded += async (_, _) =>
        {
            RepositoryInput.Focus();
            await ReloadAsync();
        };
    }

    private async Task ReloadAsync()
    {
        try
        {
            var (sources, items) = await Task.Run(() => (_sources.GetAll(), _contentItems.GetAll()));
            var itemCounts = items.GroupBy(i => i.SourceId).ToDictionary(g => g.Key, g => g.Count());
            var rows = sources.Select(s => new SourceRow(s, itemCounts.GetValueOrDefault(s.Id))).ToList();

            SourcesList.ItemsSource = rows;
            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not load sources: {ex.Message}", isError: true);
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await AddAsync();

    private async void RepositoryInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AddAsync();
        }
    }

    private void SourceType_Checked(object sender, RoutedEventArgs e)
    {
        // Raised while the window is being built, before the other controls exist.
        if (InputHint is null)
        {
            return;
        }

        InputHint.Text = YouTubeTypeButton.IsChecked == true ? "YouTube channel link, e.g. https://www.youtube.com/@channel" : "owner/repository or GitHub URL";
        AddMessage.Visibility = Visibility.Collapsed;
        RepositoryInput.Focus();
    }

    private async Task AddAsync()
    {
        if (YouTubeTypeButton.IsChecked == true)
        {
            await AddYouTubeChannelAsync();
            return;
        }

        if (!GitHubRepository.TryParse(RepositoryInput.Text, out var repository, out var error))
        {
            ShowMessage(error, isError: true);
            return;
        }

        try
        {
            var added = await Task.Run(() =>
                _sources.TryAdd(SourceType.GitHub, repository.ExternalId, repository.DisplayName, repository.Url));

            if (added is null)
            {
                var existing = await Task.Run(() => _sources.Find(SourceType.GitHub, repository.ExternalId));
                ShowMessage(
                    existing is { IsActive: false }
                        ? $"{existing.Name} is already in your sources but paused. Use Resume to follow it again."
                        : $"{existing?.Name ?? repository.DisplayName} is already in your sources.",
                    isError: true);
                return;
            }

            RepositoryInput.Clear();
            ShowMessage($"Added {added.Name}. The Worker will check it and fetch its releases shortly.", isError: false);
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not add the repository: {ex.Message}", isError: true);
            return;
        }

        await ReloadAsync();
    }

    private async Task AddYouTubeChannelAsync()
    {
        if (!YouTubeChannelLink.TryParse(RepositoryInput.Text, out var channelId, out var pageUrl, out var error))
        {
            ShowMessage(error, isError: true);
            return;
        }

        AddButton.IsEnabled = false;
        ShowMessage("Looking up the channel on YouTube…", isError: false);
        try
        {
            using var httpClient = YouTubeClient.CreateHttpClient();
            var youTube = new YouTubeClient(httpClient);
            channelId ??= await youTube.ResolveChannelIdAsync(pageUrl!);

            var existing = await Task.Run(() => _sources.Find(SourceType.YouTube, channelId));
            if (existing is not null)
            {
                ShowMessage(
                    existing.IsActive
                        ? $"{existing.Name} is already in your sources."
                        : $"{existing.Name} is already in your sources but paused. Use Resume to follow it again.",
                    isError: true);
                return;
            }

            // Reading the feed confirms the channel exists and gives its name.
            var feed = await youTube.GetFeedAsync(channelId);
            var added = await Task.Run(() =>
                _sources.TryAdd(SourceType.YouTube, channelId, feed.ChannelTitle, YouTubeChannelLink.ChannelUrl(channelId)));
            if (added is null)
            {
                ShowMessage($"{feed.ChannelTitle} is already in your sources.", isError: true);
                return;
            }

            RepositoryInput.Clear();
            ShowMessage($"Added {added.Name}. The Worker will fetch its latest videos shortly, and new uploads will be notified.", isError: false);
        }
        catch (YouTubeApiException ex)
        {
            ShowMessage(ex.Message, isError: true);
            return;
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not add the channel: {ex.Message}", isError: true);
            return;
        }
        finally
        {
            AddButton.IsEnabled = true;
        }

        await ReloadAsync();
    }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SourceRow row)
        {
            return;
        }

        try
        {
            var resume = !row.Source.IsActive;
            await Task.Run(() => _sources.SetActive(row.Source.Id, resume));
            ShowMessage(resume ? $"Resumed {row.Name}." : $"Paused {row.Name}. Its content stays in Radar.", isError: false);
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not update {row.Name}: {ex.Message}", isError: true);
        }

        await ReloadAsync();
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SourceRow row)
        {
            return;
        }

        var confirm = MessageBox.Show(this, $"Remove {row.Name} from your sources?", "Remove source",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            var result = await Task.Run(() => _sources.TryDelete(row.Source.Id));
            switch (result)
            {
                case SourceDeleteResult.Deleted:
                    ShowMessage($"Removed {row.Name}.", isError: false);
                    break;
                case SourceDeleteResult.HasContent:
                    // The Worker found content after this list was loaded.
                    ShowMessage($"{row.Name} now has content, so it was kept. Pause it instead.", isError: true);
                    break;
                case SourceDeleteResult.NotFound:
                    ShowMessage($"{row.Name} was already removed.", isError: false);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not remove {row.Name}: {ex.Message}", isError: true);
        }

        await ReloadAsync();
    }

    private void ShowMessage(string message, bool isError)
    {
        AddMessage.Text = message;
        AddMessage.Foreground = (System.Windows.Media.Brush)FindResource(isError ? "ErrorBrush" : "MutedTextBrush");
        AddMessage.Visibility = Visibility.Visible;
    }

    /// <summary>One registered source as displayed in the list.</summary>
    private sealed record SourceRow(Source Source, int ItemCount)
    {
        public string Name => Source.Name;

        public string Detail =>
            $"{Source.SourceType} · {(Source.IsActive ? "Active" : "Paused")} · {ItemCount.ToString(CultureInfo.InvariantCulture)} items";

        public string ToggleText => Source.IsActive ? "Pause" : "Resume";

        public bool CanRemove => ItemCount == 0;

        public string RemoveToolTip => CanRemove
            ? "Remove this source."
            : "This source has content. Pause it instead to keep its history.";
    }
}
