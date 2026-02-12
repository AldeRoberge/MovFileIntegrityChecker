using System.Text.Json;

namespace MovFileIntegrityChecker.Utilities
{
    /// <summary>
    /// Manages user preferences and settings, persisted to a JSON file.
    /// </summary>
    public class UserPreferences
    {
        private const string PreferencesFileName = "user_preferences.json";
        
        public string LastPath { get; set; } = string.Empty;
        public bool LastRecursive { get; set; } = true;
        public bool LastDeleteEmpty { get; set; }
        public string LastMenuChoice { get; set; } = "1";

        /// <summary>
        /// Gets the path to the preferences file in the user's application data folder.
        /// </summary>
        private static string GetPreferencesFilePath()
        {
            // Store in same directory as executable for simplicity
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
                Console.WriteLine($"Warning: Could not load preferences: {ex.Message}");
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
                Console.WriteLine($"Warning: Could not save preferences: {ex.Message}");
            }
        }
    }
}

