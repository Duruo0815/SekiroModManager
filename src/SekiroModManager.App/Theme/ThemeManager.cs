using System.IO;
using System.Text.Json;
using System.Windows;

namespace SekiroModManager.App.Theme;

public enum AppTheme
{
    Dark,
    Light
}

public static class ThemeManager
{
    private static readonly string SettingsFile = Path.Combine(AppContext.BaseDirectory, "theme.json");
    private static AppTheme _currentTheme = AppTheme.Dark;

    public static AppTheme CurrentTheme => _currentTheme;
    public static bool IsDark => _currentTheme == AppTheme.Dark;

    public static event Action<AppTheme>? ThemeChanged;

    public static void Initialize()
    {
        EnsureResources();
        var theme = AppTheme.Dark;
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Theme", out var prop))
                {
                    if (Enum.TryParse<AppTheme>(prop.GetString(), true, out var parsed))
                        theme = parsed;
                }
            }
        }
        catch
        {
            theme = AppTheme.Dark;
        }

        SetTheme(theme);
    }

    public static void EnsureResources()
    {
        var app = Application.Current;
        if (app != null)
        {
            var stylesUri = new Uri("/SekiroModManager.App;component/Theme/Styles.xaml", UriKind.Relative);
            if (!app.Resources.MergedDictionaries.Any(d => d.Source != null && d.Source.OriginalString.Contains("Styles.xaml")))
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = stylesUri });
            }
        }
    }

    public static void ToggleTheme()
    {
        SetTheme(_currentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
    }

    public static void SetTheme(AppTheme theme)
    {
        _currentTheme = theme;
        var uri = theme == AppTheme.Dark
            ? new Uri("/SekiroModManager.App;component/Theme/DarkTheme.xaml", UriKind.Relative)
            : new Uri("/SekiroModManager.App;component/Theme/LightTheme.xaml", UriKind.Relative);

        var app = Application.Current;
        if (app != null)
        {
            var dicts = app.Resources.MergedDictionaries;
            var oldTheme = dicts.FirstOrDefault(d => d.Source != null && (d.Source.OriginalString.Contains("DarkTheme.xaml") || d.Source.OriginalString.Contains("LightTheme.xaml")));

            var newDict = new ResourceDictionary { Source = uri };
            if (oldTheme != null)
            {
                var index = dicts.IndexOf(oldTheme);
                dicts[index] = newDict;
            }
            else
            {
                dicts.Insert(0, newDict);
            }

            try
            {
                foreach (Window win in app.Windows)
                {
                    WindowTitleBarHelper.ApplyThemeToWindow(win, theme == AppTheme.Dark);
                }
            }
            catch { }
        }

        try
        {
            var json = JsonSerializer.Serialize(new { Theme = theme.ToString() });
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // ignore save error
        }

        ThemeChanged?.Invoke(_currentTheme);
    }
}
