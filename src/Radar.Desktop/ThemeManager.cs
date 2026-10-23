using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Radar.Infrastructure.Persistence;

namespace Radar.Desktop;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// Switches the color palette (Themes/Light.xaml or Themes/Dark.xaml) for every window and remembers the choice.
/// The first time, it follows the Windows app theme.
/// </summary>
public static class ThemeManager
{
    // Next to radar.db, outside the repository.
    private static readonly string SettingsPath =
        Path.Combine(Path.GetDirectoryName(RadarDatabase.DefaultPath)!, "desktop-theme.txt");

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>Applies the saved theme, or the Windows theme when none was saved. Call once at startup.</summary>
    public static void Initialize()
    {
        // Dark title bars for every window, including ones opened later.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ApplyTitleBar((Window)sender)));

        Apply(LoadSavedTheme() ?? GetWindowsTheme(), save: false);
    }

    public static void Toggle() => Apply(Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light, save: true);

    private static void Apply(AppTheme theme, bool save)
    {
        Current = theme;

        var palettes = Application.Current.Resources.MergedDictionaries;
        palettes.Clear();
        palettes.Add(new ResourceDictionary { Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative) });

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }

        if (save)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, theme.ToString());
            }
            catch (IOException)
            {
                // The theme still applies for this session; it is just not remembered.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static AppTheme? LoadSavedTheme()
    {
        try
        {
            return File.Exists(SettingsPath) && Enum.TryParse<AppTheme>(File.ReadAllText(SettingsPath).Trim(), out var theme)
                ? theme
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static AppTheme GetWindowsTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is 0 ? AppTheme.Dark : AppTheme.Light;
    }

    private static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+ / Windows 11). Ignored on older systems.
        var useDarkMode = Current == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref useDarkMode, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
