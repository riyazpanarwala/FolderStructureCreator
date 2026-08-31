using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace FolderStructureCreator.Services;

public enum AppTheme
{
    Dark,
    Light,
    HighContrast,
    System
}

public static class ThemeService
{
    public static event Action<AppTheme>? ThemeChanged;

    private static AppTheme _currentTheme = AppTheme.Dark;
    public static AppTheme CurrentTheme => _currentTheme;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FolderStructureCreator",
        "settings.json"
    );

    public static void Initialize()
    {
        var savedTheme = LoadSavedTheme();
        ApplyTheme(savedTheme);
    }

    public static void ApplyTheme(AppTheme theme)
    {
        _currentTheme = theme;
        SaveTheme(theme);

        var effectiveTheme = GetEffectiveTheme(theme);
        var themeUri = effectiveTheme switch
        {
            AppTheme.Light => new Uri("pack://application:,,,/FolderStructureCreator;component/Themes/LightTheme.xaml", UriKind.Absolute),
            AppTheme.HighContrast => new Uri("pack://application:,,,/FolderStructureCreator;component/Themes/HighContrastTheme.xaml", UriKind.Absolute),
            _ => new Uri("pack://application:,,,/FolderStructureCreator;component/Themes/DarkTheme.xaml", UriKind.Absolute)
        };

        try
        {
            var appResources = Application.Current.Resources;
            var existingThemeDict = appResources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("/Themes/"));

            var newThemeDict = new ResourceDictionary { Source = themeUri };

            if (existingThemeDict != null)
            {
                appResources.MergedDictionaries.Remove(existingThemeDict);
            }

            appResources.MergedDictionaries.Add(newThemeDict);
            ThemeChanged?.Invoke(effectiveTheme);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply theme '{theme}': {ex.Message}");
        }
    }

    public static AppTheme GetEffectiveTheme(AppTheme theme)
    {
        if (theme != AppTheme.System)
            return theme;

        try
        {
            // Check Windows High Contrast mode first
            if (SystemParameters.HighContrast)
                return AppTheme.HighContrast;

            // Check Windows Personalize registry setting for Apps theme
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme)
            {
                return appsUseLightTheme == 1 ? AppTheme.Light : AppTheme.Dark;
            }
        }
        catch
        {
            // Fallback to dark if registry check fails
        }

        return AppTheme.Dark;
    }

    private static AppTheme LoadSavedTheme()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Theme", out var themeProp))
                {
                    if (Enum.TryParse<AppTheme>(themeProp.GetString(), out var parsedTheme))
                    {
                        return parsedTheme;
                    }
                }
            }
        }
        catch
        {
            // Ignore corrupted config
        }

        return AppTheme.Dark;
    }

    private static void SaveTheme(AppTheme theme)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(new { Theme = theme.ToString() }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore settings save failures
        }
    }
}
