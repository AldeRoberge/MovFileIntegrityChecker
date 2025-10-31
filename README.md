# MOV File Integrity Checker

A comprehensive tool for analyzing MOV/MP4 video file integrity, detecting corruption, and generating detailed reports.

## Features

### 🔍 Per-File Analysis
- **Deep structural analysis** of MOV/MP4 file atoms
- **Validation** of file completeness and integrity
- **Duration analysis** with playable vs. corrupted segment detection
- **Visual timeline** showing where corruption occurs
- **JSON reports** for each analyzed file
- **HTML reports** for corrupted files with detailed information

### 📊 Global Analysis
- **Aggregate reporting** across all analyzed files
- **Interactive charts** showing:
  - Complete vs. Incomplete files (Pie Chart)
  - Corrupted vs. Valid files (Pie Chart)
  - Corruption rate by file size (Bar Chart)
  - Corruption rate by video duration (Bar Chart)
- **Key insights** and statistics
- **Correlation analysis** between file characteristics and corruption

### 🎯 Interactive Menu System
When run without arguments, the tool presents an interactive menu with three options:
1. **Per-File Analysis** - Analyze individual video files in a folder
2. **Global Analysis** - Generate aggregate reports from existing JSON data
3. **Both** - Run per-file analysis followed by global analysis

## Usage

### Interactive Mode (Recommended)
```bash
MovFileIntegrityChecker.exe
```

This will show a menu where you can select:
- Per-file analysis
- Global analysis
- Both modes

### Command-Line Mode

#### Analyze a single file:
```bash
MovFileIntegrityChecker.exe <path_to_mov_file>
```

#### Analyze a folder:
```bash
MovFileIntegrityChecker.exe <path_to_folder>
```

#### Analyze with options:
```bash
MovFileIntegrityChecker.exe <path> -r -s -d
```

#### Run global analysis only:
```bash
MovFileIntegrityChecker.exe --global-analysis
# or
MovFileIntegrityChecker.exe -g
```

### Command-Line Options

| Option | Description |
|--------|-------------|
| `-r`, `--recursive` | Check subfolders recursively |
| `-s`, `--summary` | Show summary only (no detailed output) |
| `-d`, `--delete-empty` | Delete empty folders after processing |
| `-g`, `--global-analysis` | Generate global analysis report from JSON files |

## Output Files

### Per-File Analysis Outputs

#### JSON Report (always generated)
Location: `T:\SPT\SP\Mont\Projets\3_PRJ\9-ALEXANDRE_DEMERS-ROBERGE\Fichiers Corrompus\<filename>_report.json`

Contains:
- File metadata (size, dates, attributes)
- Video duration information
- Integrity analysis results
- Atom structure details
- Issues and recommendations

#### HTML Report (only for corrupted files)
Location: Same folder as the video file, named `<filename>-Incomplet.html`

Contains:
- Visual presentation of file issues
- Duration timeline
- Atom structure visualization
- Sample frame extraction (if possible)

### Global Analysis Output

#### HTML Global Report
Location: `T:\SPT\SP\Mont\Projets\3_PRJ\9-ALEXANDRE_DEMERS-ROBERGE\Fichiers Corrompus\global-report-<timestamp>.html`

Contains:
- Overall statistics
- Interactive charts (using Chart.js)
- Correlation analysis
- Key insights and patterns
- Automatically opens in default browser

## Examples

### Example 1: Check a single folder recursively
```bash
MovFileIntegrityChecker.exe "C:\Videos" -r
```

### Example 2: Check multiple folders with cleanup
```bash
MovFileIntegrityChecker.exe "C:\Videos1" "D:\Videos2" --recursive --delete-empty
```

### Example 3: Quick summary check
```bash
MovFileIntegrityChecker.exe "C:\Videos" -r -s
```

### Example 4: Generate global report
After running per-file analysis, generate an aggregate report:
```bash
MovFileIntegrityChecker.exe --global-analysis
```

## Analysis Details

### File Integrity Checks
- ✅ Validates MOV/MP4 atom structure
- ✅ Checks for required atoms (ftyp, moov, mdat)
- ✅ Detects incomplete or truncated atoms
- ✅ Identifies unknown atom types
- ✅ Verifies atom alignment and file structure
- ✅ Calculates validation percentage

### Duration Analysis
- Uses **ffprobe** to extract video duration
- Calculates playable duration based on validated bytes
- Identifies missing/corrupted segments
- Shows percentage of playable content

### Global Analysis Insights
- **Completeness Analysis**: Shows ratio of complete vs. incomplete files
- **Corruption Analysis**: Identifies corruption patterns
- **Size Correlation**: Determines if larger files are more prone to corruption
- **Duration Correlation**: Shows if longer videos have higher corruption rates
- **Common Issues**: Identifies the most frequent problems across all files

## Requirements

- **.NET 9.0** or later
- **ffprobe** (from FFmpeg) - must be in PATH for duration analysis
- **ffmpeg** - must be in PATH for frame extraction

## Installation

1. Clone the repository
2. Build the solution:
   ```bash
   dotnet build
   ```
3. Run the executable from `bin/Build/net9.0/MovFileIntegrityChecker.exe`

## Technical Details

### Supported File Types
- `.mov` - QuickTime Movie
- `.mp4` - MPEG-4 Video
- `.m4v` - iTunes Video
- `.m4a` - MPEG-4 Audio

### Atom Types Recognized
The tool recognizes standard MOV/MP4 atoms including:
- Container atoms: `moov`, `trak`, `mdia`, `minf`, `stbl`
- Metadata atoms: `mvhd`, `tkhd`, `mdhd`, `hdlr`
- Data atoms: `mdat`, `ftyp`, `free`, `skip`, `wide`
- Stream atoms: `stsd`, `stts`, `stsc`, `stsz`, `stco`, `co64`

## Chart Visualizations

The global report includes interactive charts:

1. **Completeness Pie Chart**: Visual breakdown of complete vs. incomplete files
2. **Corruption Pie Chart**: Shows valid vs. corrupted file distribution
3. **File Size Bar Chart**: Compares total files vs. corrupted files across size ranges
4. **Duration Bar Chart**: Shows corruption rates across different video lengths

Each chart is interactive with hover tooltips showing detailed statistics.

## License

[Add your license information here]

## Contributing

[Add contribution guidelines here]

## Support

For issues or questions, please [create an issue](https://github.com/your-repo/issues) in the repository.

