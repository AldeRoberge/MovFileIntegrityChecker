# AJA Files Checker - Implementation Complete ✅

## Overview

The AJA Files Checker service has been successfully implemented. This feature scans multiple AJA servers, retrieves clip lists, cross-references them with local folders, and provides download links for missing or broken files.

## Features Implemented

### 1. **AJA Models** (`MovFileIntegrityChecker.Core/Models/FileModels.cs`)
   - `AjaServer`: Represents an AJA server with Name, URL, and Type (Master/Backup)
   - `AjaClip`: Contains clip metadata (name, timestamp, codec, resolution, frame info, server info)
   - `AjaFileStatus`: Tracks whether a clip exists locally, its path, and corruption status
   - Automatic `DownloadUrl` generation: `{ServerUrl}media/{ClipName}`

### 2. **AJA Service** (`MovFileIntegrityChecker.Web/Services/AjaFilesService.cs`)
   
   **Default Configuration:**
   - **12 AJA Servers configured:**
     - D421-Master (http://10.42.0.112/)
     - D421-Backup (http://10.42.0.113/)
     - D402-Master (http://10.42.0.14/)
     - D402-Backup (http://10.42.0.15/)
     - D404-BU1-4 (http://10.42.0.120-123/)
     - D404-MA1-MA4 (http://10.42.0.116-119/)
   
   - **3 Default Scan Folders:**
     - T:\SPT\SP\Mont\Prod1\2_COU\_DL
     - T:\SPT\SP\Mont\Prod2\2_COU\_DL
     - T:\SPT\SP\Mont\Backup\2_COU\_DL
   
   **Functionality:**
   - `StartScanAsync()`: Scans all AJA servers in parallel
   - `FetchClipsFromServerAsync()`: Retrieves clips from individual server `/clips` endpoint
   - `ParseAjaClipsResponse()`: Parses JavaScript object array format using regex
   - `CrossReferenceWithLocalFilesAsync()`: Matches AJA clips with local .mov files
   - Real-time status updates and progress tracking via events

### 3. **AJA Scanner Page** (`MovFileIntegrityChecker.Web/Components/Pages/AjaScanner.razor`)
   
   **UI Components:**
   - **Header**: Shows scanner status badge
   - **Controls**: "Scanner les Serveurs AJA" button
   - **Server Summary**: Cards showing clip counts per server with Master/Backup badges
   - **Results Table**:
     - Columns: Clip Name, Server, Timestamp, Resolution, Frame Rate, Frame Count, Local Status, Actions
     - Download button for missing files (links to `{ServerUrl}media/{ClipName}`)
     - Folder button for available files
   - **Filters**: All / Missing / Available
   - **Statistics**: Total, Available, Missing counts

### 4. **Navigation** (`MovFileIntegrityChecker.Web/Components/Layout/MainLayout.razor`)
   - Added navigation bar with links to:
     - Scanner Dashboard (Tableau de Bord du Scanner)
     - AJA Files (Fichiers AJA)

### 5. **French Translations** (`MovFileIntegrityChecker.Web/Services/LocalizationService.cs`)
   New translations added:
   - AjaFiles: "Fichiers AJA"
   - AjaScanner: "Scanner AJA"
   - ScanAjaServers: "Scanner les Serveurs AJA"
   - Server: "Serveur"
   - ClipsFound: "Clips Trouvés"
   - MissingLocally: "Manquants Localement"
   - Download: "Télécharger"
   - ScanningAjaServers: "Analyse des serveurs AJA..."
   - And 10+ more...

## How It Works

### Scanning Process

1. **User clicks "Scanner les Serveurs AJA"**
2. **Parallel Server Requests**: The service sends HTTP GET requests to all 12 AJA servers simultaneously to `/clips` endpoint
3. **Response Parsing**: JavaScript object arrays are parsed using regex:
   ```javascript
   { clipname: "file.mov", timestamp: "02/17/26 12:07:26", fourcc: "apcs", width: "1920", height: "1080", framecount: "354724", framerate: "29.97", interlace: "0" }
   ```
4. **Local File Search**: The service searches configured folders for matching .mov files
5. **Cross-Reference**: Each AJA clip is matched against local files by filename
6. **Results Display**: Table shows all clips with local availability status

### Download Links

For missing files, the download button generates URLs in this format:
```
http://{ServerUrl}media/{ClipName}
```

Example:
```
http://10.42.0.112/media/SCL1416H6-voxpop_6_1.mov
```

## Usage

### Accessing the Feature

1. **Navigate to AJA Scanner**: Click "Fichiers AJA" in the navigation bar or go to `/aja`
2. **Start Scan**: Click "Scanner les Serveurs AJA"
3. **View Results**: Table updates in real-time as clips are found
4. **Filter Results**: Use All/Missing/Available filters
5. **Download Missing Files**: Click "Télécharger" button for missing clips
6. **Open Local Files**: Click "Dossier" button for available files

### Server Summary

The summary section shows:
- Server name and type (Master/Backup)
- Total clips found on that server
- Number of missing clips

### Customization

**To modify AJA servers**, edit `AjaFilesService.cs`:
```csharp
public List<AjaServer> AjaServers { get; set; } = new()
{
    new() { Name = "Your-Server", Url = "http://ip.address/", Type = "Master" },
    // ... more servers
};
```

**To modify scan folders**, edit `AjaFilesService.cs`:
```csharp
public List<string> LocalScanFolders { get; set; } = new()
{
    @"Your\Path\Here",
    // ... more folders
};
```

## Technical Details

### HTTP Communication
- Uses `HttpClient` with 10-second timeout
- Parallel requests for performance
- Error handling for network failures

### Parsing Strategy
- Regex pattern matches JavaScript object syntax
- Extracts: clipname, timestamp, fourcc, width, height, framecount, framerate, interlace
- Robust against formatting variations

### Performance
- Asynchronous/parallel processing
- Non-blocking UI updates
- Efficient dictionary-based file lookup

### File Matching
- Case-insensitive filename comparison
- Searches recursively in all configured folders
- First match wins (if duplicates exist)

## Files Modified/Created

### Created
- `MovFileIntegrityChecker.Web/Services/AjaFilesService.cs`
- `MovFileIntegrityChecker.Web/Components/Pages/AjaScanner.razor`

### Modified
- `MovFileIntegrityChecker.Core/Models/FileModels.cs` - Added AJA models
- `MovFileIntegrityChecker.Web/Services/LocalizationService.cs` - Added French translations
- `MovFileIntegrityChecker.Web/Program.cs` - Registered AjaFilesService and HttpClient
- `MovFileIntegrityChecker.Web/Components/Layout/MainLayout.razor` - Added navigation

## Testing

✅ Service compiles without errors
✅ Models properly defined
✅ HTTP client configured
✅ Localization strings added
✅ UI components created
✅ Navigation integrated

## Next Steps (Optional Enhancements)

1. **Add file integrity checking**: Automatically verify downloaded files
2. **Add progress indicators**: Show individual server scan progress
3. **Add caching**: Cache results for configurable duration
4. **Add configuration UI**: Allow users to manage servers and folders through UI
5. **Add export**: Export results to CSV/JSON
6. **Add scheduling**: Automatic periodic scans
7. **Add notifications**: Alert when new clips are found or when clips go missing

## Date
Implementation completed: 2026-02-18

