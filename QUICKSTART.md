# Quick Start Guide

## Running the Program

### Option 1: Interactive Menu (Recommended for new users)
```cmd
cd C:\Users\demersra\Documents\GitHub\MovFileIntegrityChecker\MovFileIntegrityChecker\bin\Build\net9.0
MovFileIntegrityChecker.exe
```
Then select:
- **1** for per-file analysis
- **2** for global analysis
- **3** for both

### Option 2: Command Line (Per-File Analysis)
```cmd
# Analyze a single file
MovFileIntegrityChecker.exe "C:\path\to\video.mov"

# Analyze a folder recursively
MovFileIntegrityChecker.exe "C:\Videos" -r

# Analyze with cleanup
MovFileIntegrityChecker.exe "C:\Videos" -r --delete-empty
```

### Option 3: Command Line (Global Analysis)
```cmd
MovFileIntegrityChecker.exe --global-analysis
# Then enter the path to your JSON reports folder
```

## Quick Test with Sample Data

Test the global analysis with provided sample data:

```cmd
cd C:\Users\demersra\Documents\GitHub\MovFileIntegrityChecker\MovFileIntegrityChecker\bin\Build\net9.0
MovFileIntegrityChecker.exe --global-analysis
# When prompted, enter: C:\Users\demersra\Documents\GitHub\MovFileIntegrityChecker\TestReports
```

The report will:
- Analyze 5 sample files
- Show 60% corruption rate
- Display interactive charts
- Auto-open in your browser

## Understanding the Output

### Per-File Analysis Creates:
1. **JSON Report** (always): `<filename>_report.json` in the configured output directory
2. **HTML Report** (if corrupted): `<filename>-Incomplet.html` next to the original file

### Global Analysis Creates:
1. **HTML Report**: `global-report-<timestamp>.html` with:
   - Statistics cards
   - Pie charts (completeness & corruption)
   - Bar charts (size & duration correlations)
   - Key insights

## Command-Line Flags

| Flag | Description |
|------|-------------|
| `-r` or `--recursive` | Check subfolders |
| `-s` or `--summary` | Summary only (less output) |
| `-d` or `--delete-empty` | Delete empty folders |
| `-g` or `--global-analysis` | Generate global report |

## Configuration

Default JSON report directory (can be changed in code):
```
T:\SPT\SP\Mont\Projets\3_PRJ\9-ALEXANDRE_DEMERS-ROBERGE\Fichiers Corrompus
```

To change: Edit `CreateJsonReport()` function in `Program.cs`

## Requirements

- **ffprobe** and **ffmpeg** must be in PATH
- **.NET 9.0** runtime
- Internet connection (for Chart.js CDN in global reports)

## Troubleshooting

**Q: "No JSON reports found"**  
A: Run per-file analysis first to generate JSON reports

**Q: "Duration shows as 0"**  
A: Ensure ffprobe is installed and in your PATH

**Q: "Charts not showing in global report"**  
A: Check internet connection (Chart.js loads from CDN)

**Q: "Permission denied when saving report"**  
A: Run as administrator or change output directory

## Next Steps

1. Run per-file analysis on your video folders
2. Review individual HTML reports for corrupted files
3. Run global analysis to see patterns
4. Use insights to improve transfer/storage processes

For detailed documentation, see `README.md`  
For testing instructions, see `TESTING.md`  
For implementation details, see `IMPLEMENTATION_SUMMARY.md`

