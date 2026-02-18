using System.Text.Json;

namespace MovFileIntegrityChecker.CLI.Utilities
{
    public class UserPreferences
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MovFileIntegrityChecker",
            "cli_settings.json");

        public string LastPath { get; set; } = string.Empty;
        public bool LastRecursive { get; set; } = true;
        public bool LastDeleteEmpty { get; set; } = false;
        public string LastMenuChoice { get; set; } = "1";

        public static UserPreferences Load()
        {
            try
            {
                if (File.Exists(AppDataPath))
                {
                    string json = File.ReadAllText(AppDataPath);
                    return JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
                }
            }
            catch
            {
                // Ignore errors and return defaults
            }
            return new UserPreferences();
        }

        public void Save()
        {
            try
            {
                string? directory = Path.GetDirectoryName(AppDataPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(AppDataPath, json);
            }
            catch
            {
                // Ignore errors
            }
        }
    }
}
