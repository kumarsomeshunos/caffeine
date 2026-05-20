using System;
using Microsoft.Win32;
using System.Windows;

namespace CaffeineWin;

public enum AppTheme { System, Light, Dark }

public static class ThemeManager
{
    private const string SettingsPath = @"Software\CaffeineWin";
    private const string ThemeKey = "Theme";
    private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static AppTheme CurrentSetting { get; private set; } = AppTheme.System;
    public static bool IsDark { get; private set; }

    public static event Action? ThemeChanged;

    public static void Initialize()
    {
        LoadPreference();
        ApplyTheme(CurrentSetting);
        SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
    }

    public static void Shutdown()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
    }

    public static void ApplyTheme(AppTheme theme)
    {
        CurrentSetting = theme;
        IsDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsWindowsDarkMode()
        };

        var dictUri = IsDark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        var dict = new ResourceDictionary { Source = dictUri };
        var mergedDicts = Application.Current.Resources.MergedDictionaries;

        if (mergedDicts.Count > 0)
            mergedDicts[0] = dict;
        else
            mergedDicts.Add(dict);

        ThemeChanged?.Invoke();
    }

    public static void SavePreference(AppTheme theme)
    {
        CurrentSetting = theme;
        using var key = Registry.CurrentUser.CreateSubKey(SettingsPath);
        key.SetValue(ThemeKey, theme.ToString());
    }

    private static void LoadPreference()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsPath, false);
        var value = key?.GetValue(ThemeKey) as string;
        if (Enum.TryParse<AppTheme>(value, out var theme))
            CurrentSetting = theme;
    }

    private static bool IsWindowsDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizePath, false);
        var val = key?.GetValue("AppsUseLightTheme");
        return val is int i && i == 0;
    }

    private static void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (CurrentSetting != AppTheme.System) return;

        Application.Current.Dispatcher.BeginInvoke(() => ApplyTheme(AppTheme.System));
    }
}
