# Testing Guide for Global Analysis Feature

## Quick Test with Sample Data

We've created 5 sample JSON reports in the `TestReports` folder to test the global analysis feature:

1. **small_corrupted_report.json** - 50 MB, 45 sec, 65.9% playable (corrupted)
2. **large_valid_report.json** - 5 GB, 30 min, 100% playable (valid)
3. **medium_corrupted_report.json** - 500 MB, 5.5 min, 68.15% playable (corrupted)
4. **short_valid_report.json** - 100 MB, 3 min, 100% playable (valid)
5. **long_corrupted_report.json** - 2 GB, 15.8 min, 75% playable (corrupted)

## Expected Results

### Statistics:
- **Total Files**: 5
- **Complete Files**: 2 (40%)
- **Corrupted Files**: 3 (60%)
- **Total Size**: ~7.7 GB

### Size Distribution:
- 0-100 MB: 1 file (1 corrupted) - 100% corruption rate
- 100-500 MB: 2 files (1 corrupted) - 50% corruption rate
- 500 MB-1 GB: 0 files
- 1-5 GB: 1 file (1 corrupted) - 100% corruption rate
- 5+ GB: 1 file (0 corrupted) - 0% corruption rate

### Duration Distribution:
- 0-1 min: 1 file (1 corrupted) - 100% corruption rate
- 1-5 min: 1 file (0 corrupted) - 0% corruption rate
- 5-15 min: 1 file (1 corrupted) - 100% corruption rate
- 15-30 min: 1 file (1 corrupted) - 100% corruption rate
- 30+ min: 1 file (0 corrupted) - 0% corruption rate

## How to Test

### Method 1: Run the executable and select option 2
```cmd
cd C:\Users\demersra\Documents\GitHub\MovFileIntegrityChecker\MovFileIntegrityChecker\bin\Build\net9.0
MovFileIntegrityChecker.exe
# Select option 2 (Global Analysis)
# Enter path: C:\Users\demersra\Documents\GitHub\MovFileIntegrityChecker\TestReports
```

### Method 2: Use command-line flag
```cmd
cd C:\Users\demersra\Documents\GitHub\MovFileIntegrityChecker\MovFileIntegrityChecker\bin\Build\net9.0
MovFileIntegrityChecker.exe --global-analysis
# Enter path: C:\Users\demersra\Documents\GitHub\MovFileIntegrityChecker\TestReports
```

### Method 3: Run with dotnet
```cmd
cd C:\Users\demersra\Documents\GitHub\MovFileIntegrityChecker
dotnet run --project MovFileIntegrityChecker -- --global-analysis
# Enter path: C:\Users\demersra\Documents\GitHub\MovFileIntegrityChecker\TestReports
```

## What to Look For

The global report should:
1. ✅ Show correct statistics (5 total, 2 complete, 3 corrupted)
2. ✅ Display interactive pie charts
3. ✅ Show bar charts with correlation data
4. ✅ Generate insights about corruption patterns
5. ✅ Automatically open in your default browser
6. ✅ Be saved as `global-report-<timestamp>.html` in the TestReports folder

## Insights to Expect

- Overall corruption rate: 60%
- Most common issue: "Incomplete atom" or "File structure is invalid or incomplete"
- Files in 0-100 MB and 1-5 GB ranges have highest corruption rates
- Average playable percentage for corrupted files: ~69.7%

## Clean Up

After testing, you can delete the TestReports folder if desired:
```cmd
rmdir /s C:\Users\demersra\Documents\GitHub\MovFileIntegrityChecker\TestReports
```

