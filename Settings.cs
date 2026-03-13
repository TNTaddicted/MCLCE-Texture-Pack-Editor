using System;
using System.IO;
using System.Text.Json;

namespace LegacyConsolePackEditor
{
    internal class Settings
    {
        private const string SettingsFileName = "settings.json";

        public string? SwfEditorJarPath { get; set; }

        public static Settings Load()
        {
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path))
                    return new Settings();

                var json = File.ReadAllText(path);
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
                File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // best-effort
            }
        }

        private static string GetSettingsPath()
        {
            string dir = AppContext.BaseDirectory;
            return Path.Combine(dir, SettingsFileName);
        }
    }
}
