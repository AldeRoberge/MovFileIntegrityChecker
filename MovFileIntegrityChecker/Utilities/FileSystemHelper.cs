// Cleans up empty folders after we're done checking files.
// Sometimes corrupt files get deleted or moved, leaving behind empty directories.
// This just tidies things up so you don't have a bunch of empty folders cluttering your drive.

using static MovFileIntegrityChecker.Utilities.ConsoleHelper;

namespace MovFileIntegrityChecker.Utilities
{
    public static class FileSystemHelper
    {
        public static void DeleteEmptyDirectories(string rootPath, bool recursive)
        {
            try
            {
                var directories = Directory.GetDirectories(
                    rootPath,
                    "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                int deletedCount = 0;

                // Sort by descending path length to delete deep folders first
                foreach (var dir in directories.OrderByDescending(d => d.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                        deletedCount++;
                        WriteSuccess($"Deleted empty folder: {dir}");
                    }
                }

                // Check root folder itself
                if (!Directory.EnumerateFileSystemEntries(rootPath).Any())
                {
                    Directory.Delete(rootPath);
                    deletedCount++;
                    WriteSuccess($"Deleted empty root folder: {rootPath}");
                }

                if (deletedCount > 0)
                    WriteInfo($"\n✅ {deletedCount} empty folder(s) deleted.");
                else
                    WriteInfo("\nNo empty folders found.");
            }
            catch (Exception ex)
            {
                WriteError($"Error while deleting empty folders: {ex.Message}");
            }
        }
    }
}

