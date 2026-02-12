// All the data models for JSON report output.
// These classes get serialized into the JSON reports so everything is properly structured
// and easy to parse if you're feeding this into another tool or dashboard.

using System.Text.Json.Serialization;

namespace MovFileIntegrityChecker.Core.Models
{
    public class JsonFileMetadata
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("fullPath")]
        public string FullPath { get; set; } = string.Empty;

        [JsonPropertyName("fileSizeBytes")]
        public long FileSizeBytes { get; set; }

        [JsonPropertyName("fileSizeMB")]
        public double FileSizeMB { get; set; }

        [JsonPropertyName("creationTimeUtc")]
        public DateTime CreationTimeUtc { get; set; }

        [JsonPropertyName("lastModifiedTimeUtc")]
        public DateTime LastModifiedTimeUtc { get; set; }

        [JsonPropertyName("lastAccessTimeUtc")]
        public DateTime LastAccessTimeUtc { get; set; }

        [JsonPropertyName("fileExtension")]
        public string FileExtension { get; set; } = string.Empty;

        [JsonPropertyName("isReadOnly")]
        public bool IsReadOnly { get; set; }

        [JsonPropertyName("attributes")]
        public string Attributes { get; set; } = string.Empty;
    }

    public class JsonVideoDuration
    {
        [JsonPropertyName("totalDurationSeconds")]
        public double TotalDurationSeconds { get; set; }

        [JsonPropertyName("totalDurationFormatted")]
        public string TotalDurationFormatted { get; set; } = string.Empty;

        [JsonPropertyName("playableDurationSeconds")]
        public double PlayableDurationSeconds { get; set; }

        [JsonPropertyName("playableDurationFormatted")]
        public string PlayableDurationFormatted { get; set; } = string.Empty;

        [JsonPropertyName("missingDurationSeconds")]
        public double MissingDurationSeconds { get; set; }

        [JsonPropertyName("missingDurationFormatted")]
        public string MissingDurationFormatted { get; set; } = string.Empty;

        [JsonPropertyName("playablePercentage")]
        public double PlayablePercentage { get; set; }

        [JsonPropertyName("corruptedPercentage")]
        public double CorruptedPercentage { get; set; }
    }

    public class JsonAtomInfo
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("offsetBytes")]
        public long OffsetBytes { get; set; }

        [JsonPropertyName("isComplete")]
        public bool IsComplete { get; set; }

        [JsonPropertyName("isKnownType")]
        public bool IsKnownType { get; set; }
    }

    public class JsonRequiredAtoms
    {
        [JsonPropertyName("ftyp")]
        public bool Ftyp { get; set; }

        [JsonPropertyName("moov")]
        public bool Moov { get; set; }

        [JsonPropertyName("mdat")]
        public bool Mdat { get; set; }
    }

    public class JsonIntegrityAnalysis
    {
        [JsonPropertyName("bytesValidated")]
        public long BytesValidated { get; set; }

        [JsonPropertyName("validationPercentage")]
        public double ValidationPercentage { get; set; }

        [JsonPropertyName("atomsFound")]
        public int AtomsFound { get; set; }

        [JsonPropertyName("hasStructuralIssues")]
        public bool HasStructuralIssues { get; set; }

        [JsonPropertyName("issues")]
        public List<string> Issues { get; set; } = new();

        [JsonPropertyName("atoms")]
        public List<JsonAtomInfo> Atoms { get; set; } = new();

        [JsonPropertyName("hasRequiredAtoms")]
        public JsonRequiredAtoms HasRequiredAtoms { get; set; } = new();
    }

    public class JsonFileStatus
    {
        [JsonPropertyName("isCorrupted")]
        public bool IsCorrupted { get; set; }

        [JsonPropertyName("isComplete")]
        public bool IsComplete { get; set; }

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;

        [JsonPropertyName("recommendation")]
        public string Recommendation { get; set; } = string.Empty;
    }

    public class JsonCorruptionReport
    {
        [JsonPropertyName("reportVersion")]
        public string ReportVersion { get; set; } = "1.0";

        [JsonPropertyName("generatedByTool")]
        public string GeneratedByTool { get; set; } = "MovFileIntegrityChecker";

        [JsonPropertyName("evaluationTimeUtc")]
        public DateTime EvaluationTimeUtc { get; set; }

        [JsonPropertyName("evaluationTimeLocal")]
        public DateTime EvaluationTimeLocal { get; set; }

        [JsonPropertyName("fileMetadata")]
        public JsonFileMetadata FileMetadata { get; set; } = new();

        [JsonPropertyName("videoDuration")]
        public JsonVideoDuration? VideoDuration { get; set; }

        [JsonPropertyName("integrityAnalysis")]
        public JsonIntegrityAnalysis IntegrityAnalysis { get; set; } = new();

        [JsonPropertyName("status")]
        public JsonFileStatus Status { get; set; } = new();
    }
}

