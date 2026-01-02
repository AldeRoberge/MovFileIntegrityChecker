# FFmpeg Auto-Installation Feature

## Overview
The MOV File Integrity Checker now includes automatic FFmpeg detection and installation functionality. This ensures users can run the application without manually installing FFmpeg dependencies.

## How It Works

### On Application Startup
1. **Check System PATH**: The application first checks if `ffmpeg` and `ffprobe` are available in the system PATH
2. **Check Local Installation**: If not in PATH, it checks for a local installation in the application data folder
3. **Offer Download**: If FFmpeg is not found, the user is prompted to download and install it

### Automatic Installation (Windows Only)
- Downloads the official FFmpeg builds from `gyan.dev` (trusted Windows builds)
- Extracts to: `%LOCALAPPDATA%\MovFileIntegrityChecker\ffmpeg\bin\`
- Adds to PATH for the current session
- Provides instructions for permanent PATH configuration

### Manual Installation (Linux/macOS)
For non-Windows platforms, the application provides instructions:
- **Linux**: Package manager commands (apt, dnf, pacman)
- **macOS**: Homebrew installation command

## Implementation Details

### New File: `FfmpegHelper.cs`
Location: `MovFileIntegrityChecker\Utilities\FfmpegHelper.cs`

Key Methods:
- `EnsureFfmpegInstalledAsync()` - Main entry point, checks and installs if needed
- `IsFfmpegInPath()` - Checks if FFmpeg is in system PATH
- `IsFfmpegInLocalFolder()` - Checks local application data folder
- `DownloadAndInstallFfmpegAsync()` - Downloads and installs FFmpeg
- `AddLocalFfmpegToPath()` - Adds local installation to PATH

### Modified File: `Program.cs`
- Changed `Main` method from `void` to `async Task`
- Added FFmpeg check at startup before any analysis runs
- Application exits gracefully if FFmpeg is not available and user declines installation

## User Experience

### First Run (No FFmpeg)
```
=== MOV File Integrity Checker ===

⚠ FFmpeg not found on this system.
FFmpeg is required to analyze video files.

Would you like to download and install FFmpeg now? (y/n):
```

### If User Accepts
```
Downloading FFmpeg...
Progress: 45% (45 MB / 100 MB)
Download complete. Extracting...
✓ FFmpeg successfully installed to: C:\Users\...\AppData\Local\MovFileIntegrityChecker\ffmpeg

Note: FFmpeg has been added to PATH for this session only.
To use it permanently, add the following to your system PATH:
  C:\Users\...\AppData\Local\MovFileIntegrityChecker\ffmpeg\bin
```

### If FFmpeg Already Installed
```
=== MOV File Integrity Checker ===

✓ FFmpeg is already installed and available in PATH
```

## Technical Notes

### Download Source
- **Windows**: https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip
- Contains ffmpeg, ffprobe, and ffplay executables
- Includes all necessary codecs for video analysis

### Installation Path
- Windows: `%LOCALAPPDATA%\MovFileIntegrityChecker\ffmpeg\`
- This location does not require administrator privileges
- Persists across application updates

### Session PATH Modification
The application adds FFmpeg to the PATH environment variable for the current process only:
```csharp
Environment.SetEnvironmentVariable("PATH", $"{binPath};{currentPath}");
```

### Error Handling
- Network errors during download
- Extraction failures
- Permission issues
- Invalid ZIP structure
- All errors provide user-friendly messages and fallback instructions

## Benefits

1. **Improved User Experience**: No manual setup required
2. **Cross-Platform Support**: Provides appropriate instructions for each OS
3. **Non-Intrusive**: Asks permission before downloading
4. **Transparent**: Shows progress and installation location
5. **Graceful Degradation**: Application exits cleanly if FFmpeg unavailable

## Future Enhancements

Potential improvements for future versions:
- Silent installation mode for CI/CD environments
- Version checking and updates
- Alternative download sources/mirrors
- Retry logic for failed downloads
- Checksum verification of downloaded files
- Custom installation path option

## Testing

To test the FFmpeg installation feature:

1. **Test with FFmpeg installed**: Run normally, should see success message
2. **Test without FFmpeg**: Temporarily remove FFmpeg from PATH, should prompt for installation
3. **Test cancellation**: Decline installation, application should exit gracefully
4. **Test download**: Accept installation, verify download progress and success

## Dependencies

The FFmpeg helper uses these .NET libraries:
- `System.Diagnostics.Process` - For running ffmpeg/ffprobe
- `System.Net.Http.HttpClient` - For downloading FFmpeg
- `System.IO.Compression.ZipFile` - For extracting the archive
- `System.Runtime.InteropServices` - For OS detection

