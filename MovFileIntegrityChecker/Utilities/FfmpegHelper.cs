using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using static MovFileIntegrityChecker.Utilities.ConsoleHelper;

namespace MovFileIntegrityChecker.Utilities
{
    /// <summary>
    /// Helper class to check for ffmpeg installation and download if needed.
    /// </summary>
    public static class FfmpegHelper
    {
        private static readonly string FfmpegFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MovFileIntegrityChecker",
            "ffmpeg"
        );

        /// <summary>
        /// Checks if ffmpeg and ffprobe are installed and available.
        /// Downloads and installs them if not found.
        /// </summary>
        /// <returns>True if ffmpeg is available, false otherwise.</returns>
        public static async Task<bool> EnsureFfmpegInstalledAsync()
        {
            // First check if ffmpeg/ffprobe are in PATH
            if (IsFfmpegInPath())
            {
                WriteSuccess("✓ FFmpeg is already installed and available in PATH");
                return true;
            }

            // Check if we have a local installation
            if (IsFfmpegInLocalFolder())
            {
                WriteSuccess("✓ FFmpeg found in local installation folder");
                AddLocalFfmpegToPath();
                return true;
            }

            // Not found - offer to download
            Console.WriteLine();
            WriteWarning("⚠ FFmpeg not found on this system.");
            Console.WriteLine("FFmpeg is required to analyze video files.");
            Console.WriteLine();
            Console.Write("Would you like to download and install FFmpeg now? (y/n): ");
            
            string? response = Console.ReadLine()?.Trim().ToLower();
            if (response != "y" && response != "yes")
            {
                WriteError("Cannot proceed without FFmpeg. Exiting...");
                return false;
            }

            // Download and install
            return await DownloadAndInstallFfmpegAsync();
        }

        /// <summary>
        /// Checks if ffmpeg and ffprobe are available in the system PATH.
        /// </summary>
        private static bool IsFfmpegInPath()
        {
            return IsCommandAvailable("ffmpeg") && IsCommandAvailable("ffprobe");
        }

        /// <summary>
        /// Checks if a command is available in PATH.
        /// </summary>
        private static bool IsCommandAvailable(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                process.WaitForExit(3000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if ffmpeg exists in our local installation folder.
        /// </summary>
        private static bool IsFfmpegInLocalFolder()
        {
            string ffmpegExe = Path.Combine(FfmpegFolder, "bin", "ffmpeg.exe");
            string ffprobeExe = Path.Combine(FfmpegFolder, "bin", "ffprobe.exe");
            
            return File.Exists(ffmpegExe) && File.Exists(ffprobeExe);
        }

        /// <summary>
        /// Adds the local ffmpeg folder to the PATH environment variable for this process.
        /// </summary>
        private static void AddLocalFfmpegToPath()
        {
            string binPath = Path.Combine(FfmpegFolder, "bin");
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            
            if (!currentPath.Contains(binPath))
            {
                Environment.SetEnvironmentVariable("PATH", $"{binPath};{currentPath}");
            }
        }

        /// <summary>
        /// Downloads and installs ffmpeg from the official builds.
        /// </summary>
        private static async Task<bool> DownloadAndInstallFfmpegAsync()
        {
            try
            {
                Console.WriteLine();
                Console.WriteLine("Downloading FFmpeg...");
                
                // Determine the download URL based on the OS
                string downloadUrl;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Using gyan.dev builds (official Windows builds)
                    downloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    WriteError("Automatic download for Linux is not supported. Please install ffmpeg using your package manager:");
                    Console.WriteLine("  Ubuntu/Debian: sudo apt-get install ffmpeg");
                    Console.WriteLine("  Fedora: sudo dnf install ffmpeg");
                    Console.WriteLine("  Arch: sudo pacman -S ffmpeg");
                    return false;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    WriteError("Automatic download for macOS is not supported. Please install ffmpeg using Homebrew:");
                    Console.WriteLine("  brew install ffmpeg");
                    return false;
                }
                else
                {
                    WriteError("Unsupported operating system.");
                    return false;
                }

                // Create temp directory for download
                string tempZip = Path.Combine(Path.GetTempPath(), "ffmpeg.zip");
                string tempExtract = Path.Combine(Path.GetTempPath(), "ffmpeg_extract");

                try
                {
                    // Download the file
                    using (var httpClient = new HttpClient())
                    {
                        httpClient.Timeout = TimeSpan.FromMinutes(10);
                        
                        using (var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();
                            
                            var totalBytes = response.Content.Headers.ContentLength ?? -1;
                            using (var contentStream = await response.Content.ReadAsStreamAsync())
                            using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                var buffer = new byte[8192];
                                long totalRead = 0;
                                int bytesRead;
                                
                                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                                    totalRead += bytesRead;
                                    
                                    if (totalBytes > 0)
                                    {
                                        var progress = (int)((totalRead * 100) / totalBytes);
                                        Console.Write($"\rProgress: {progress}% ({totalRead / 1024 / 1024} MB / {totalBytes / 1024 / 1024} MB)");
                                    }
                                }
                            }
                        }
                    }
                    
                    Console.WriteLine();
                    Console.WriteLine("Download complete. Extracting...");

                    // Extract the zip file
                    if (Directory.Exists(tempExtract))
                    {
                        Directory.Delete(tempExtract, true);
                    }
                    Directory.CreateDirectory(tempExtract);
                    
                    ZipFile.ExtractToDirectory(tempZip, tempExtract);

                    // Find the bin folder in the extracted files (it's usually in a subfolder)
                    var binFolders = Directory.GetDirectories(tempExtract, "bin", SearchOption.AllDirectories);
                    if (binFolders.Length == 0)
                    {
                        WriteError("Could not find bin folder in the extracted ffmpeg archive.");
                        return false;
                    }

                    string sourceBinFolder = binFolders[0];
                    
                    // Create destination folder
                    Directory.CreateDirectory(FfmpegFolder);
                    string destBinFolder = Path.Combine(FfmpegFolder, "bin");
                    
                    if (Directory.Exists(destBinFolder))
                    {
                        Directory.Delete(destBinFolder, true);
                    }
                    
                    // Copy the bin folder
                    CopyDirectory(sourceBinFolder, destBinFolder);

                    // Verify installation
                    if (!IsFfmpegInLocalFolder())
                    {
                        WriteError("Installation verification failed.");
                        return false;
                    }

                    // Add to PATH for this session
                    AddLocalFfmpegToPath();

                    WriteSuccess($"✓ FFmpeg successfully installed to: {FfmpegFolder}");
                    Console.WriteLine();
                    Console.WriteLine("Note: FFmpeg has been added to PATH for this session only.");
                    Console.WriteLine("To use it permanently, add the following to your system PATH:");
                    Console.WriteLine($"  {Path.Combine(FfmpegFolder, "bin")}");
                    Console.WriteLine();

                    return true;
                }
                finally
                {
                    // Clean up temp files
                    try
                    {
                        if (File.Exists(tempZip))
                            File.Delete(tempZip);
                        if (Directory.Exists(tempExtract))
                            Directory.Delete(tempExtract, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError($"Failed to download and install FFmpeg: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Please install FFmpeg manually from: https://ffmpeg.org/download.html");
                return false;
            }
        }

        /// <summary>
        /// Recursively copies a directory.
        /// </summary>
        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }
    }
}

