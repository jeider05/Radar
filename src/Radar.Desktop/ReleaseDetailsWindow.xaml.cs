using System.Globalization;
using System.Windows;
using Radar.Core.Contents;
using Radar.Core.Sources;
using Radar.Infrastructure.Contents;
using Radar.Infrastructure.Gemini;

namespace Radar.Desktop;

/// <summary>
/// Original release notes plus a Spanish AI explanation. A stored explanation is shown as is;
/// Gemini is only called when none exists yet.
/// </summary>
public partial class ReleaseDetailsWindow : Window
{
    private readonly ContentItemStore _contentItems;
    private readonly ContentItem _item;
    private readonly string _sourceName;
    private readonly SourceType _sourceType;
    private bool _isClosed;

    public ReleaseDetailsWindow(ContentItemStore contentItems, ContentItem item, string sourceName, SourceType sourceType)
    {
        InitializeComponent();
        _contentItems = contentItems;
        _item = item;
        _sourceName = sourceName;
        _sourceType = sourceType;

        if (sourceType == SourceType.YouTube)
        {
            Title = "Video details";
            NotesLabel.Text = "Video description";
        }

        TitleText.Text = item.Title;
        SubtitleText.Text = item.PublishedAtUtc is { } published
            ? $"{sourceName} · {published.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}"
            : sourceName;
        NotesText.Text = string.IsNullOrWhiteSpace(item.Description)
            ? sourceType == SourceType.YouTube ? "This video has no description." : "This release has no notes."
            : item.Description;

        Loaded += async (_, _) => await LoadExplanationAsync();
        Closed += (_, _) => _isClosed = true;
    }

    private async Task LoadExplanationAsync()
    {
        GenerateButton.IsEnabled = false;
        try
        {
            var explanation = await Task.Run(() => _contentItems.GetExplanation(_item.Id));
            if (_isClosed)
            {
                return;
            }

            if (explanation is not null)
            {
                ShowExplanation(explanation);
                return;
            }
        }
        catch (Exception ex)
        {
            ExplanationStatus.Text = $"Could not load the saved explanation: {ex.Message}";
            return;
        }

        if (_sourceType == SourceType.YouTube)
        {
            // Video explanations will be based on transcripts, not on the description, to avoid guessing.
            ExplanationStatus.Text = "AI explanations for videos will use the video transcript and arrive in a later phase.";
            GenerateButton.Visibility = Visibility.Collapsed;
            return;
        }

        if (string.IsNullOrWhiteSpace(_item.Description))
        {
            ExplanationStatus.Text = "There are no release notes to explain.";
            return;
        }

        ExplanationStatus.Text = "No explanation yet. It is generated once and saved for next time.";
        GenerateButton.IsEnabled = true;
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        using var httpClient = GeminiClient.CreateHttpClient();
        var gemini = GeminiClient.FromEnvironment(httpClient);
        if (gemini is null)
        {
            ExplanationStatus.Text =
                $"Gemini is not configured. Set the {GeminiClient.ApiKeyVariable} environment variable for your Windows user and restart Radar.";
            return;
        }

        GenerateButton.IsEnabled = false;
        ExplanationStatus.Text = "Generating explanation…";
        try
        {
            var text = await gemini.ExplainReleaseAsync(_sourceName, _item);
            var explanation = await Task.Run(() => _contentItems.SaveExplanation(_item.Id, text, gemini.Model));
            if (!_isClosed)
            {
                ShowExplanation(explanation);
            }
        }
        catch (Exception ex)
        {
            // async void handler: never let a failure close the app.
            if (_isClosed)
            {
                return;
            }

            ExplanationStatus.Text = ex is TaskCanceledException ? "Gemini did not respond in time. Try again." : ex.Message;
            GenerateButton.IsEnabled = true;
        }
    }

    private void ShowExplanation(ContentExplanation explanation)
    {
        ExplanationText.Text = explanation.Text;
        ExplanationStatus.Text =
            $"Generated with {explanation.Model} on {explanation.CreatedAtUtc.ToLocalTime().ToString("MMM d, yyyy HH:mm", CultureInfo.InvariantCulture)}. AI can make mistakes; check the original notes.";
        GenerateButton.Visibility = Visibility.Collapsed;
    }
}
