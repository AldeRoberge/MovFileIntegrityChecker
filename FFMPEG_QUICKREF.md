# Quick Reference: FFmpeg Auto-Installation

## What Was Implemented

A fully automated FFmpeg detection and installation system that runs on application startup.

## Key Components

### 1. FfmpegHelper.cs (New File)
- Location: `MovFileIntegrityChecker\Utilities\FfmpegHelper.cs`
- Purpose: Manages FFmpeg detection and installation
- Main method: `EnsureFfmpegInstalledAsync()`

### 2. Program.cs (Modified)
- Changed `Main` to `async Task Main`
- Added FFmpeg check at startup (line 19)

## User Experience

### Windows Users
- **FFmpeg Installed**: Application starts normally
- **FFmpeg Missing**: Prompted to download (automatic installation)
- **Download Declined**: Application exits with message

### Linux/macOS Users
- **FFmpeg Installed**: Application starts normally
- **FFmpeg Missing**: Receives package manager commands to install manually

## Installation Paths

### System Check Order
1. System PATH (e.g., `C:\Program Files\ffmpeg\bin`)
2. Local AppData (`%LOCALAPPDATA%\MovFileIntegrityChecker\ffmpeg\bin`)
3. Download prompt if not found

### Auto-Install Location (Windows)
```
C:\Users\[Username]\AppData\Local\MovFileIntegrityChecker\ffmpeg\bin\
├── ffmpeg.exe
├── ffprobe.exe
└── ffplay.exe
```

## How to Test

### Test 1: Normal Operation (FFmpeg installed)
```bash
MovFileIntegrityChecker.exe
# Should see: ✓ FFmpeg is already installed and available in PATH
```

### Test 2: Auto-Install (FFmpeg not installed)
```bash
# Temporarily rename/remove FFmpeg from PATH
MovFileIntegrityChecker.exe
# Should prompt for download
# Enter 'y' to test auto-installation
```

### Test 3: Use Test Script
```bash
test-ffmpeg.bat
# Checks FFmpeg status and runs the application
```

## Configuration

### No Configuration Required!
The system works automatically with sensible defaults:
- ✅ Auto-detects FFmpeg
- ✅ Downloads from official source
- ✅ Installs to user folder (no admin needed)
- ✅ Adds to PATH automatically

### Manual Override
If you prefer a different FFmpeg installation:
1. Install FFmpeg manually
2. Add to system PATH
3. Application will detect and use it

## Troubleshooting

### Issue: Download fails
**Solution**: Install FFmpeg manually from https://ffmpeg.org/download.html

### Issue: Permission error
**Solution**: Ensure AppData folder is writable (usually no issue)

### Issue: PATH not updated
**Solution**: FFmpeg is added to PATH for current session only. For permanent use, add manually to system PATH.

### Issue: Linux/macOS auto-download not working
**Expected**: Auto-download only works on Windows. Use provided package manager commands.

## For Developers

### Integration Points
```csharp
// In Program.cs Main method:
if (!await FfmpegHelper.EnsureFfmpegInstalledAsync())
{
    return; // Exit if unavailable
}
```

### Adding to New Projects
1. Copy `FfmpegHelper.cs` to your Utilities folder
2. Add check to your Main method (make it async)
3. Ensure ConsoleHelper is available for colored output

### Customization
Modify `FfmpegHelper.cs` to:
- Change download URL
- Modify installation path
- Add version checking
- Implement auto-updates

## Summary

✅ **What it does**: Automatically ensures FFmpeg is available before running video analysis
✅ **How it works**: Checks PATH → Checks AppData → Offers download → Installs
✅ **User benefit**: No manual FFmpeg installation required
✅ **Developer benefit**: One less dependency to document and support

## Files to Review

1. `MovFileIntegrityChecker\Utilities\FfmpegHelper.cs` - Implementation
2. `MovFileIntegrityChecker\Program.cs` - Integration point
3. `FFMPEG_INSTALLATION.md` - Detailed documentation
4. `FFMPEG_IMPLEMENTATION_SUMMARY.md` - Complete summary

---

**Status**: ✅ Implemented and Ready
**Date**: December 19, 2025

