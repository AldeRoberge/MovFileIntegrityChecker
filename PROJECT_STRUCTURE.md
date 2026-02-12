# Project Structure - Core and CLI Split

This document describes the reorganized solution structure, which has been split into two distinct projects:

## Project Overview

### **MovFileIntegrityChecker.Core** (Class Library)
A reusable .NET 9.0 class library containing all the core video file analysis logic. This library can be used by any .NET application, not just the CLI.

**Location:** `MovFileIntegrityChecker.Core/`

**Namespaces:**
- `MovFileIntegrityChecker.Core.Models` - Data models for analysis results
- `MovFileIntegrityChecker.Core.Services` - Analysis services and orchestrators
- `MovFileIntegrityChecker.Core.Utilities` - Core utilities (FFmpeg, file security, file system helpers)

**Key Components:**
- **Models/**
  - `FileModels.cs` - `FileCheckResult`, `AtomInfo` models
  - `JsonModels.cs` - JSON report data structures

- **Services/**
  - `AnalysisOrchestrator.cs` - Coordinates all analyzers and generates reports
  - `FileAnalyzer.cs` - MOV/MP4 file structure analysis
  - `VideoAnalyzer.cs` - Video duration detection via FFprobe
  - `JsonReportGenerator.cs` - JSON report generation

- **Utilities/**
  - `FfmpegHelper.cs` - FFmpeg installation and availability checks
  - `FileSecurityHelper.cs` - Safe file access validation
  - `FileSystemHelper.cs` - Directory cleanup operations
  - `ConsoleHelper.cs` - Console output formatting (temporary, will be refactored to interface)

### **MovFileIntegrityChecker.CLI** (Console Application)
A .NET 9.0 console application providing command-line interface and interactive menu for video file analysis.

**Location:** `MovFileIntegrityChecker.CLI/`

**Namespaces:**
- `MovFileIntegrityChecker.CLI` - Main program and entry point
- `MovFileIntegrityChecker.CLI.Utilities` - CLI-specific utilities
- `MovFileIntegrityChecker.CLI.Services` - CLI-specific services

**Key Components:**
- `Program.cs` - Main entry point, command-line parsing, interactive menu
- `Program.Legacy.cs` - Legacy HTML report generation code (to be refactored)
- **Services/**
  - `LegacyReportGenerators.cs` - Wrapper for legacy report generation
  
- **Utilities/**
  - `ConsoleHelper.cs` - Console output formatting for CLI
  - `UserPreferences.cs` - User preference persistence

- **DemoFiles/** - Sample video files for testing

### **MovFileIntegrityChecker** (Original Project)
The original monolithic project is still present in the solution but is being phased out. All functionality has been migrated to Core and CLI.

## Dependencies

```
MovFileIntegrityChecker.CLI
    └── MovFileIntegrityChecker.Core
```

The CLI project references the Core library. The Core library has no dependencies on the CLI project.

## Building the Solution

Build entire solution:
```powershell
dotnet build
```

Build individual projects:
```powershell
dotnet build MovFileIntegrityChecker.Core
dotnet build MovFileIntegrityChecker.CLI
```

## Running the Application

Run the CLI application:
```powershell
cd MovFileIntegrityChecker.CLI
dotnet run
```

Run with command-line arguments:
```powershell
dotnet run -- path/to/video.mp4
dotnet run -- path/to/folder -r -s
dotnet run -- --global-analysis
```

## Using the Core Library

The Core library can be referenced by other projects:

```xml
<ItemGroup>
  <ProjectReference Include="..\MovFileIntegrityChecker.Core\MovFileIntegrityChecker.Core.csproj" />
</ItemGroup>
```

Example usage:

```csharp
using MovFileIntegrityChecker.Core.Services;
using MovFileIntegrityChecker.Core.Models;

var orchestrator = new AnalysisOrchestrator();
var results = orchestrator.AnalyzePaths(
    new[] { "video.mp4" }, 
    recursive: false, 
    summaryOnly: false, 
    deleteEmpty: false
);

foreach (var result in results)
{
    Console.WriteLine($"File: {result.FilePath}");
    Console.WriteLine($"Has Issues: {result.HasIssues}");
}
```

## Configuration Files

### MovFileIntegrityChecker.Core.csproj
- Target Framework: net9.0
- Output Type: Library
- Configurations: Debug, Release, Build

### MovFileIntegrityChecker.CLI.csproj
- Target Framework: net9.0
- Output Type: Exe
- Configurations: Debug, Release, Build
- Includes DemoFiles (copied to output)
- References: MovFileIntegrityChecker.Core

## Future Improvements

1. **Refactor ConsoleHelper** - Create an `ILogger` interface in Core and inject console implementation from CLI
2. **Move Legacy Code** - Extract and refactor `Program.Legacy.cs` into proper services
3. **Configuration Management** - Make output directory configurable instead of hardcoded
4. **Testing** - Add unit tests for Core library components
5. **Publish Core** - Potentially publish Core library as a NuGet package for reuse

## Migration Notes

All code has been migrated from the original monolithic `MovFileIntegrityChecker` project to the new structure:

- **Core Logic** → `MovFileIntegrityChecker.Core`
- **CLI Interface** → `MovFileIntegrityChecker.CLI`
- **Namespaces Updated** - All namespaces now include `.Core` or `.CLI` suffix
- **Cross-References** - Legacy code dependencies have been handled appropriately

The original project can be removed once the migration is verified complete.

