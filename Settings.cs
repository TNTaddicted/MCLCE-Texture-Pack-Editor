using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LegacyConsolePackEditor;

internal sealed class Settings
{
    private const string SettingsFileName = "settings.json";

    public List<string> RecentPaths { get; set; } = new();
    public string? LastTemplateZip { get; set; }
    public string ThemeMode { get; set; } = "Dark";
    public string AccentName { get; set; } = "Teal";

    public static Settings Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return new Settings();

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            string path = GetSettingsPath();
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // best effort only
        }
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(AppContext.BaseDirectory, SettingsFileName);
    }
}