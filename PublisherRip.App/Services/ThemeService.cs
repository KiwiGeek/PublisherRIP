using System.Windows;
using Microsoft.Win32;

namespace PublisherRip.App.Services;

/// <summary>
/// Follows the Windows "app mode" (light/dark) setting, live. Switches both WPF's Fluent theme
/// (standard controls, title bar) and the app's own palette (Themes/Light.xaml, Themes/Dark.xaml).
/// </summary>
public static class ThemeService
{
    /// <summary>For testing: "light" or "dark" forces a theme regardless of the Windows setting.</summary>
    private const string OverrideEnvVar = "PUBLISHERRIP_THEME";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightPalette = "Themes/Light.xaml";
    private const string DarkPalette = "Themes/Dark.xaml";

    private static bool? _isDarkApplied;

    public static void Initialize(Application app)
    {
        Apply(app);

        // Fires when the user changes app mode in Settings (category General).
        UserPreferenceChangedEventHandler handler = (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                app.Dispatcher.BeginInvoke(() => Apply(app));
            }
        };
        SystemEvents.UserPreferenceChanged += handler;
        app.Exit += (_, _) => SystemEvents.UserPreferenceChanged -= handler;
    }

    private static void Apply(Application app)
    {
        var isDark = ShouldUseDark();
        if (_isDarkApplied == isDark)
        {
            return;
        }

        // ThemeMode (WPF's Fluent theme) is still marked experimental in .NET 10. The app ships
        // self-contained, so the runtime it runs on is fixed at build time; if the API changes in a
        // future .NET, this fails to compile rather than misbehaving at runtime.
#pragma warning disable WPF0001
        app.ThemeMode = isDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        // Replace the palette, and keep it last so it wins over anything the Fluent theme merges in.
        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(IsPalette);
        if (existing is not null)
        {
            dictionaries.Remove(existing);
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(isDark ? DarkPalette : LightPalette, UriKind.Relative),
        });

        _isDarkApplied = isDark;
    }

    private static bool IsPalette(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source is not null
            && (source.EndsWith(LightPalette, StringComparison.OrdinalIgnoreCase)
                || source.EndsWith(DarkPalette, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldUseDark()
    {
        switch (Environment.GetEnvironmentVariable(OverrideEnvVar)?.Trim().ToLowerInvariant())
        {
            case "dark":
                return true;
            case "light":
                return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}
