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
    }
}

