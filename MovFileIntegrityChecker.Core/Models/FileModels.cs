// Data structures for storing file analysis results.
// AtomInfo holds info about each chunk of the video file.
// FileCheckResult bundles everything together - issues, atoms, duration, the whole nine yards.

using MovFileIntegrityChecker.Core.Services;

namespace MovFileIntegrityChecker.Core.Models
{
    public class AtomInfo
    {
        public string Type { get; set; } = string.Empty;
        public long Size { get; set; }
        public long Offset { get; set; }
        public bool IsComplete { get; set; }
    }

    public class FileCheckResult
    {
        public string FilePath { get; set; } = string.Empty;
        public bool HasIssues => Issues.Count > 0;
        public List<string> Issues { get; set; } = new();
        public List<AtomInfo> Atoms { get; set; } = new();
        public long FileSize { get; set; }
        public long BytesValidated { get; set; }
        public double TotalDuration { get; set; }
        public double PlayableDuration { get; set; }
        public List<AudioTrackInfo> AudioTracks { get; set; } = new();
        public string? HtmlReportPath { get; set; }
        public string? JsonReportPath { get; set; }
        
        // AJA integration - store download URL if file came from AJA server
        public string? AjaDownloadUrl { get; set; }
        public string? AjaServerName { get; set; }
    }

    // AJA Server models
    public class AjaServer
    {
        public required string Name { get; set; }
        public required string Url { get; set; }
        public required string Type { get; set; } // "Master" or "Backup"
    }

    public class AjaClip
    {
        public required string ClipName { get; set; }
        public required string Timestamp { get; set; }
        public required string FourCC { get; set; }
        public required string Width { get; set; }
        public required string Height { get; set; }
        public required string FrameCount { get; set; }
        public required string FrameRate { get; set; }
        public required string Interlace { get; set; }
        public required string ServerName { get; set; }
        public required string ServerUrl { get; set; }
        
        public string DownloadUrl => $"{ServerUrl}media/{ClipName}";
    }

    public class AjaFileStatus
    {
        public required AjaClip Clip { get; set; }
        public bool ExistsLocally { get; set; }
        public string? LocalPath { get; set; }
        public bool? IsCorrupted { get; set; }
        public FileCheckResult? CheckResult { get; set; }
    }
}

