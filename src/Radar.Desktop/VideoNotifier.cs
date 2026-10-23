using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Radar.Desktop;

/// <summary>
/// Shows Windows notifications (toasts) for new YouTube videos. Clicking a notification opens the video in the browser.
/// </summary>
public static class VideoNotifier
{
    // Unpackaged apps need an AppUserModelID registered for the current user to show toasts.
    private const string AppId = "Radar.Desktop";

    // More new videos than this at once produce a single summary notification instead.
    private const int MaxIndividualNotifications = 3;

    public static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
        key.SetValue("DisplayName", "Radar");
    }

    public static void NotifyNewVideos(IReadOnlyList<(string Channel, string Title, string Url)> videos)
    {
        if (videos.Count == 0)
        {
            return;
        }

        if (videos.Count > MaxIndividualNotifications)
        {
            var channels = string.Join(", ", videos.Select(v => v.Channel).Distinct());
            Show($"{videos.Count} new videos", channels, launchUrl: null);
            return;
        }

        foreach (var (channel, title, url) in videos)
        {
            Show($"New video · {channel}", title, url);
        }
    }

    private static void Show(string heading, string body, string? launchUrl)
    {
        // Protocol activation opens the link without needing a COM activator.
        var launch = launchUrl is not null && Uri.TryCreate(launchUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? $" activationType=\"protocol\" launch=\"{SecurityElement.Escape(uri.AbsoluteUri)}\""
            : "";

        var xml = new XmlDocument();
        xml.LoadXml($"""
            <toast{launch}>
              <visual>
                <binding template="ToastGeneric">
                  <text>{SecurityElement.Escape(heading)}</text>
                  <text>{SecurityElement.Escape(body)}</text>
                </binding>
              </visual>
            </toast>
            """);

        ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
    }
}
