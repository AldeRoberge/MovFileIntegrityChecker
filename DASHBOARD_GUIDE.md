# Interactive Dashboard Guide

## Overview
The enhanced global analysis dashboard provides visual, interactive insights into video file transfer failures. It helps identify **why** and **when** file transfers fail.

## How to Generate the Dashboard

### Method 1: Interactive Menu
1. Run the program without arguments:
   ```bash
   dotnet run
   ```
2. Select option **2** (Global Analysis) or **3** (Both)
3. Press Enter to use the default JSON reports directory, or specify a custom path

### Method 2: Command Line
```bash
dotnet run -- --global-analysis
```
or
```bash
dotnet run -- -g
```

## Dashboard Features

### 📊 Visualizations

#### 1. **Pie Charts**
- **File Completeness Status**: Complete vs Incomplete files
- **File Corruption Status**: Valid vs Corrupted files

#### 2. **Bar Charts**
- **Corruption Rate by File Size**: Shows which file size ranges are most vulnerable
- **Corruption Rate by Video Duration**: Identifies if longer videos fail more often

#### 3. **Heatmap** 🕐
- **Transfer Failure Frequency by Hour**: Reveals if files fail at specific hours (e.g., 3-4 AM)
- Shows both total files modified per hour and failed files
- Helps identify server maintenance windows or scheduled operations

#### 4. **Scatter Plots**
- **File Size vs Playable %**: 
  - **Green dots** = Valid files (100% playable)
  - **Red dots** = Corrupted files (partial playability)
  - Shows correlation between file size and corruption
- **Last Modified Hour vs Corruption Rate**: 
  - Bubble chart showing corruption patterns by time of day
  - Bubble size represents number of files

#### 5. **Timeline Chart** 📅
- **File Creation vs Last Modification**: 
  - Green points = File creation times
  - Red points = Last modification times (for corrupted files)
  - Helps detect abrupt transfer interruptions

#### 6. **Data Table** 📋
- **All files** with detailed metrics (corrupted files listed first):
  - File name, size, duration
  - Playable %, Corruption %
  - Last modified hour
  - Status (✅ Valid or ❌ Corrupted)
- **Color coding**:
  - Green rows = Valid files (100% playable)
  - Red rows = Corrupted files
- Sortable and easy to scan for patterns

### 🔍 Key Insights Section

The dashboard automatically generates insights including:

- Overall corruption rate
- **High-risk time windows** (e.g., "80% of files modified at 03:00-04:00 are corrupted")
- File size correlations
- Duration correlations
- Data recovery potential (average playable percentage)
- Transfer interruption patterns
- Most common structural issues

### 💡 Root Cause Analysis

Based on the data patterns, the dashboard suggests likely causes such as:
- Scheduled server maintenance interrupting transfers
- Network congestion during peak hours
- Timeout issues with large files
- Network instability or storage issues

## Example Use Cases

### Scenario 1: Nightly Failures
If the heatmap shows high corruption at 3-4 AM:
- **Likely Cause**: Scheduled server maintenance or automatic shutdown
- **Solution**: Adjust transfer schedule or disable maintenance during transfer windows

### Scenario 2: Large File Issues
If scatter plot shows larger files have lower playable %:
- **Likely Cause**: Network timeouts or insufficient buffer sizes
- **Solution**: Increase timeout values, optimize buffer sizes, or implement chunked transfers

### Scenario 3: Peak Hour Problems
If failures cluster around 12-2 PM:
- **Likely Cause**: Network congestion or server load
- **Solution**: Schedule transfers during off-peak hours

## Output Location

The dashboard HTML file is saved in the same directory as your JSON reports with a timestamp:
```
global-report-20251031-143045.html
```

The file automatically opens in your default browser after generation.

## Technical Details

- **Technology**: Chart.js for interactive visualizations
- **Responsive**: Works on desktop and mobile browsers
- **Interactive**: Hover over charts for detailed tooltips
- **Self-contained**: Single HTML file with embedded CSS and JavaScript

## Tips for Better Analysis

1. **Run per-file analysis first** to generate JSON reports for all your video files
2. **Collect data over time** to identify patterns (e.g., daily failures at the same hour)
3. **Compare before/after** dashboards when testing solutions
4. **Focus on patterns** not individual files - look for clusters in time, size, or duration
5. **Cross-reference insights** - if multiple metrics point to the same time window, that's your smoking gun

## Troubleshooting

**No JSON reports found?**
- Run per-file analysis first with option 1 or 3 from the menu
- Check that JSON reports are in: `T:\SPT\SP\Mont\Projets\3_PRJ\9-ALEXANDRE_DEMERS-ROBERGE\Fichiers Corrompus`

**Dashboard looks empty?**
- Ensure you have at least a few video files analyzed
- Some charts require files with duration data (obtained via ffprobe)

**Charts not displaying?**
- Check your internet connection (Chart.js is loaded from CDN)
- Open browser console (F12) to check for JavaScript errors

