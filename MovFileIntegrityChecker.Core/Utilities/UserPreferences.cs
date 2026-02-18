using System.Text.Json;

namespace MovFileIntegrityChecker.Core.Utilities
{
    /// <summary>
    /// Manages user preferences and settings, persisted to a JSON file.
    /// This is shared between CLI and Web projects.
    /// </summary>
    public class UserPreferences
    {
        private const string PreferencesFileName = "user_preferences.json";

        // CLI Settings
        public string LastPath { get; set; } = string.Empty;
        public bool LastRecursive { get; set; } = true;
        public bool LastDeleteEmpty { get; set; }
        public string LastMenuChoice { get; set; } = "1";

        // Web Settings
        public string WebLastScanPath { get; set; } = @"T:\SPT\SP\Mont\Prod2\2_COU\_DL";
        public bool WebLastRecursive { get; set; } = true;
        public int WebAutoScanInterval { get; set; } = 24;
        public bool WebAutoScanEnabled { get; set; } = false;
        public bool WebUseCustomReportFolder { get; set; } = false;
        public string WebCustomReportFolder { get; set; } = @"C:\Reports";

        /// <summary>
        /// Gets the path to the preferences file in the user's application data folder.
        /// </summary>
        private static string GetPreferencesFilePath()
        {
            // Store in same directory as executable for simplicity
            // In a real web app this might need to be a fixed path, but for this tool it works well
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(appDir, PreferencesFileName);
        }

        /// <summary>
        /// Loads user preferences from disk, or returns defaults if file doesn't exist.
        /// </summary>
        public static UserPreferences Load()
        {
            string filePath = GetPreferencesFilePath();

            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var preferences = JsonSerializer.Deserialize<UserPreferences>(json);
                    return preferences ?? new UserPreferences();
                }
            }
            catch (Exception ex)
            {
                // In console apps this is visible, in web apps it goes to the logs
                System.Diagnostics.Debug.WriteLine($"Warning: Could not load preferences: {ex.Message}");
            }

            return new UserPreferences();
        }

        /// <summary>
        /// Saves current preferences to disk.
        /// </summary>
        public void Save()
        {
            string filePath = GetPreferencesFilePath();

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Could not save preferences: {ex.Message}");
            }
        }
    }
}
