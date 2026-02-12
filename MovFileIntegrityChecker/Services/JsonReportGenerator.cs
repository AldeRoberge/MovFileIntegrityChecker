using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MovFileIntegrityChecker.Models;
using static MovFileIntegrityChecker.Utilities.ConsoleHelper;

namespace MovFileIntegrityChecker.Services
{
    public class JsonReportGenerator(string outputDirectory = @"T:\SPT\SP\Mont\Projets\3_PRJ\9-ALEXANDRE_DEMERS-ROBERGE\Fichiers Corrompus")
    {
        private static readonly HashSet<string> ValidAtomTypes = new()
        {
            "ftyp", "moov", "mdat", "free", "skip", "wide", "pnot",
            "mvhd", "trak", "tkhd", "mdia", "mdhd", "hdlr", "minf",
            "vmhd", "smhd", "dinf", "stbl", "stsd", "stts", "stsc",
            "stsz", "stco", "co64", "edts", "elst", "udta", "meta"
        };

        public void CreateReport(FileCheckResult result)
        {
            try
            {
                string filePath = result.FilePath;
                FileInfo fileInfo = new FileInfo(filePath);

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                string baseName = Path.GetFileNameWithoutExtension(filePath);
                string jsonFileName = $"{baseName}_report.json";
                string jsonReportPath = Path.Combine(outputDirectory, jsonFileName);

                DateTime evaluationTime = DateTime.UtcNow;

                var jsonReport = new JsonCorruptionReport
                {
                    ReportVersion = "1.0",
                    GeneratedByTool = "MovFileIntegrityChecker v1.0",
                    EvaluationTimeUtc = evaluationTime,
                    EvaluationTimeLocal = evaluationTime.ToLocalTime(),

                    FileMetadata = new JsonFileMetadata
                    {
                        FileName = fileInfo.Name,
                        FullPath = fileInfo.FullName,
                        FileSizeBytes = fileInfo.Length,
                        FileSizeMB = fileInfo.Length / (1024.0 * 1024.0),
                        CreationTimeUtc = fileInfo.CreationTimeUtc,
                        LastModifiedTimeUtc = fileInfo.LastWriteTimeUtc,
                        LastAccessTimeUtc = fileInfo.LastAccessTimeUtc,
                        FileExtension = fileInfo.Extension,
                        IsReadOnly = fileInfo.IsReadOnly,
                        Attributes = fileInfo.Attributes.ToString()
                    },

                    IntegrityAnalysis = new JsonIntegrityAnalysis
                    {
                        BytesValidated = result.BytesValidated,
                        ValidationPercentage = result.FileSize > 0 ? (result.BytesValidated * 100.0 / result.FileSize) : 0,
                        AtomsFound = result.Atoms.Count,
                        HasStructuralIssues = result.HasIssues,
                        Issues = new List<string>(result.Issues),
                        Atoms = result.Atoms.Select(a => new JsonAtomInfo
                        {
                            Type = a.Type,
                            SizeBytes = a.Size,
                            OffsetBytes = a.Offset,
                            IsComplete = a.IsComplete,
                            IsKnownType = ValidAtomTypes.Contains(a.Type)
                        }).ToList(),
                        HasRequiredAtoms = new JsonRequiredAtoms
                        {
                            Ftyp = result.Atoms.Any(a => a.Type == "ftyp"),
                            Moov = result.Atoms.Any(a => a.Type == "moov"),
                            Mdat = result.Atoms.Any(a => a.Type == "mdat")
                        }
                    },

                    Status = new JsonFileStatus
                    {
                        IsCorrupted = result.HasIssues,
                        IsComplete = !result.HasIssues,
                        Severity = result.HasIssues ? "ERROR" : "OK",
                        Recommendation = result.HasIssues
                            ? "File appears to be corrupted or incomplete. Review in VLC to verify playback."
                            : "File structure appears valid and complete."
                    }
                };

                // Add video duration if available
                if (result.TotalDuration > 0)
                {
                    double missingDuration = result.TotalDuration - result.PlayableDuration;
                    double playablePercent = (result.PlayableDuration / result.TotalDuration) * 100.0;
                    double corruptedPercent = 100.0 - playablePercent;

                    jsonReport.VideoDuration = new JsonVideoDuration
                    {
                        TotalDurationSeconds = result.TotalDuration,
                        TotalDurationFormatted = FormatDuration(result.TotalDuration),
                        PlayableDurationSeconds = result.PlayableDuration,
                        PlayableDurationFormatted = FormatDuration(result.PlayableDuration),
                        MissingDurationSeconds = missingDuration,
                        MissingDurationFormatted = FormatDuration(missingDuration),
                        PlayablePercentage = playablePercent,
                        CorruptedPercentage = corruptedPercent
                    };
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string jsonContent = JsonSerializer.Serialize(jsonReport, options);
                File.WriteAllText(jsonReportPath, jsonContent, Encoding.UTF8);

                WriteSuccess($"✅ JSON report saved: {jsonReportPath}");
            }
            catch (Exception ex)
            {
                WriteWarning($"⚠️ Unable to create JSON report: {ex.Message}");
            }
        }
    }
}

