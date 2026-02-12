// Security helper to make sure we're not messing with files that are already open.
// Nobody likes a "file is in use" error, so we check before we leap.
// Read-only operations only - we're not here to modify anything.

namespace MovFileIntegrityChecker.Core.Utilities
{
    public static class FileSecurityHelper
    {
        /// <summary>
        /// Tries to open a file to check if it's available and not locked by another process.
        /// This is a safe, read-only check that won't interfere with other programs.
        /// </summary>
        /// <param name="filePath">Path to the file to check</param>
        /// <param name="errorMessage">Error message if the file can't be opened</param>
        /// <returns>True if the file can be safely read, false otherwise</returns>
        public static bool TryOpenFile(string filePath, out string? errorMessage)
        {
            errorMessage = null;

            if (!File.Exists(filePath))
            {
                errorMessage = "File does not exist";
                return false;
            }

            FileStream? testStream = null;
            try
            {
                // Try to open the file with read-only access
                // FileShare.Read means other processes can still read it while we check
                // This is the safest way to verify the file is accessible
                testStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1,
                    FileOptions.None);

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                errorMessage = "Access denied - check file permissions";
                return false;
            }
            catch (IOException ex)
            {
                // This usually means the file is locked by another process
                errorMessage = $"File is currently in use by another application: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Unable to access file: {ex.Message}";
                return false;
            }
            finally
            {
                testStream?.Dispose();
            }
        }

        /// <summary>
        /// Validates that a file path is safe and doesn't contain suspicious patterns.
        /// Helps prevent path traversal attacks if this tool ever gets used in automated scenarios.
        /// </summary>
        /// <param name="filePath">The file path to validate</param>
        /// <returns>True if the path looks safe, false otherwise</returns>
        public static bool IsPathSafe(string filePath)
        {
            try
            {
                // Get the full path to normalize it
                string fullPath = Path.GetFullPath(filePath);

                // Make sure it's not trying to do anything sketchy like path traversal
                if (fullPath.Contains(".."))
                {
                    return false;
                }

                // Check that it's actually pointing to a file, not a directory
                FileAttributes attr = File.GetAttributes(fullPath);
                if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Opens a file stream with the most restrictive, secure settings.
        /// Read-only, allows sharing with other readers, and minimal buffer.
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <returns>A secure, read-only FileStream</returns>
        public static FileStream OpenSecureReadOnlyStream(string filePath)
        {
            return new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,  // Allow other processes to read, but not write
                bufferSize: 4096,
                FileOptions.SequentialScan);  // Optimize for sequential reading
        }
    }
}

