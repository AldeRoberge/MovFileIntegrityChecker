using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MovFileIntegrityChecker.Models;
using MovFileIntegrityChecker.Services;
using MovFileIntegrityChecker.Utilities;

namespace MovFileIntegrityChecker
{
    public static class MovIntegrityChecker
    {
        // Common MOV/MP4 atom types
        private static readonly HashSet<string> ValidAtomTypes = new()
        {
            "ftyp", "moov", "mdat", "free", "skip", "wide", "pnot",
            "mvhd", "trak", "tkhd", "mdia", "mdhd", "hdlr", "minf",
            "vmhd", "smhd", "dinf", "stbl", "stsd", "stts", "stsc",
            "stsz", "stco", "co64", "edts", "elst", "udta", "meta"
        };

        public class AtomInfo
        {
            public string Type       { get; set; } = string.Empty;
            public long   Size       { get; set; }
            public long   Offset     { get; set; }
            public bool   IsComplete { get; set; }
        }

        // JSON Report Classes
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

        public class JsonRequiredAtoms
        {
            [JsonPropertyName("ftyp")]
            public bool Ftyp { get; set; }

            [JsonPropertyName("moov")]
            public bool Moov { get; set; }

            [JsonPropertyName("mdat")]
            public bool Mdat { get; set; }
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

        public class FileCheckResult
        {
            public string         FilePath         { get; set; } = string.Empty;
            public bool           HasIssues        => Issues.Count > 0;
            public List<string>   Issues           { get; set; } = new();
            public List<AtomInfo> Atoms            { get; set; } = new();
            public long           FileSize         { get; set; }
            public long           BytesValidated   { get; set; }
            public double         TotalDuration    { get; set; }
            public double         PlayableDuration { get; set; }
        }

        private static FileCheckResult CheckFileIntegrity(string filePath)
        {
            var result = new FileCheckResult
            {
                FilePath = filePath,
            };

            if (!File.Exists(filePath))
            {
                result.Issues.Add("File does not exist");
                return result;
            }

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                long fileLength = fs.Length;
                result.FileSize = fileLength;

                if (fileLength < 8)
                {
                    result.Issues.Add("File too small to be a valid MOV (< 8 bytes)");
                    return result;
                }

                // Parse all atoms in the file
                bool hasStructuralErrors = false;
                bool hasIncompleteAtoms = false;
                long position = 0;

                while (position < fileLength)
                {
                    fs.Position = position;

                    // Read atom size (4 bytes, big-endian)
                    byte[] sizeBytes = new byte[4];
                    int bytesRead = fs.Read(sizeBytes, 0, 4);
                    if (bytesRead < 4)
                    {
                        result.Issues.Add($"Incomplete atom header at offset {position:N0}");
                        hasIncompleteAtoms = true;
                        break;
                    }

                    long atomSize = ReadBigEndianUInt32(sizeBytes);

                    // Read atom type (4 bytes)
                    byte[] typeBytes = new byte[4];
                    bytesRead = fs.Read(typeBytes, 0, 4);
                    if (bytesRead < 4)
                    {
                        result.Issues.Add($"Incomplete atom type at offset {position:N0}");
                        hasIncompleteAtoms = true;
                        break;
                    }

                    string atomType = Encoding.ASCII.GetString(typeBytes);

                    // Handle extended size (size == 1)
                    long headerSize = 8;
                    if (atomSize == 1)
                    {
                        byte[] extSizeBytes = new byte[8];
                        bytesRead = fs.Read(extSizeBytes, 0, 8);
                        if (bytesRead < 8)
                        {
                            result.Issues.Add($"Incomplete extended size for atom '{atomType}' at offset {position:N0}");
                            hasIncompleteAtoms = true;
                            break;
                        }

                        atomSize = ReadBigEndianUInt64(extSizeBytes);
                        headerSize = 16;
                    }
                    // Handle size == 0 (atom extends to end of file)
                    else if (atomSize == 0)
                    {
                        atomSize = fileLength - position;
                    }

                    // Validate atom size
                    if (atomSize < headerSize)
                    {
                        result.Issues.Add($"Invalid atom size ({atomSize}) at offset {position:N0} for type '{atomType}'");
                        hasStructuralErrors = true;
                        break;
                    }

                    // Check if atom extends beyond file
                    bool isComplete = (position + atomSize) <= fileLength;

                    var atom = new AtomInfo
                    {
                        Type = atomType,
                        Size = atomSize,
                        Offset = position,
                        IsComplete = isComplete
                    };
                    result.Atoms.Add(atom);

                    if (!isComplete)
                    {
                        long available = fileLength - position;
                        long missing = atomSize - available;
                        result.Issues.Add($"Incomplete atom '{atomType}' at offset {position:N0}: Expected {atomSize:N0} bytes, available {available:N0} bytes, missing {missing:N0} bytes ({(missing * 100.0 / atomSize):F1}%)");
                        hasIncompleteAtoms = true;
                        break;
                    }

                    // Warn about unknown atom types
                    if (!ValidAtomTypes.Contains(atomType) && !IsAsciiPrintable(atomType))
                    {
                        result.Issues.Add($"Unknown/invalid atom type '{atomType}' at offset {position:N0}");
                    }

                    position += atomSize;
                }

                result.BytesValidated = position;

                // Check for required atoms
                bool hasFtyp = result.Atoms.Any(a => a.Type == "ftyp");
                bool hasMoov = result.Atoms.Any(a => a.Type == "moov");
                bool hasMdat = result.Atoms.Any(a => a.Type == "mdat");

                if (!hasFtyp && result.Atoms.Count > 0)
                {
                    result.Issues.Add("Missing 'ftyp' atom (file type header)");
                }

                if (!hasMoov && result.Atoms.Count > 0)
                {
                    result.Issues.Add("Missing 'moov' atom (metadata)");
                }

                if (!hasMdat && result.Atoms.Count > 0)
                {
                    result.Issues.Add("Missing 'mdat' atom (media data)");
                }

                // Validate ftyp is first (if present)
                if (hasFtyp && result.Atoms.Count > 0 && result.Atoms[0].Type != "ftyp")
                {
                    result.Issues.Add($"'ftyp' atom should be first, but found at offset {result.Atoms.First(a => a.Type == "ftyp").Offset:N0}");
                }

                // Check last atom alignment
                if (result.Atoms.Count > 0)
                {
                    var lastAtom = result.Atoms.Last();
                    long declaredEnd = lastAtom.Offset + lastAtom.Size;

                    if (lastAtom.IsComplete && declaredEnd != fileLength)
                    {
                        long gap = fileLength - declaredEnd;
                        result.Issues.Add($"Gap of {gap:N0} bytes after last atom '{lastAtom.Type}' at offset {declaredEnd:N0}");
                    }
                }

                // Final verdict
                if (hasStructuralErrors || hasIncompleteAtoms || result.Atoms.Count == 0)
                {
                    result.Issues.Add("File structure is invalid or incomplete");
                }

                // Get video duration using ffprobe
                result.TotalDuration = GetVideoDuration(filePath);

                // Estimate playable duration based on bytes validated
                if (result.TotalDuration > 0 && result.FileSize > 0)
                {
                    double completionRatio = (double)result.BytesValidated / result.FileSize;
                    result.PlayableDuration = result.TotalDuration * completionRatio;
                }
                else
                {
                    result.PlayableDuration = 0;
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add($"Error reading file: {ex.Message}");
            }

            return result;
        }

        private static void PrintDetailedResult(FileCheckResult result)
        {
            Console.WriteLine($"\n{'='}{new string('=', 80)}");
            Console.WriteLine($"File: {Path.GetFileName(result.FilePath)}");
            Console.WriteLine(new string('=', 80));

            Console.WriteLine($"\n📊 Analysis Summary:");
            Console.WriteLine($"   File Size: {result.FileSize:N0} bytes");
            Console.WriteLine($"   Atoms Found: {result.Atoms.Count}");
            Console.WriteLine($"   Bytes Validated: {result.BytesValidated:N0} / {result.FileSize:N0} ({(result.BytesValidated * 100.0 / Math.Max(1, result.FileSize)):F1}%)");

            // Display duration timeline if available
            if (result.TotalDuration > 0)
            {
                Console.WriteLine($"\n⏱️  Duration Timeline:");
                Console.WriteLine($"   Total Duration: {FormatDuration(result.TotalDuration)}");

                if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
                {
                    Console.WriteLine($"   Playable Duration: {FormatDuration(result.PlayableDuration)}");
                    double playablePercent = (result.PlayableDuration / result.TotalDuration) * 100.0;

                    // Create visual timeline bar (50 chars wide)
                    int barWidth = 50;
                    int greenWidth = (int)(barWidth * playablePercent / 100.0);
                    int redWidth = barWidth - greenWidth;

                    string greenBar = new string('█', greenWidth);
                    string redBar = new string('█', redWidth);

                    Console.Write($"   ");
                    WriteSuccess($"{greenBar}");
                    Console.Write($"");
                    WriteError($"{redBar}\n");
                    Console.WriteLine($"   |");
                    Console.WriteLine($"   {FormatDuration(0)}          {FormatDuration(result.PlayableDuration)} (break)          {FormatDuration(result.TotalDuration)}");
                    Console.WriteLine($"   Start           Missing: {FormatDuration(result.TotalDuration - result.PlayableDuration)}           End");
                }
                else
                {
                    Console.WriteLine($"   Status: Complete playback expected");
                    int barWidth = 50;
                    string greenBar = new string('█', barWidth);
                    Console.Write($"   ");
                    WriteSuccess($"{greenBar}\n");
                    Console.WriteLine($"   |");
                    Console.WriteLine($"   {FormatDuration(0)}                                      {FormatDuration(result.TotalDuration)}");
                    Console.WriteLine($"   Start                                   End");
                }
            }

            if (result.Atoms.Count > 0)
            {
                Console.WriteLine($"\n📦 Atom Structure:");
                foreach (var atom in result.Atoms)
                {
                    string status = atom.IsComplete ? "✅" : "❌";
                    string knownType = ValidAtomTypes.Contains(atom.Type) ? "" : " (unknown)";
                    Console.WriteLine($"   {status} [{atom.Type}]{knownType} - Size: {atom.Size:N0} bytes, Offset: {atom.Offset:N0}");
                }

                // Check for required atoms
                bool hasFtyp = result.Atoms.Any(a => a.Type == "ftyp");
                bool hasMoov = result.Atoms.Any(a => a.Type == "moov");
                bool hasMdat = result.Atoms.Any(a => a.Type == "mdat");

                Console.WriteLine($"\n🔍 Key Atoms:");
                Console.WriteLine($"   ftyp (file type): {(hasFtyp ? "✅ Found" : "❌ Missing")}");
                Console.WriteLine($"   moov (metadata): {(hasMoov ? "✅ Found" : "❌ Missing")}");
                Console.WriteLine($"   mdat (media data): {(hasMdat ? "✅ Found" : "❌ Missing")}");
            }

            if (result.Issues.Count > 0)
            {
                Console.WriteLine($"\n⚠️  Issues Found ({result.Issues.Count}):");
                foreach (var issue in result.Issues)
                {
                    WriteWarning($"   • {issue}");
                }
            }

            if (result.HasIssues)
            {
                WriteError("\n❌ File Status: CORRUPTED or INCOMPLETE");
            }
            else
            {
                WriteSuccess("\n✅ File Status: VALID and COMPLETE");
            }
        }

        private static uint ReadBigEndianUInt32(byte[] data)
        {
            return ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
        }

        private static long ReadBigEndianUInt64(byte[] data)
        {
            return ((long)data[0] << 56) | ((long)data[1] << 48) | ((long)data[2] << 40) | ((long)data[3] << 32) |
                   ((long)data[4] << 24) | ((long)data[5] << 16) | ((long)data[6] << 8) | data[7];
        }

        private static bool IsAsciiPrintable(string str)
        {
            return str.All(c => c >= 32 && c <= 126);
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds <= 0) return "00:00:00";

            int hours = (int)(seconds / 3600);
            int minutes = (int)((seconds % 3600) / 60);
            int secs = (int)(seconds % 60);

            return $"{hours:D2}:{minutes:D2}:{secs:D2}";
        }

        // --- Added helpers for extracting a random frame using ffprobe/ffmpeg ---
        private static double GetVideoDuration(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p == null) return 0;

                // Wait a bit for ffprobe to produce output
                string output = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(3000);

                if (!string.IsNullOrWhiteSpace(output) && double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var duration))
                    return duration;

                // If ffprobe failed, try to parse duration from stderr as a fallback (sometimes ffprobe prints to stderr)
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    var digits = new string(stderr.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
                    if (double.TryParse(digits.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dur2))
                        return dur2;
                }
            }
            catch
            {
                // ignore and return 0
            }

            return 0;
        }

        // Tries multiple extraction strategies and timestamps to increase the chance of success
        public static string? GetRandomFrameBase64(string filePath)
        {
            try
            {
                double duration = GetVideoDuration(filePath);
                var rnd = new Random();

                // Candidate timestamps (seconds)
                var timestamps = new List<double> { 0 };
                if (duration > 0)
                {
                    timestamps.Add(Math.Min(Math.Max(0.1, duration / 2.0), Math.Max(0.1, duration - 0.5)));
                    if (duration > 2.0) timestamps.Add(Math.Max(0.5, duration - 1.0));
                }

                // Add a random timestamp if duration is known
                if (duration > 0.5)
                {
                    double max = Math.Max(0.1, duration - 0.5);
                    timestamps.Add(rnd.NextDouble() * max);
                }

                // Ensure unique and reasonable timestamps
                timestamps = timestamps.Distinct().Select(t => Math.Max(0, t)).Take(5).ToList();

                // Try extraction strategies for each timestamp
                foreach (var ts in timestamps)
                {
                    // Strategy 1: write to a temp file using fast seek (-ss before -i)
                    var tmp = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ".png");
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-ss {ts.ToString(CultureInfo.InvariantCulture)} -i \"{filePath}\" -frames:v 1 -vf scale=640:-1 -y -hide_banner -loglevel error \"{tmp}\"",
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var p = Process.Start(psi);
                        if (p != null)
                        {
                            // wait longer for more complex files
                            if (!p.WaitForExit(10000))
                            {
                                try
                                {
                                    p.Kill(true);
                                }
                                catch
                                {
                                }
                            }
                        }

                        if (File.Exists(tmp))
                        {
                            var fi = new FileInfo(tmp);
                            if (fi.Length > 100) // small sanity size
                            {
                                byte[] data = File.ReadAllBytes(tmp);
                                try
                                {
                                    File.Delete(tmp);
                                }
                                catch
                                {
                                }

                                return Convert.ToBase64String(data);
                            }

                            try
                            {
                                File.Delete(tmp);
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch
                    {
                        try
                        {
                            if (File.Exists(tmp)) File.Delete(tmp);
                        }
                        catch
                        {
                        }
                    }

                    // Strategy 2: accurate seek (position after -i)
                    try
                    {
                        var tmp2 = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ".png");
                        var psi2 = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-i \"{filePath}\" -ss {ts.ToString(CultureInfo.InvariantCulture)} -frames:v 1 -vf scale=640:-1 -y -hide_banner -loglevel error \"{tmp2}\"",
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var p2 = Process.Start(psi2);
                        if (p2 != null)
                        {
                            if (!p2.WaitForExit(12000))
                            {
                                try
                                {
                                    p2.Kill(true);
                                }
                                catch
                                {
                                }
                            }
                        }

                        if (File.Exists(tmp2))
                        {
                            var fi2 = new FileInfo(tmp2);
                            if (fi2.Length > 100)
                            {
                                byte[] data = File.ReadAllBytes(tmp2);
                                try
                                {
                                    File.Delete(tmp2);
                                }
                                catch
                                {
                                }

                                return Convert.ToBase64String(data);
                            }

                            try
                            {
                                File.Delete(tmp2);
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch
                    {
                        // ignore and continue
                    }

                    // Strategy 3: try piping to stdout (older approach) as a last resort
                    try
                    {
                        var psi3 = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-ss {ts.ToString(CultureInfo.InvariantCulture)} -i \"{filePath}\" -frames:v 1 -vf scale=640:-1 -f image2 -vcodec png pipe:1 -hide_banner -loglevel error",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var proc = Process.Start(psi3);
                        if (proc != null)
                        {
                            using var ms = new MemoryStream();
                            // copy but with a timeout guard
                            var copyTask = proc.StandardOutput.BaseStream.CopyToAsync(ms);
                            if (!copyTask.Wait(7000))
                            {
                                try
                                {
                                    proc.Kill(true);
                                }
                                catch
                                {
                                }
                            }
                            else
                            {
                                proc.WaitForExit(2000);
                            }

                            if (ms.Length > 100)
                                return Convert.ToBase64String(ms.ToArray());
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                // All attempts failed
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static void CreateJsonReport(FileCheckResult result)
        {
            try
            {
                string filePath = result.FilePath;
                FileInfo fileInfo = new FileInfo(filePath);

                // Create JSON report directory if it doesn't exist
                string jsonReportDir = @"T:\SPT\SP\Mont\Projets\3_PRJ\9-ALEXANDRE_DEMERS-ROBERGE\Fichiers Corrompus";

                if (!Directory.Exists(jsonReportDir))
                {
                    Directory.CreateDirectory(jsonReportDir);
                }

                string baseName = Path.GetFileNameWithoutExtension(filePath);
                string jsonFileName = $"{baseName}_report.json";
                string jsonReportPath = Path.Combine(jsonReportDir, jsonFileName);

                DateTime evaluationTime = DateTime.UtcNow;

                // Build the comprehensive JSON report
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

                // Serialize to JSON with pretty formatting
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

        public static void CreateErrorReport(FileCheckResult result)
        {
            try
            {
                string filePath = result.FilePath;
                string folder = Path.GetDirectoryName(filePath)!;
                string baseName = Path.GetFileNameWithoutExtension(filePath);
                string reportPath = Path.Combine(folder, $"{baseName}-Incomplet.html");

                var sb = new StringBuilder();

                // Try to get a random frame as base64 (may return null)
                string? frameBase64 = GetRandomFrameBase64(filePath);

                // HTML Header with embedded CSS
                sb.AppendLine("<!DOCTYPE html>");
                sb.AppendLine("<html lang=\"fr\">");
                sb.AppendLine("<head>");
                sb.AppendLine("    <meta charset=\"UTF-8\">");
                sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
                sb.AppendLine($"    <title>Rapport d'Intégrité - {Path.GetFileName(filePath)}</title>");
                sb.AppendLine("    <style>");
                sb.AppendLine("        * { margin: 0; padding: 0; box-sizing: border-box; }");
                sb.AppendLine("        body {");
                sb.AppendLine("            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;");
                sb.AppendLine("            background: #000000;");
                sb.AppendLine("            min-height: 100vh;");
                sb.AppendLine("            padding: 40px 20px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .container {");
                sb.AppendLine("            max-width: 1000px;");
                sb.AppendLine("            margin: 0 auto;");
                sb.AppendLine("            background: #1a1a1a;");
                sb.AppendLine("            border-radius: 16px;");
                sb.AppendLine("            box-shadow: 0 20px 60px rgba(0,0,0,0.5);");
                sb.AppendLine("            overflow: hidden;");
                sb.AppendLine("            border: 1px solid #333;");
                sb.AppendLine("        }");
                sb.AppendLine("        .header {");
                sb.AppendLine("            background: #111111;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            padding: 30px 40px;");
                sb.AppendLine("            text-align: center;");
                sb.AppendLine("            border-bottom: 2px solid #ff8c00;");
                sb.AppendLine("        }");
                sb.AppendLine("        .header h1 {");
                sb.AppendLine("            font-size: 1.8em;");
                sb.AppendLine("            margin-bottom: 15px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .header .subtitle {");
                sb.AppendLine("            font-size: 1em;");
                sb.AppendLine("            color: #ccc;");
                sb.AppendLine("            margin-bottom: 15px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .header .warning {");
                sb.AppendLine("            font-size: 0.95em;");
                sb.AppendLine("            color: #ff8c00;");
                sb.AppendLine("            line-height: 1.5;");
                sb.AppendLine("            margin-top: 15px;");
                sb.AppendLine("            padding-top: 15px;");
                sb.AppendLine("            border-top: 1px solid #333;");
                sb.AppendLine("        }");
                sb.AppendLine("        .header .warning strong {");
                sb.AppendLine("            display: block;");
                sb.AppendLine("            margin-bottom: 8px;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("        }");
                sb.AppendLine("        .content {");
                sb.AppendLine("            padding: 40px;");
                sb.AppendLine("            background: #1a1a1a;");
                sb.AppendLine("        }");
                sb.AppendLine("        .section {");
                sb.AppendLine("            margin-bottom: 30px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .section-title {");
                sb.AppendLine("            font-size: 1.5em;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            margin-bottom: 15px;");
                sb.AppendLine("            padding-bottom: 10px;");
                sb.AppendLine("            border-bottom: 2px solid #333;");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("            align-items: center;");
                sb.AppendLine("            gap: 10px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .info-grid {");
                sb.AppendLine("            display: grid;");
                sb.AppendLine("            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));");
                sb.AppendLine("            gap: 20px;");
                sb.AppendLine("            margin-top: 20px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .info-card {");
                sb.AppendLine("            background: #222222;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            padding: 20px;");
                sb.AppendLine("            border-radius: 12px;");
                sb.AppendLine("            box-shadow: 0 4px 15px rgba(0,0,0,0.5);");
                sb.AppendLine("            border: 1px solid #333;");
                sb.AppendLine("            border-left: 3px solid #ff8c00;");
                sb.AppendLine("        }");
                sb.AppendLine("        .info-card .label {");
                sb.AppendLine("            font-size: 0.9em;");
                sb.AppendLine("            color: #999;");
                sb.AppendLine("            margin-bottom: 5px;");
                sb.AppendLine("            text-transform: uppercase;");
                sb.AppendLine("            letter-spacing: 0.5px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .info-card .value {");
                sb.AppendLine("            font-size: 1.5em;");
                sb.AppendLine("            font-weight: bold;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("        }");
                sb.AppendLine("        .issue-list {");
                sb.AppendLine("            background: #2a1a1a;");
                sb.AppendLine("            border-left: 4px solid #ff8c00;");
                sb.AppendLine("            padding: 20px;");
                sb.AppendLine("            border-radius: 8px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .issue-item {");
                sb.AppendLine("            padding: 10px 0;");
                sb.AppendLine("            border-bottom: 1px solid #333;");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("            align-items: start;");
                sb.AppendLine("            gap: 10px;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("        }");
                sb.AppendLine("        .issue-item:last-child {");
                sb.AppendLine("            border-bottom: none;");
                sb.AppendLine("        }");
                sb.AppendLine("        .issue-icon {");
                sb.AppendLine("            color: #ff8c00;");
                sb.AppendLine("            font-weight: bold;");
                sb.AppendLine("            flex-shrink: 0;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table {");
                sb.AppendLine("            width: 100%;");
                sb.AppendLine("            border-collapse: collapse;");
                sb.AppendLine("            margin-top: 20px;");
                sb.AppendLine("            box-shadow: 0 2px 10px rgba(0,0,0,0.5);");
                sb.AppendLine("            border-radius: 8px;");
                sb.AppendLine("            overflow: hidden;");
                sb.AppendLine("            border: 1px solid #333;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table thead {");
                sb.AppendLine("            background: #111111;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            border-bottom: 2px solid #ff8c00;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table th {");
                sb.AppendLine("            padding: 15px;");
                sb.AppendLine("            text-align: left;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table td {");
                sb.AppendLine("            padding: 12px 15px;");
                sb.AppendLine("            border-bottom: 1px solid #333;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table tbody tr {");
                sb.AppendLine("            background: #1a1a1a;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table tbody tr:hover {");
                sb.AppendLine("            background: #252525;");
                sb.AppendLine("        }");
                sb.AppendLine("        .status-badge {");
                sb.AppendLine("            display: inline-block;");
                sb.AppendLine("            padding: 5px 12px;");
                sb.AppendLine("            border-radius: 20px;");
                sb.AppendLine("            font-size: 0.85em;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("        }");
                sb.AppendLine("        .status-complete {");
                sb.AppendLine("            background: #1a3a1a;");
                sb.AppendLine("            color: #90ee90;");
                sb.AppendLine("            border: 1px solid #90ee90;");
                sb.AppendLine("        }");
                sb.AppendLine("        .status-incomplete {");
                sb.AppendLine("            background: #3a1a1a;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            border: 1px solid #ff8c00;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-type {");
                sb.AppendLine("            font-family: 'Courier New', monospace;");
                sb.AppendLine("            font-weight: bold;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            font-size: 1.1em;");
                sb.AppendLine("        }");
                sb.AppendLine("        .footer {");
                sb.AppendLine("            background: #111111;");
                sb.AppendLine("            padding: 20px 40px;");
                sb.AppendLine("            text-align: center;");
                sb.AppendLine("            color: #888;");
                sb.AppendLine("            font-size: 0.9em;");
                sb.AppendLine("            border-top: 1px solid #333;");
                sb.AppendLine("        }");
                sb.AppendLine("        .progress-bar {");
                sb.AppendLine("            width: 100%;");
                sb.AppendLine("            height: 30px;");
                sb.AppendLine("            background: #2a2a2a;");
                sb.AppendLine("            border-radius: 15px;");
                sb.AppendLine("            overflow: hidden;");
                sb.AppendLine("            margin-top: 10px;");
                sb.AppendLine("            border: 1px solid #444;");
                sb.AppendLine("        }");
                sb.AppendLine("        .progress-fill {");
                sb.AppendLine("            height: 100%;");
                sb.AppendLine("            background: linear-gradient(90deg, #ff8c00 0%, #ffa500 100%);");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("            align-items: center;");
                sb.AppendLine("            justify-content: center;");
                sb.AppendLine("            color: #000000;");
                sb.AppendLine("            font-weight: bold;");
                sb.AppendLine("            font-size: 0.85em;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-container {");
                sb.AppendLine("            margin-top: 30px;");
                sb.AppendLine("            padding: 20px;");
                sb.AppendLine("            background: #222222;");
                sb.AppendLine("            border-radius: 12px;");
                sb.AppendLine("            border: 1px solid #333;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-title {");
                sb.AppendLine("            font-size: 1.2em;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            margin-bottom: 15px;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-bar {");
                sb.AppendLine("            width: 100%;");
                sb.AppendLine("            height: 40px;");
                sb.AppendLine("            background: #2a2a2a;");
                sb.AppendLine("            border-radius: 8px;");
                sb.AppendLine("            overflow: hidden;");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("            border: 2px solid #444;");
                sb.AppendLine("            margin: 15px 0;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-good {");
                sb.AppendLine("            background: linear-gradient(90deg, #00aa00 0%, #00cc00 100%);");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("            align-items: center;");
                sb.AppendLine("            justify-content: center;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            font-weight: bold;");
                sb.AppendLine("            font-size: 0.9em;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-bad {");
                sb.AppendLine("            background: linear-gradient(90deg, #cc0000 0%, #aa0000 100%);");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("            align-items: center;");
                sb.AppendLine("            justify-content: center;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            font-weight: bold;");
                sb.AppendLine("            font-size: 0.9em;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-labels {");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("            justify-content: space-between;");
                sb.AppendLine("            margin-top: 10px;");
                sb.AppendLine("            color: #ccc;");
                sb.AppendLine("            font-size: 0.9em;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-label {");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("            flex-direction: column;");
                sb.AppendLine("            align-items: center;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-label.start { align-items: flex-start; }");
                sb.AppendLine("        .timeline-label.end { align-items: flex-end; }");
                sb.AppendLine("        .timeline-label .time {");
                sb.AppendLine("            font-weight: bold;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            font-size: 1.1em;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-info {");
                sb.AppendLine("            margin-top: 15px;");
                sb.AppendLine("            padding: 15px;");
                sb.AppendLine("            background: #1a1a1a;");
                sb.AppendLine("            border-radius: 8px;");
                sb.AppendLine("            border-left: 3px solid #ff8c00;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-info div {");
                sb.AppendLine("            color: #ccc;");
                sb.AppendLine("            margin: 5px 0;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-info strong {");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("        }");
                sb.AppendLine("        .preview img { max-width: 100%; border-radius: 8px; display: block; margin: 15px auto; }");
                sb.AppendLine("    </style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");
                sb.AppendLine("    <div class=\"container\">");

                // Header
                sb.AppendLine("        <div class=\"header\">");
                sb.AppendLine("            <h1>⚠️ Rapport d'Intégrité Fichier</h1>");
                sb.AppendLine($"            <div class=\"subtitle\">{System.Security.SecurityElement.Escape(Path.GetFileName(filePath))}</div>");
                sb.AppendLine("            <div class=\"warning\">");
                sb.AppendLine("                <strong>Avertissement - Fichier potentiellement incomplet</strong>");
                sb.AppendLine("                Ce fichier a été automatiquement détecté comme étant potentiellement incomplet. ");
                sb.AppendLine("                Veuillez le visionner jusqu'à la fin dans VLC pour vérifier s'il se lit correctement.");
                sb.AppendLine("            </div>");
                sb.AppendLine("        </div>");

                // Content
                sb.AppendLine("        <div class=\"content\">");

                // Embed preview if available
                if (!string.IsNullOrEmpty(frameBase64))
                {
                    sb.AppendLine("            <div class=\"section\">");
                    sb.AppendLine("                <div class=\"section-title\">Preview</div>");
                    sb.AppendLine("                <div class=\"preview\">");
                    sb.AppendLine($"                    <img src=\"data:image/png;base64,{frameBase64}\" alt=\"Preview\">");
                    sb.AppendLine("                </div>");
                    sb.AppendLine("            </div>");
                }
                else
                {
                    sb.AppendLine("            <div class=\"section\">");
                    sb.AppendLine("                <div class=\"section-title\">Preview</div>");
                    sb.AppendLine("                <div class=\"issue-list\">");
                    sb.AppendLine("                    <div class=\"issue-item\">");
                    sb.AppendLine("                        <span class=\"issue-icon\">ℹ️</span>");
                    sb.AppendLine("                        <span>Preview not available (ffmpeg/ffprobe not found or frame extraction failed).</span>");
                    sb.AppendLine("                    </div>");
                    sb.AppendLine("                </div>");
                    sb.AppendLine("            </div>");
                }

                sb.AppendLine("            <div class=\"section\">");
                sb.AppendLine("                <div class=\"section-title\">📊 Résumé Technique</div>");
                sb.AppendLine("                <div class=\"info-grid\">");
                sb.AppendLine("                    <div class=\"info-card\">");
                sb.AppendLine("                        <div class=\"label\">Nom du fichier</div>");
                sb.AppendLine($"                        <div class=\"value\">{System.Security.SecurityElement.Escape(Path.GetFileName(filePath))}</div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                    <div class=\"info-card\">");
                sb.AppendLine("                        <div class=\"label\">Taille du fichier</div>");
                sb.AppendLine($"                        <div class=\"value\">{result.FileSize:N0} octets</div>");
                sb.AppendLine($"                        <div class=\"label\">({result.FileSize / (1024.0 * 1024.0):F2} MB)</div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                    <div class=\"info-card\">");
                sb.AppendLine("                        <div class=\"label\">Octets validés</div>");
                sb.AppendLine($"                        <div class=\"value\">{result.BytesValidated:N0}</div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                    <div class=\"info-card\">");
                sb.AppendLine("                        <div class=\"label\">Atoms détectés</div>");
                sb.AppendLine($"                        <div class=\"value\">{result.Atoms.Count}</div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                </div>");

                double validationPercent = result.FileSize > 0 ? (result.BytesValidated * 100.0 / result.FileSize) : 0;
                sb.AppendLine("                <div class=\"progress-bar\">");
                sb.AppendLine($"                    <div class=\"progress-fill\" style=\"width: {validationPercent.ToString("F1", CultureInfo.InvariantCulture)}%\">\n                        {validationPercent:F1}% validé\n                    </div>");
                sb.AppendLine("                </div>");

                // Add duration timeline if available
                if (result.TotalDuration > 0)
                {
                    sb.AppendLine("                <div class=\"timeline-container\">");
                    sb.AppendLine("                    <div class=\"timeline-title\">⏱️ Chronologie de la Vidéo</div>");

                    double playablePercent = result.TotalDuration > 0 ? (result.PlayableDuration / result.TotalDuration) * 100.0 : 0;
                    double brokenPercent = 100.0 - playablePercent;

                    sb.AppendLine("                    <div class=\"timeline-bar\">");
                    if (playablePercent > 0)
                    {
                        sb.AppendLine($"                        <div class=\"timeline-good\" style=\"width: {playablePercent.ToString("F1", CultureInfo.InvariantCulture)}%\">");
                        sb.AppendLine($"                            ✓ Lecture OK");
                        sb.AppendLine("                        </div>");
                    }

                    if (brokenPercent > 0)
                    {
                        sb.AppendLine($"                        <div class=\"timeline-bad\" style=\"width: {brokenPercent.ToString("F1", CultureInfo.InvariantCulture)}%\">");
                        sb.AppendLine($"                            ✗ Corrompu");
                        sb.AppendLine("                        </div>");
                    }

                    sb.AppendLine("                    </div>");

                    sb.AppendLine("                    <div class=\"timeline-labels\">");
                    sb.AppendLine("                        <div class=\"timeline-label start\">");
                    sb.AppendLine("                            <div class=\"time\">00:00:00</div>");
                    sb.AppendLine("                            <div>Début</div>");
                    sb.AppendLine("                        </div>");

                    if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
                    {
                        sb.AppendLine("                        <div class=\"timeline-label\">");
                        sb.AppendLine($"                            <div class=\"time\">{System.Security.SecurityElement.Escape(FormatDuration(result.PlayableDuration))}</div>");
                        sb.AppendLine("                            <div style=\"color: #ff8c00;\">⚠️ Point de rupture</div>");
                        sb.AppendLine("                        </div>");
                    }

                    sb.AppendLine("                        <div class=\"timeline-label end\">");
                    sb.AppendLine($"                            <div class=\"time\">{System.Security.SecurityElement.Escape(FormatDuration(result.TotalDuration))}</div>");
                    sb.AppendLine("                            <div>Fin</div>");
                    sb.AppendLine("                        </div>");
                    sb.AppendLine("                    </div>");

                    sb.AppendLine("                    <div class=\"timeline-info\">");
                    sb.AppendLine($"                        <div><strong>Durée totale:</strong> {System.Security.SecurityElement.Escape(FormatDuration(result.TotalDuration))}</div>");
                    sb.AppendLine($"                        <div><strong>Durée lisible:</strong> {System.Security.SecurityElement.Escape(FormatDuration(result.PlayableDuration))} ({playablePercent:F1}%)</div>");
                    if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
                    {
                        double missingDuration = result.TotalDuration - result.PlayableDuration;
                        sb.AppendLine($"                        <div style=\"color: #ff8c00;\"><strong>Durée manquante:</strong> {System.Security.SecurityElement.Escape(FormatDuration(missingDuration))} ({brokenPercent:F1}%)</div>");
                    }

                    sb.AppendLine("                    </div>");
                    sb.AppendLine("                </div>");
                }

                sb.AppendLine("            </div>");

                if (result.Issues.Count > 0)
                {
                    sb.AppendLine("            <div class=\"section\">\n                <div class=\"section-title\">❌ Problèmes Détectés</div>\n                <div class=\"issue-list\">\n");
                    foreach (var issue in result.Issues)
                    {
                        sb.AppendLine("                    <div class=\"issue-item\">\n                        <span class=\"issue-icon\">❌</span>\n                        <span>" + System.Security.SecurityElement.Escape(issue) + "</span>\n                    </div>");
                    }

                    sb.AppendLine("                </div>\n            </div>");
                }

                if (result.Atoms.Count > 0)
                {
                    sb.AppendLine("            <div class=\"section\">\n                <div class=\"section-title\">📦 Structure des Atoms</div>\n                <table class=\"atom-table\">\n                    <thead>\n                        <tr>\n                            <th>Type</th>\n                            <th>Taille</th>\n                            <th>Offset</th>\n                            <th>Statut</th>\n                        </tr>\n                    </thead>\n                    <tbody>");
                    foreach (var atom in result.Atoms)
                    {
                        string statusClass = atom.IsComplete ? "status-complete" : "status-incomplete";
                        string statusText = atom.IsComplete ? "✅ Complet" : "❌ Incomplet";
                        sb.AppendLine("                        <tr>\n                            <td><span class=\"atom-type\">" + System.Security.SecurityElement.Escape(atom.Type) + "</span></td>\n                            <td>" + atom.Size.ToString("N0") + " octets</td>\n                            <td>" + atom.Offset.ToString("N0") + "</td>\n                            <td><span class=\"status-badge " + statusClass + "\">" + statusText + "</span></td>\n                        </tr>");
                    }

                    sb.AppendLine("                    </tbody>\n                </table>\n            </div>");
                }

                sb.AppendLine("        </div>");

                // Footer
                sb.AppendLine("        <div class=\"footer\">\n            Généré automatiquement le " + DateTime.Now.ToString("yyyy-MM-dd") + " à " + DateTime.Now.ToString("HH:mm:ss") + "<br>\n            Outil : MovIntegrityChecker (rapport automatique)\n        </div>");

                sb.AppendLine("    </div>");
                sb.AppendLine("</body>");
                sb.AppendLine("</html>");

                File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                WriteWarning($"Impossible d'écrire le rapport d'erreur : {ex.Message}");
            }
        }

        private static void WriteWarning(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
        }

        private static void WriteSuccess(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
        }

        private static void WriteError(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
        }

        private static void WriteInfo(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
        }

        private static void DeleteEmptyDirectories(string rootPath, bool recursive)
        {
            try
            {
                var directories = Directory.GetDirectories(
                    rootPath,
                    "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                int deletedCount = 0;

                // Sort by descending path length to delete deep folders first
                foreach (var dir in directories.OrderByDescending(d => d.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                        deletedCount++;
                        WriteSuccess($"Deleted empty folder: {dir}");
                    }
                }

                // Check root folder itself
                if (!Directory.EnumerateFileSystemEntries(rootPath).Any())
                {
                    Directory.Delete(rootPath);
                    deletedCount++;
                    WriteSuccess($"Deleted empty root folder: {rootPath}");
                }

                if (deletedCount > 0)
                    WriteInfo($"\n✅ {deletedCount} empty folder(s) deleted.");
                else
                    WriteInfo("\nNo empty folders found.");
            }
            catch (Exception ex)
            {
                WriteError($"Error while deleting empty folders: {ex.Message}");
            }
        }

        private static void ShowMainMenu()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║        MOV File Integrity Checker - Analysis Mode             ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("Please select an analysis mode:");
            Console.WriteLine();
            Console.WriteLine("  1. Per-File Analysis     - Analyze individual video files");
            Console.WriteLine("  2. Global Analysis       - Generate global report from JSON files");
            Console.WriteLine("  3. Both                  - Run per-file analysis then global report");
            Console.WriteLine("  4. Exit");
            Console.WriteLine();
            Console.Write("Enter your choice (1-4): ");

            string? choice = Console.ReadLine();
            Console.WriteLine();

            switch (choice?.Trim())
            {
                case "1":
                    RunPerFileAnalysisInteractive();
                    break;
                case "2":
                    RunGlobalAnalysis();
                    break;
                case "3":
                    RunPerFileAnalysisInteractive();
                    Console.WriteLine("\n" + new string('=', 80));
                    Console.WriteLine("Starting Global Analysis...");
                    Console.WriteLine(new string('=', 80) + "\n");
                    RunGlobalAnalysis();
                    break;
                case "4":
                    Console.WriteLine("Exiting...");
                    return;
                default:
                    WriteError("Invalid choice. Please run the program again.");
                    break;
            }
        }

        private static void RunPerFileAnalysisInteractive()
        {
            Console.Write("Enter the path to a file or folder: ");
            string? path = Console.ReadLine()?.Trim().Trim('"');

            if (string.IsNullOrEmpty(path))
            {
                WriteError("No path provided.");
                return;
            }

            Console.Write("Recursive search? (y/n, default: n): ");
            bool recursive = Console.ReadLine()?.Trim().ToLower() == "y";

            Console.Write("Delete empty folders? (y/n, default: n): ");
            bool deleteEmpty = Console.ReadLine()?.Trim().ToLower() == "y";

            Console.WriteLine();

            // Run the analysis
            RunPerFileAnalysis(new[] { path }, recursive, summaryOnly: false, deleteEmpty);
        }

        private static void RunPerFileAnalysis(string[] paths, bool recursive, bool summaryOnly, bool deleteEmpty)
        {
            var results = new List<FileCheckResult>();

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    // Single file
                    WriteInfo($"\nChecking file: {path}\n");
                    var result = CheckFileIntegrity(path);
                    results.Add(result);

                    // Always create JSON report
                    CreateJsonReport(result);

                    // Create HTML report only for corrupted files
                    if (result.HasIssues)
                    {
                        CreateErrorReport(result);
                    }

                    if (!summaryOnly)
                        PrintDetailedResult(result);
                }
                else if (Directory.Exists(path))
                {
                    // Directory
                    WriteInfo($"\nChecking folder: {path}");
                    WriteInfo($"Recursive: {(recursive ? "Yes" : "No")}\n");

                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var extensions = new[] { "*.mov", "*.mp4", "*.m4v", "*.m4a" };

                    var files = extensions
                        .SelectMany(ext => Directory.GetFiles(path, ext, searchOption))
                        .OrderBy(f => f)
                        .ToList();

                    if (files.Count == 0)
                    {
                        WriteWarning($"No MOV/MP4 files found in: {path}");
                        continue;
                    }

                    WriteInfo($"Found {files.Count} file(s) to check...\n");

                    int current = 0;
                    foreach (var file in files)
                    {
                        current++;

                        if (summaryOnly)
                            Console.Write($"\rProcessing: {current}/{files.Count} - {Path.GetFileName(file)}".PadRight(80));
                        else
                            WriteInfo($"[{current}/{files.Count}] Checking: {Path.GetFileName(file)}");

                        var result = CheckFileIntegrity(file);
                        results.Add(result);

                        // Always create JSON report
                        CreateJsonReport(result);

                        // Create HTML report only for corrupted files
                        if (result.HasIssues)
                        {
                            CreateErrorReport(result);
                        }

                        if (!summaryOnly)
                            PrintDetailedResult(result);
                    }

                    if (summaryOnly)
                        Console.WriteLine("\n");

                    // Delete empty folders if requested
                    if (deleteEmpty)
                    {
                        DeleteEmptyDirectories(path, recursive);
                    }
                }
                else
                {
                    WriteError($"Path not found: {path}");
                    Environment.ExitCode = 1;
                }
            }

            // Print summary
            PrintSummary(results, summaryOnly);
        }

        private static void PrintSummary(List<FileCheckResult> results, bool summaryOnly)
        {
            Console.WriteLine($"\n{new string('=', 80)}");
            Console.WriteLine("SUMMARY");
            Console.WriteLine(new string('=', 80));

            int totalFiles = results.Count;
            int validFiles = results.Count(r => !r.HasIssues);
            int corruptedFiles = results.Count(r => r.HasIssues);
            long totalSize = results.Sum(r => r.FileSize);

            Console.WriteLine($"\nTotal Files Checked: {totalFiles}");
            WriteSuccess($"Valid Files: {validFiles} ({(validFiles * 100.0 / Math.Max(1, totalFiles)):F1}%)");
            WriteError($"Corrupted/Incomplete Files: {corruptedFiles} ({(corruptedFiles * 100.0 / Math.Max(1, totalFiles)):F1}%)");
            Console.WriteLine($"Total Size: {totalSize:N0} bytes ({totalSize / (1024.0 * 1024.0):F2} MB)");

            if (corruptedFiles > 0)
            {
                Console.WriteLine($"\n❌ Corrupted/Incomplete Files:");
                foreach (var result in results.Where(r => r.HasIssues))
                {
                    WriteError($"   • {Path.GetFileName(result.FilePath)}");
                    if (!summaryOnly && result.Issues.Count > 0)
                    {
                        foreach (var issue in result.Issues.Take(3))
                            Console.WriteLine($"      - {issue}");
                        if (result.Issues.Count > 3)
                            Console.WriteLine($"      ... and {result.Issues.Count - 3} more issue(s)");
                    }
                }
            }

            Environment.ExitCode = corruptedFiles > 0 ? 1 : 0;
        }

        public static void RunGlobalAnalysis()
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║              Global Analysis Mode                              ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Ask for the directory containing JSON reports
            Console.Write("Enter the directory containing JSON reports\n(default: T:\\SPT\\SP\\Mont\\Projets\\3_PRJ\\9-ALEXANDRE_DEMERS-ROBERGE\\Fichiers Corrompus): ");
            string? jsonDir = Console.ReadLine()?.Trim().Trim('"');

            if (string.IsNullOrEmpty(jsonDir))
            {
                jsonDir = @"T:\SPT\SP\Mont\Projets\3_PRJ\9-ALEXANDRE_DEMERS-ROBERGE\Fichiers Corrompus";
            }

            if (!Directory.Exists(jsonDir))
            {
                WriteError($"Directory not found: {jsonDir}");
                return;
            }

            WriteInfo($"\nSearching for JSON reports in: {jsonDir}\n");

            // Find all JSON report files
            var jsonFiles = Directory.GetFiles(jsonDir, "*_report.json", SearchOption.TopDirectoryOnly);

            if (jsonFiles.Length == 0)
            {
                WriteWarning("No JSON report files found. Please run per-file analysis first to generate reports.");
                return;
            }

            WriteInfo($"Found {jsonFiles.Length} JSON report(s). Loading data...\n");

            // Load all reports
            var reports = new List<JsonCorruptionReport>();
            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    string jsonContent = File.ReadAllText(jsonFile);
                    var report = JsonSerializer.Deserialize<JsonCorruptionReport>(jsonContent);
                    if (report != null)
                    {
                        reports.Add(report);
                    }
                }
                catch (Exception ex)
                {
                    WriteWarning($"Failed to load {Path.GetFileName(jsonFile)}: {ex.Message}");
                }
            }

            if (reports.Count == 0)
            {
                WriteError("No valid reports could be loaded.");
                return;
            }

            WriteSuccess($"Loaded {reports.Count} report(s) successfully.\n");

            // Generate global report
            GenerateGlobalHtmlReport(reports, jsonDir);
        }

        public static void GenerateGlobalHtmlReport(List<JsonCorruptionReport> reports, string outputDir)
        {
            WriteInfo("Analyzing data and generating global report...\n");

            // Calculate statistics
            int totalFiles = reports.Count;
            int corruptedFiles = reports.Count(r => r.Status.IsCorrupted);
            int completeFiles = reports.Count(r => r.Status.IsComplete);
            int incompleteFiles = totalFiles - completeFiles;

            long totalBytes = reports.Sum(r => r.FileMetadata.FileSizeBytes);
            double totalMB = totalBytes / (1024.0 * 1024.0);
            double totalGB = totalBytes / (1024.0 * 1024.0 * 1024.0);

            // Duration analysis (only for files with duration data)
            var reportsWithDuration = reports.Where(r => r.VideoDuration != null).ToList();

            // Hour-based heatmap data (transfer failures by hour)
            var hourlyFailures = new int[24];
            var hourlyTotal = new int[24];
            foreach (var report in reports)
            {
                int hour = report.FileMetadata.LastModifiedTimeUtc.ToLocalTime().Hour;
                hourlyTotal[hour]++;
                if (report.Status.IsCorrupted)
                    hourlyFailures[hour]++;
            }
            
            // Scatter plot data: file size vs playable percentage (all files)
            var scatterDataCorrupted = reportsWithDuration
                .Where(r => r.Status.IsCorrupted)
                .Select(r => new {
                    SizeMB = r.FileMetadata.FileSizeMB,
                    PlayablePercent = r.VideoDuration!.PlayablePercentage,
                    FileName = r.FileMetadata.FileName,
                    IsCorrupted = true
                })
                .ToList();

            var scatterDataValid = reportsWithDuration
                .Where(r => !r.Status.IsCorrupted)
                .Select(r => new {
                    SizeMB = r.FileMetadata.FileSizeMB,
                    PlayablePercent = r.VideoDuration!.PlayablePercentage,
                    FileName = r.FileMetadata.FileName,
                    IsCorrupted = false
                })
                .ToList();

            var scatterData = scatterDataCorrupted.Concat(scatterDataValid).ToList();

            // Timeline data: creation vs last modified times
            var timelineData = reports
                .Select(r => new {
                    FileName = r.FileMetadata.FileName,
                    CreationTime = r.FileMetadata.CreationTimeUtc.ToLocalTime(),
                    LastModifiedTime = r.FileMetadata.LastModifiedTimeUtc.ToLocalTime(),
                    IsCorrupted = r.Status.IsCorrupted,
                    DurationHours = (r.FileMetadata.LastModifiedTimeUtc - r.FileMetadata.CreationTimeUtc).TotalHours
                })
                .OrderBy(x => x.CreationTime)
                .ToList();

            // Correlation between file size and corruption
            var fileSizeRanges = new Dictionary<string, (int total, int corrupted)>
            {
                ["0-100 MB"] = (0, 0),
                ["100-500 MB"] = (0, 0),
                ["500 MB-1 GB"] = (0, 0),
                ["1-5 GB"] = (0, 0),
                ["5+ GB"] = (0, 0)
            };

            foreach (var report in reports)
            {
                double sizeMB = report.FileMetadata.FileSizeMB;
                bool isCorrupted = report.Status.IsCorrupted;

                string range;
                if (sizeMB < 100) range = "0-100 MB";
                else if (sizeMB < 500) range = "100-500 MB";
                else if (sizeMB < 1024) range = "500 MB-1 GB";
                else if (sizeMB < 5120) range = "1-5 GB";
                else range = "5+ GB";

                var (total, corrupted) = fileSizeRanges[range];
                fileSizeRanges[range] = (total + 1, isCorrupted ? corrupted + 1 : corrupted);
            }

            // Duration correlation
            var durationRanges = new Dictionary<string, (int total, int corrupted)>
            {
                ["0-1 min"] = (0, 0),
                ["1-5 min"] = (0, 0),
                ["5-15 min"] = (0, 0),
                ["15-30 min"] = (0, 0),
                ["30+ min"] = (0, 0)
            };

            foreach (var report in reportsWithDuration)
            {
                double durationSec = report.VideoDuration!.TotalDurationSeconds;
                bool isCorrupted = report.Status.IsCorrupted;

                string range;
                if (durationSec < 60) range = "0-1 min";
                else if (durationSec < 300) range = "1-5 min";
                else if (durationSec < 900) range = "5-15 min";
                else if (durationSec < 1800) range = "15-30 min";
                else range = "30+ min";

                var (total, corrupted) = durationRanges[range];
                durationRanges[range] = (total + 1, isCorrupted ? corrupted + 1 : corrupted);
            }

            // Generate HTML
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine("    <title>Video Transfer Failure Analysis Dashboard</title>");
            html.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js\"></script>");
            html.AppendLine("    <style>");
            html.AppendLine("        * { margin: 0; padding: 0; box-sizing: border-box; }");
            html.AppendLine("        body {");
            html.AppendLine("            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;");
            html.AppendLine("            background: #0f0f1e;");
            html.AppendLine("            color: #e0e0e0;");
            html.AppendLine("            padding: 20px;");
            html.AppendLine("            min-height: 100vh;");
            html.AppendLine("        }");
            html.AppendLine("        .container {");
            html.AppendLine("            max-width: 1600px;");
            html.AppendLine("            margin: 0 auto;");
            html.AppendLine("        }");
            html.AppendLine("        .header {");
            html.AppendLine("            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);");
            html.AppendLine("            padding: 40px;");
            html.AppendLine("            border-radius: 16px;");
            html.AppendLine("            margin-bottom: 30px;");
            html.AppendLine("            box-shadow: 0 10px 50px rgba(102, 126, 234, 0.3);");
            html.AppendLine("        }");
            html.AppendLine("        .header h1 {");
            html.AppendLine("            font-size: 2.8em;");
            html.AppendLine("            margin-bottom: 10px;");
            html.AppendLine("            font-weight: 700;");
            html.AppendLine("        }");
            html.AppendLine("        .header .subtitle {");
            html.AppendLine("            font-size: 1.2em;");
            html.AppendLine("            opacity: 0.95;");
            html.AppendLine("        }");
            html.AppendLine("        .stats-grid {");
            html.AppendLine("            display: grid;");
            html.AppendLine("            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));");
            html.AppendLine("            gap: 20px;");
            html.AppendLine("            margin-bottom: 30px;");
            html.AppendLine("        }");
            html.AppendLine("        .stat-card {");
            html.AppendLine("            background: linear-gradient(135deg, rgba(255,255,255,0.08) 0%, rgba(255,255,255,0.04) 100%);");
            html.AppendLine("            padding: 25px;");
            html.AppendLine("            border-radius: 12px;");
            html.AppendLine("            border: 1px solid rgba(255,255,255,0.12);");
            html.AppendLine("            backdrop-filter: blur(10px);");
            html.AppendLine("            transition: transform 0.2s, box-shadow 0.2s;");
            html.AppendLine("        }");
            html.AppendLine("        .stat-card:hover {");
            html.AppendLine("            transform: translateY(-3px);");
            html.AppendLine("            box-shadow: 0 8px 25px rgba(102, 126, 234, 0.2);");
            html.AppendLine("        }");
            html.AppendLine("        .stat-card .label {");
            html.AppendLine("            font-size: 0.85em;");
            html.AppendLine("            color: #999;");
            html.AppendLine("            margin-bottom: 8px;");
            html.AppendLine("            text-transform: uppercase;");
            html.AppendLine("            letter-spacing: 1.2px;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("        }");
            html.AppendLine("        .stat-card .value {");
            html.AppendLine("            font-size: 2.5em;");
            html.AppendLine("            font-weight: 700;");
            html.AppendLine("            color: #667eea;");
            html.AppendLine("        }");
            html.AppendLine("        .stat-card.success .value { color: #10b981; }");
            html.AppendLine("        .stat-card.error .value { color: #ef4444; }");
            html.AppendLine("        .stat-card.warning .value { color: #f59e0b; }");
            html.AppendLine("        .charts-grid {");
            html.AppendLine("            display: grid;");
            html.AppendLine("            grid-template-columns: repeat(auto-fit, minmax(550px, 1fr));");
            html.AppendLine("            gap: 25px;");
            html.AppendLine("            margin-bottom: 30px;");
            html.AppendLine("        }");
            html.AppendLine("        .chart-container {");
            html.AppendLine("            background: rgba(255,255,255,0.03);");
            html.AppendLine("            padding: 30px;");
            html.AppendLine("            border-radius: 12px;");
            html.AppendLine("            border: 1px solid rgba(255,255,255,0.08);");
            html.AppendLine("            box-shadow: 0 4px 15px rgba(0,0,0,0.2);");
            html.AppendLine("        }");
            html.AppendLine("        .chart-container h2 {");
            html.AppendLine("            margin-bottom: 20px;");
            html.AppendLine("            color: #fff;");
            html.AppendLine("            font-size: 1.4em;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("        }");
            html.AppendLine("        .chart-container canvas {");
            html.AppendLine("            max-height: 400px;");
            html.AppendLine("        }");
            html.AppendLine("        .full-width {");
            html.AppendLine("            grid-column: 1 / -1;");
            html.AppendLine("        }");
            html.AppendLine("        .insights {");
            html.AppendLine("            background: linear-gradient(135deg, rgba(102, 126, 234, 0.15) 0%, rgba(118, 75, 162, 0.1) 100%);");
            html.AppendLine("            padding: 35px;");
            html.AppendLine("            border-radius: 12px;");
            html.AppendLine("            border-left: 5px solid #667eea;");
            html.AppendLine("            margin-top: 30px;");
            html.AppendLine("            box-shadow: 0 4px 20px rgba(102, 126, 234, 0.2);");
            html.AppendLine("        }");
            html.AppendLine("        .insights h2 {");
            html.AppendLine("            margin-bottom: 20px;");
            html.AppendLine("            color: #667eea;");
            html.AppendLine("            font-size: 1.8em;");
            html.AppendLine("        }");
            html.AppendLine("        .insights ul {");
            html.AppendLine("            list-style: none;");
            html.AppendLine("            padding-left: 0;");
            html.AppendLine("        }");
            html.AppendLine("        .insights li {");
            html.AppendLine("            padding: 12px 0;");
            html.AppendLine("            border-bottom: 1px solid rgba(255,255,255,0.1);");
            html.AppendLine("            font-size: 1.05em;");
            html.AppendLine("            line-height: 1.6;");
            html.AppendLine("        }");
            html.AppendLine("        .insights li:last-child {");
            html.AppendLine("            border-bottom: none;");
            html.AppendLine("        }");
            html.AppendLine("        .insights li:before {");
            html.AppendLine("            content: '▸ ';");
            html.AppendLine("            color: #667eea;");
            html.AppendLine("            font-weight: bold;");
            html.AppendLine("            margin-right: 10px;");
            html.AppendLine("        }");
            html.AppendLine("        .insights .conclusion {");
            html.AppendLine("            margin-top: 20px;");
            html.AppendLine("            padding: 20px;");
            html.AppendLine("            background: rgba(239, 68, 68, 0.15);");
            html.AppendLine("            border-left: 4px solid #ef4444;");
            html.AppendLine("            border-radius: 8px;");
            html.AppendLine("            font-size: 1.1em;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("            color: #fca5a5;");
            html.AppendLine("        }");
            html.AppendLine("        .data-table {");
            html.AppendLine("            width: 100%;");
            html.AppendLine("            border-collapse: collapse;");
            html.AppendLine("            margin-top: 20px;");
            html.AppendLine("            font-size: 0.9em;");
            html.AppendLine("        }");
            html.AppendLine("        .data-table th {");
            html.AppendLine("            background: rgba(102, 126, 234, 0.2);");
            html.AppendLine("            padding: 12px;");
            html.AppendLine("            text-align: left;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("            color: #667eea;");
            html.AppendLine("            border-bottom: 2px solid rgba(102, 126, 234, 0.5);");
            html.AppendLine("        }");
            html.AppendLine("        .data-table td {");
            html.AppendLine("            padding: 10px 12px;");
            html.AppendLine("            border-bottom: 1px solid rgba(255,255,255,0.05);");
            html.AppendLine("        }");
            html.AppendLine("        .data-table tr:hover {");
            html.AppendLine("            background: rgba(255,255,255,0.03);");
            html.AppendLine("        }");
            html.AppendLine("        .corrupted-row {");
            html.AppendLine("            color: #fca5a5;");
            html.AppendLine("        }");
            html.AppendLine("        .valid-row {");
            html.AppendLine("            color: #86efac;");
            html.AppendLine("        }");
            html.AppendLine("        .status-badge {");
            html.AppendLine("            padding: 4px 8px;");
            html.AppendLine("            border-radius: 4px;");
            html.AppendLine("            font-size: 0.85em;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("        }");
            html.AppendLine("        .footer {");
            html.AppendLine("            text-align: center;");
            html.AppendLine("            margin-top: 50px;");
            html.AppendLine("            padding: 20px;");
            html.AppendLine("            color: #666;");
            html.AppendLine("            font-size: 0.9em;");
            html.AppendLine("        }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("    <div class=\"container\">");
            html.AppendLine("        <div class=\"header\">");
            html.AppendLine("            <h1>🎥 Video Transfer Failure Analysis Dashboard</h1>");
            html.AppendLine($"            <div class=\"subtitle\">Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Analyzing {totalFiles} video files | MovFileIntegrityChecker v1.0</div>");
            html.AppendLine("        </div>");

            // Stats cards
            html.AppendLine("        <div class=\"stats-grid\">");
            html.AppendLine($"            <div class=\"stat-card\">");
            html.AppendLine($"                <div class=\"label\">Total Files</div>");
            html.AppendLine($"                <div class=\"value\">{totalFiles}</div>");
            html.AppendLine($"            </div>");
            html.AppendLine($"            <div class=\"stat-card success\">");
            html.AppendLine($"                <div class=\"label\">Complete Files</div>");
            html.AppendLine($"                <div class=\"value\">{completeFiles}</div>");
            html.AppendLine($"            </div>");
            html.AppendLine($"            <div class=\"stat-card error\">");
            html.AppendLine($"                <div class=\"label\">Corrupted Files</div>");
            html.AppendLine($"                <div class=\"value\">{corruptedFiles}</div>");
            html.AppendLine($"            </div>");
            html.AppendLine($"            <div class=\"stat-card warning\">");
            html.AppendLine($"                <div class=\"label\">Total Size</div>");
            html.AppendLine($"                <div class=\"value\">{(totalGB >= 1 ? $"{totalGB:F2} GB" : $"{totalMB:F2} MB")}</div>");
            html.AppendLine($"            </div>");
            html.AppendLine("        </div>");

            // Charts
            html.AppendLine("        <div class=\"charts-grid\">");
            
            // Complete vs Incomplete pie chart
            html.AppendLine("            <div class=\"chart-container\">");
            html.AppendLine("                <h2>File Completeness Status</h2>");
            html.AppendLine("                <canvas id=\"completenessChart\"></canvas>");
            html.AppendLine("            </div>");

            // Corrupted vs Non-corrupted pie chart
            html.AppendLine("            <div class=\"chart-container\">");
            html.AppendLine("                <h2>File Corruption Status</h2>");
            html.AppendLine("                <canvas id=\"corruptionChart\"></canvas>");
            html.AppendLine("            </div>");

            // File size vs corruption bar chart
            html.AppendLine("            <div class=\"chart-container full-width\">");
            html.AppendLine("                <h2>Corruption Rate by File Size</h2>");
            html.AppendLine("                <canvas id=\"fileSizeChart\"></canvas>");
            html.AppendLine("            </div>");

            // Duration vs corruption bar chart (if data available)
            if (reportsWithDuration.Count > 0)
            {
                html.AppendLine("            <div class=\"chart-container full-width\">");
                html.AppendLine("                <h2>Corruption Rate by Video Duration</h2>");
                html.AppendLine("                <canvas id=\"durationChart\"></canvas>");
                html.AppendLine("            </div>");
            }

            // Heatmap: Transfer failure by hour
            html.AppendLine("            <div class=\"chart-container full-width\">");
            html.AppendLine("                <h2>🕐 Transfer Failure Frequency by Hour (Local Time)</h2>");
            html.AppendLine("                <canvas id=\"hourlyHeatmap\"></canvas>");
            html.AppendLine("            </div>");

            // Scatter plot: File size vs Playable percentage
            if (scatterData.Count > 0)
            {
                html.AppendLine("            <div class=\"chart-container\">");
                html.AppendLine("                <h2>📊 File Size vs Playable %</h2>");
                html.AppendLine("                <canvas id=\"scatterSizePlayable\"></canvas>");
                html.AppendLine("            </div>");
            }

            // Scatter plot: Last modified hour vs Corruption
            html.AppendLine("            <div class=\"chart-container\">");
            html.AppendLine("                <h2>⏰ Last Modified Hour vs Corruption Rate</h2>");
            html.AppendLine("                <canvas id=\"scatterHourCorruption\"></canvas>");
            html.AppendLine("            </div>");

            // Timeline: Creation vs Last Modified
            html.AppendLine("            <div class=\"chart-container full-width\">");
            html.AppendLine("                <h2>📅 File Creation vs Last Modification Timeline</h2>");
            html.AppendLine("                <canvas id=\"timelineChart\"></canvas>");
            html.AppendLine("            </div>");

            html.AppendLine("        </div>");

            // Data table - show all files sorted by corruption percentage (corrupted first)
            html.AppendLine("        <div class=\"chart-container\">");
            html.AppendLine("            <h2>📋 Detailed File Analysis</h2>");
            html.AppendLine("            <table class=\"data-table\">");
            html.AppendLine("                <thead>");
            html.AppendLine("                    <tr>");
            html.AppendLine("                        <th>File Name</th>");
            html.AppendLine("                        <th>Size (MB)</th>");
            html.AppendLine("                        <th>Duration</th>");
            html.AppendLine("                        <th>Playable %</th>");
            html.AppendLine("                        <th>Corruption %</th>");
            html.AppendLine("                        <th>Last Modified Hour</th>");
            html.AppendLine("                        <th>Status</th>");
            html.AppendLine("                    </tr>");
            html.AppendLine("                </thead>");
            html.AppendLine("                <tbody>");
            
            // Add all files to the table, sorted by corruption (corrupted files first, then by corruption %)
            var allFilesForTable = reports
                .OrderByDescending(r => r.Status.IsCorrupted)
                .ThenByDescending(r => r.VideoDuration?.CorruptedPercentage ?? 0)
                .ToList();

            foreach (var report in allFilesForTable)
            {
                var duration = report.VideoDuration?.TotalDurationFormatted ?? "N/A";
                var playable = report.VideoDuration?.PlayablePercentage.ToString("F1") ?? "N/A";
                var corruption = report.VideoDuration?.CorruptedPercentage.ToString("F1") ?? "N/A";
                var hour = report.FileMetadata.LastModifiedTimeUtc.ToLocalTime().Hour;
                var rowClass = report.Status.IsCorrupted ? "corrupted-row" : "valid-row";
                var statusBadge = report.Status.IsCorrupted ? "❌ Corrupted" : "✅ Valid";
                
                html.AppendLine($"                    <tr class=\"{rowClass}\">");
                html.AppendLine($"                        <td>{System.Security.SecurityElement.Escape(report.FileMetadata.FileName)}</td>");
                html.AppendLine($"                        <td>{report.FileMetadata.FileSizeMB:F2}</td>");
                html.AppendLine($"                        <td>{duration}</td>");
                html.AppendLine($"                        <td>{playable}%</td>");
                html.AppendLine($"                        <td>{corruption}%</td>");
                html.AppendLine($"                        <td>{hour:D2}:00</td>");
                html.AppendLine($"                        <td><span class=\"status-badge\">{statusBadge}</span></td>");
                html.AppendLine("                    </tr>");
            }

            html.AppendLine("                </tbody>");
            html.AppendLine("            </table>");
            html.AppendLine("        </div>");

            // Insights section
            html.AppendLine("        <div class=\"insights\">");
            html.AppendLine("            <h2>🔍 Key Insights & Correlations</h2>");
            html.AppendLine("            <ul>");

            // Generate insights
            double corruptionRate = (double)corruptedFiles / totalFiles * 100;
            html.AppendLine($"                <li><strong>Overall corruption rate:</strong> {corruptionRate:F1}% ({corruptedFiles} out of {totalFiles} files are corrupted or incomplete)</li>");

            // Hourly analysis - find peak failure times
            var peakHours = hourlyFailures
                .Select((count, hour) => new { Hour = hour, Count = count, Total = hourlyTotal[hour] })
                .Where(x => x.Total > 0)
                .OrderByDescending(x => (double)x.Count / x.Total)
                .Take(3)
                .ToList();

            if (peakHours.Any())
            {
                var top = peakHours[0];
                double peakRate = (double)top.Count / top.Total * 100;
                if (peakRate > corruptionRate * 1.2) // At least 20% higher than average
                {
                    var timeRanges = peakHours
                        .Where(x => (double)x.Count / x.Total * 100 > corruptionRate * 1.1)
                        .Select(x => $"{x.Hour:D2}:00-{(x.Hour + 1) % 24:D2}:00")
                        .ToList();
                    
                    html.AppendLine($"                <li><strong>⚠️ High-risk time window:</strong> {peakRate:F1}% of files modified at {top.Hour:D2}:00-{(top.Hour + 1) % 24:D2}:00 are corrupted (peak failure time)</li>");
                    
                    if (timeRanges.Count > 1)
                    {
                        html.AppendLine($"                <li><strong>Additional risk windows:</strong> {string.Join(", ", timeRanges.Skip(1))}</li>");
                    }
                }
            }

            // Find highest risk file size range
            var highestRiskSize = fileSizeRanges
                .Where(kvp => kvp.Value.total > 0)
                .OrderByDescending(kvp => (double)kvp.Value.corrupted / kvp.Value.total)
                .FirstOrDefault();
            
            if (highestRiskSize.Key != null)
            {
                double riskRate = (double)highestRiskSize.Value.corrupted / highestRiskSize.Value.total * 100;
                html.AppendLine($"                <li><strong>File size correlation:</strong> {highestRiskSize.Key} range shows highest corruption risk at {riskRate:F1}%</li>");
            }

            // Find highest risk duration range
            if (reportsWithDuration.Count > 0)
            {
                var highestRiskDuration = durationRanges
                    .Where(kvp => kvp.Value.total > 0)
                    .OrderByDescending(kvp => (double)kvp.Value.corrupted / kvp.Value.total)
                    .FirstOrDefault();
                
                if (highestRiskDuration.Key != null)
                {
                    double riskRate = (double)highestRiskDuration.Value.corrupted / highestRiskDuration.Value.total * 100;
                    html.AppendLine($"                <li><strong>Duration correlation:</strong> {highestRiskDuration.Key} videos have {riskRate:F1}% corruption rate</li>");
                }
            }

            // Average playable percentage for corrupted files
            var corruptedWithDuration = reportsWithDuration.Where(r => r.Status.IsCorrupted).ToList();
            if (corruptedWithDuration.Count > 0)
            {
                double avgPlayable = corruptedWithDuration.Average(r => r.VideoDuration!.PlayablePercentage);
                html.AppendLine($"                <li><strong>Data recovery potential:</strong> Corrupted files retain {avgPlayable:F1}% playable content on average</li>");
            }

            // Transfer interruption patterns
            var abruptStops = timelineData
                .Where(x => x.IsCorrupted && x.DurationHours < 1)
                .Count();
            
            if (abruptStops > 0)
            {
                double abruptRate = (double)abruptStops / corruptedFiles * 100;
                html.AppendLine($"                <li><strong>Transfer interruption pattern:</strong> {abruptRate:F1}% of corrupted files show signs of abrupt transfer termination (modified within 1 hour of creation)</li>");
            }

            // Most common issues
            var allIssues = reports
                .SelectMany(r => r.IntegrityAnalysis.Issues)
                .Where(i => !string.IsNullOrEmpty(i))
                .GroupBy(i => i.Contains("Incomplete atom") ? "Incomplete atom" : 
                              i.Contains("Missing") ? "Missing atom" : 
                              i.Contains("Gap") ? "Gap after last atom" : i)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToList();

            if (allIssues.Count > 0)
            {
                html.AppendLine($"                <li><strong>Most common structural issue:</strong> \"{allIssues[0].Key}\" detected in {allIssues[0].Count()} files ({(double)allIssues[0].Count() / totalFiles * 100:F1}%)</li>");
            }

            html.AppendLine("            </ul>");

            // Root cause conclusion
            html.AppendLine("            <div class=\"conclusion\">");
            html.AppendLine("                <strong>💡 Likely Root Cause:</strong> ");
            
            // Determine likely cause based on patterns
            if (peakHours.Any() && (double)peakHours[0].Count / peakHours[0].Total * 100 > corruptionRate * 1.5)
            {
                var topHour = peakHours[0].Hour;
                if (topHour >= 2 && topHour <= 5)
                {
                    html.AppendLine("                Scheduled server maintenance or automatic shutdown during nightly hours (03:00-05:00) is likely interrupting ongoing file transfers.");
                }
                else if (topHour >= 12 && topHour <= 14)
                {
                    html.AppendLine("                Network congestion or server load during peak hours (12:00-14:00) may be causing transfer timeouts and incomplete file writes.");
                }
                else
                {
                    html.AppendLine($"                Systematic failures occur predominantly at {topHour:D2}:00-{(topHour + 1) % 24:D2}:00, suggesting scheduled operations or resource constraints during this time window.");
                }
            }
            else if (highestRiskSize.Key != null && highestRiskSize.Key.Contains("GB"))
            {
                html.AppendLine("                Larger files are disproportionately affected, indicating network timeout issues or insufficient buffer sizes for large file transfers.");
            }
            else
            {
                html.AppendLine("                File transfers are being interrupted before completion, likely due to network instability, storage issues, or application crashes during write operations.");
            }
            
            html.AppendLine("            </div>");
            html.AppendLine("        </div>");

            // Footer
            html.AppendLine("        <div class=\"footer\">");
            html.AppendLine("            <p>Generated by MovFileIntegrityChecker v1.0</p>");
            html.AppendLine("        </div>");
            html.AppendLine("    </div>");

            // Chart.js scripts
            html.AppendLine("    <script>");
            html.AppendLine("        Chart.defaults.color = '#ffffff';");
            html.AppendLine("        Chart.defaults.borderColor = 'rgba(255,255,255,0.1)';");

            // Completeness chart
            html.AppendLine("        new Chart(document.getElementById('completenessChart'), {");
            html.AppendLine("            type: 'pie',");
            html.AppendLine("            data: {");
            html.AppendLine($"                labels: ['Complete ({completeFiles})', 'Incomplete ({incompleteFiles})'],");
            html.AppendLine("                datasets: [{");
            html.AppendLine($"                    data: [{completeFiles}, {incompleteFiles}],");
            html.AppendLine("                    backgroundColor: ['#10b981', '#ef4444'],");
            html.AppendLine("                    borderWidth: 2,");
            html.AppendLine("                    borderColor: '#1a1a2e'");
            html.AppendLine("                }]");
            html.AppendLine("            },");
            html.AppendLine("            options: {");
            html.AppendLine("                responsive: true,");
            html.AppendLine("                maintainAspectRatio: true,");
            html.AppendLine("                plugins: {");
            html.AppendLine("                    legend: { position: 'bottom' }");
            html.AppendLine("                }");
            html.AppendLine("            }");
            html.AppendLine("        });");

            // Corruption chart
            int nonCorrupted = totalFiles - corruptedFiles;
            html.AppendLine("        new Chart(document.getElementById('corruptionChart'), {");
            html.AppendLine("            type: 'pie',");
            html.AppendLine("            data: {");
            html.AppendLine($"                labels: ['Valid ({nonCorrupted})', 'Corrupted ({corruptedFiles})'],");
            html.AppendLine("                datasets: [{");
            html.AppendLine($"                    data: [{nonCorrupted}, {corruptedFiles}],");
            html.AppendLine("                    backgroundColor: ['#10b981', '#ef4444'],");
            html.AppendLine("                    borderWidth: 2,");
            html.AppendLine("                    borderColor: '#1a1a2e'");
            html.AppendLine("                }]");
            html.AppendLine("            },");
            html.AppendLine("            options: {");
            html.AppendLine("                responsive: true,");
            html.AppendLine("                maintainAspectRatio: true,");
            html.AppendLine("                plugins: {");
            html.AppendLine("                    legend: { position: 'bottom' }");
            html.AppendLine("                }");
            html.AppendLine("            }");
            html.AppendLine("        });");

            // File size chart
            var sizeLabels = string.Join(", ", fileSizeRanges.Keys.Select(k => $"'{k}'"));
            var sizeTotals = string.Join(", ", fileSizeRanges.Values.Select(v => v.total));
            var sizeCorrupted = string.Join(", ", fileSizeRanges.Values.Select(v => v.corrupted));
            var sizePercentages = string.Join(", ", fileSizeRanges.Values.Select(v => 
                v.total > 0 ? ((double)v.corrupted / v.total * 100).ToString("F1") : "0"));

            html.AppendLine("        new Chart(document.getElementById('fileSizeChart'), {");
            html.AppendLine("            type: 'bar',");
            html.AppendLine("            data: {");
            html.AppendLine($"                labels: [{sizeLabels}],");
            html.AppendLine("                datasets: [{");
            html.AppendLine("                    label: 'Total Files',");
            html.AppendLine($"                    data: [{sizeTotals}],");
            html.AppendLine("                    backgroundColor: 'rgba(102, 126, 234, 0.6)',");
            html.AppendLine("                    borderColor: 'rgba(102, 126, 234, 1)',");
            html.AppendLine("                    borderWidth: 1");
            html.AppendLine("                }, {");
            html.AppendLine("                    label: 'Corrupted Files',");
            html.AppendLine($"                    data: [{sizeCorrupted}],");
            html.AppendLine("                    backgroundColor: 'rgba(239, 68, 68, 0.6)',");
            html.AppendLine("                    borderColor: 'rgba(239, 68, 68, 1)',");
            html.AppendLine("                    borderWidth: 1");
            html.AppendLine("                }]");
            html.AppendLine("            },");
            html.AppendLine("            options: {");
            html.AppendLine("                responsive: true,");
            html.AppendLine("                maintainAspectRatio: true,");
            html.AppendLine("                scales: {");
            html.AppendLine("                    y: { beginAtZero: true }");
            html.AppendLine("                },");
            html.AppendLine("                plugins: {");
            html.AppendLine("                    legend: { position: 'top' },");
            html.AppendLine("                    tooltip: {");
            html.AppendLine("                        callbacks: {");
            html.AppendLine("                            afterLabel: function(context) {");
            html.AppendLine($"                                const percentages = [{sizePercentages}];");
            html.AppendLine("                                if (context.datasetIndex === 1) {");
            html.AppendLine("                                    return 'Corruption Rate: ' + percentages[context.dataIndex] + '%';");
            html.AppendLine("                                }");
            html.AppendLine("                            }");
            html.AppendLine("                        }");
            html.AppendLine("                    }");
            html.AppendLine("                }");
            html.AppendLine("            }");
            html.AppendLine("        });");

            // Duration chart (if data available)
            if (reportsWithDuration.Count > 0)
            {
                var durationLabels = string.Join(", ", durationRanges.Keys.Select(k => $"'{k}'"));
                var durationTotals = string.Join(", ", durationRanges.Values.Select(v => v.total));
                var durationCorrupted = string.Join(", ", durationRanges.Values.Select(v => v.corrupted));
                var durationPercentages = string.Join(", ", durationRanges.Values.Select(v => 
                    v.total > 0 ? ((double)v.corrupted / v.total * 100).ToString("F1") : "0"));

                html.AppendLine("        new Chart(document.getElementById('durationChart'), {");
                html.AppendLine("            type: 'bar',");
                html.AppendLine("            data: {");
                html.AppendLine($"                labels: [{durationLabels}],");
                html.AppendLine("                datasets: [{");
                html.AppendLine("                    label: 'Total Files',");
                html.AppendLine($"                    data: [{durationTotals}],");
                html.AppendLine("                    backgroundColor: 'rgba(102, 126, 234, 0.6)',");
                html.AppendLine("                    borderColor: 'rgba(102, 126, 234, 1)',");
                html.AppendLine("                    borderWidth: 1");
                html.AppendLine("                }, {");
                html.AppendLine("                    label: 'Corrupted Files',");
                html.AppendLine($"                    data: [{durationCorrupted}],");
                html.AppendLine("                    backgroundColor: 'rgba(239, 68, 68, 0.6)',");
                html.AppendLine("                    borderColor: 'rgba(239, 68, 68, 1)',");
                html.AppendLine("                    borderWidth: 1");
                html.AppendLine("                }]");
                html.AppendLine("            },");
                html.AppendLine("            options: {");
                html.AppendLine("                responsive: true,");
                html.AppendLine("                maintainAspectRatio: true,");
                html.AppendLine("                scales: {");
                html.AppendLine("                    y: { beginAtZero: true }");
                html.AppendLine("                },");
                html.AppendLine("                plugins: {");
                html.AppendLine("                    legend: { position: 'top' },");
                html.AppendLine("                    tooltip: {");
                html.AppendLine("                        callbacks: {");
                html.AppendLine("                            afterLabel: function(context) {");
                html.AppendLine($"                                const percentages = [{durationPercentages}];");
                html.AppendLine("                                if (context.datasetIndex === 1) {");
                html.AppendLine("                                    return 'Corruption Rate: ' + percentages[context.dataIndex] + '%';");
                html.AppendLine("                                }");
                html.AppendLine("                            }");
                html.AppendLine("                        }");
                html.AppendLine("                    }");
                html.AppendLine("                }");
                html.AppendLine("            }");
                html.AppendLine("        });");
            }

            // Hourly heatmap
            var hourLabels = string.Join(", ", Enumerable.Range(0, 24).Select(h => $"'{h:D2}:00'"));
            var hourFailureData = string.Join(", ", hourlyFailures);
            var hourTotalData = string.Join(", ", hourlyTotal);
            var hourPercentages = string.Join(", ", hourlyTotal.Select((total, i) => 
                total > 0 ? ((double)hourlyFailures[i] / total * 100).ToString("F1") : "0"));

            html.AppendLine("        new Chart(document.getElementById('hourlyHeatmap'), {");
            html.AppendLine("            type: 'bar',");
            html.AppendLine("            data: {");
            html.AppendLine($"                labels: [{hourLabels}],");
            html.AppendLine("                datasets: [{");
            html.AppendLine("                    label: 'Total Files Modified',");
            html.AppendLine($"                    data: [{hourTotalData}],");
            html.AppendLine("                    backgroundColor: 'rgba(102, 126, 234, 0.5)',");
            html.AppendLine("                    borderColor: 'rgba(102, 126, 234, 1)',");
            html.AppendLine("                    borderWidth: 1");
            html.AppendLine("                }, {");
            html.AppendLine("                    label: 'Failed/Corrupted Files',");
            html.AppendLine($"                    data: [{hourFailureData}],");
            html.AppendLine("                    backgroundColor: 'rgba(239, 68, 68, 0.7)',");
            html.AppendLine("                    borderColor: 'rgba(239, 68, 68, 1)',");
            html.AppendLine("                    borderWidth: 1");
            html.AppendLine("                }]");
            html.AppendLine("            },");
            html.AppendLine("            options: {");
            html.AppendLine("                responsive: true,");
            html.AppendLine("                maintainAspectRatio: true,");
            html.AppendLine("                scales: {");
            html.AppendLine("                    y: { beginAtZero: true, title: { display: true, text: 'Number of Files' } },");
            html.AppendLine("                    x: { title: { display: true, text: 'Hour of Day (Local Time)' } }");
            html.AppendLine("                },");
            html.AppendLine("                plugins: {");
            html.AppendLine("                    legend: { position: 'top' },");
            html.AppendLine("                    tooltip: {");
            html.AppendLine("                        callbacks: {");
            html.AppendLine("                            afterLabel: function(context) {");
            html.AppendLine($"                                const percentages = [{hourPercentages}];");
            html.AppendLine("                                if (context.datasetIndex === 1) {");
            html.AppendLine("                                    return 'Failure Rate: ' + percentages[context.dataIndex] + '%';");
            html.AppendLine("                                }");
            html.AppendLine("                            }");
            html.AppendLine("                        }");
            html.AppendLine("                    }");
            html.AppendLine("                }");
            html.AppendLine("            }");
            html.AppendLine("        });");

            // Scatter plot: File Size vs Playable %
            if (scatterData.Count > 0)
            {
                var scatterPointsCorrupted = string.Join(", ", scatterDataCorrupted.Select(d => 
                    $"{{x: {d.SizeMB.ToString("F2", CultureInfo.InvariantCulture)}, y: {d.PlayablePercent.ToString("F2", CultureInfo.InvariantCulture)}, label: '{System.Security.SecurityElement.Escape(d.FileName)}'}}"));
                
                var scatterPointsValid = string.Join(", ", scatterDataValid.Select(d => 
                    $"{{x: {d.SizeMB.ToString("F2", CultureInfo.InvariantCulture)}, y: {d.PlayablePercent.ToString("F2", CultureInfo.InvariantCulture)}, label: '{System.Security.SecurityElement.Escape(d.FileName)}'}}"));

                html.AppendLine("        new Chart(document.getElementById('scatterSizePlayable'), {");
                html.AppendLine("            type: 'scatter',");
                html.AppendLine("            data: {");
                html.AppendLine("                datasets: [{");
                html.AppendLine("                    label: 'Valid Files (100% Playable)',");
                html.AppendLine($"                    data: [{scatterPointsValid}],");
                html.AppendLine("                    backgroundColor: 'rgba(16, 185, 129, 0.6)',");
                html.AppendLine("                    borderColor: 'rgba(16, 185, 129, 1)',");
                html.AppendLine("                    borderWidth: 1,");
                html.AppendLine("                    pointRadius: 6,");
                html.AppendLine("                    pointHoverRadius: 8");
                html.AppendLine("                }, {");
                html.AppendLine("                    label: 'Corrupted Files',");
                html.AppendLine($"                    data: [{scatterPointsCorrupted}],");
                html.AppendLine("                    backgroundColor: 'rgba(239, 68, 68, 0.6)',");
                html.AppendLine("                    borderColor: 'rgba(239, 68, 68, 1)',");
                html.AppendLine("                    borderWidth: 1,");
                html.AppendLine("                    pointRadius: 6,");
                html.AppendLine("                    pointHoverRadius: 8");
                html.AppendLine("                }]");
                html.AppendLine("            },");
                html.AppendLine("            options: {");
                html.AppendLine("                responsive: true,");
                html.AppendLine("                maintainAspectRatio: true,");
                html.AppendLine("                scales: {");
                html.AppendLine("                    x: { title: { display: true, text: 'File Size (MB)' }, beginAtZero: true },");
                html.AppendLine("                    y: { title: { display: true, text: 'Playable Percentage (%)' }, beginAtZero: true, max: 100 }");
                html.AppendLine("                },");
                html.AppendLine("                plugins: {");
                html.AppendLine("                    legend: { display: true, position: 'top' },");
                html.AppendLine("                    tooltip: {");
                html.AppendLine("                        callbacks: {");
                html.AppendLine("                            label: function(context) {");
                html.AppendLine("                                return context.raw.label + ': ' + context.parsed.y.toFixed(1) + '% playable';");
                html.AppendLine("                            }");
                html.AppendLine("                        }");
                html.AppendLine("                    }");
                html.AppendLine("                }");
                html.AppendLine("            }");
                html.AppendLine("        });");
            }

            // Scatter plot: Last Modified Hour vs Corruption
            var hourCorruptionData = reports
                .GroupBy(r => r.FileMetadata.LastModifiedTimeUtc.ToLocalTime().Hour)
                .Select(g => new {
                    Hour = g.Key,
                    Total = g.Count(),
                    Corrupted = g.Count(r => r.Status.IsCorrupted),
                    Rate = g.Count() > 0 ? (double)g.Count(r => r.Status.IsCorrupted) / g.Count() * 100 : 0
                })
                .ToList();

            var hourScatterPoints = string.Join(", ", hourCorruptionData.Select(d => 
                $"{{x: {d.Hour}, y: {d.Rate.ToString("F2", CultureInfo.InvariantCulture)}, r: {Math.Max(5, d.Total * 2)}}}"));

            html.AppendLine("        new Chart(document.getElementById('scatterHourCorruption'), {");
            html.AppendLine("            type: 'bubble',");
            html.AppendLine("            data: {");
            html.AppendLine($"                datasets: [{{");
            html.AppendLine("                    label: 'Corruption Rate by Hour',");
            html.AppendLine($"                    data: [{hourScatterPoints}],");
            html.AppendLine("                    backgroundColor: 'rgba(245, 158, 11, 0.5)',");
            html.AppendLine("                    borderColor: 'rgba(245, 158, 11, 1)',");
            html.AppendLine("                    borderWidth: 1");
            html.AppendLine("                }]");
            html.AppendLine("            },");
            html.AppendLine("            options: {");
            html.AppendLine("                responsive: true,");
            html.AppendLine("                maintainAspectRatio: true,");
            html.AppendLine("                scales: {");
            html.AppendLine("                    x: { title: { display: true, text: 'Hour of Day (0-23)' }, min: 0, max: 23, ticks: { stepSize: 1 } },");
            html.AppendLine("                    y: { title: { display: true, text: 'Corruption Rate (%)' }, beginAtZero: true, max: 100 }");
            html.AppendLine("                },");
            html.AppendLine("                plugins: {");
            html.AppendLine("                    legend: { display: false },");
            html.AppendLine("                    tooltip: {");
            html.AppendLine("                        callbacks: {");
            html.AppendLine("                            label: function(context) {");
            html.AppendLine("                                return 'Hour ' + context.parsed.x + ':00 - ' + context.parsed.y.toFixed(1) + '% corruption rate';");
            html.AppendLine("                            }");
            html.AppendLine("                        }");
            html.AppendLine("                    }");
            html.AppendLine("                }");
            html.AppendLine("            }");
            html.AppendLine("        });");

            // Timeline chart: Creation vs Last Modified
            var timelineCreationData = string.Join(", ", timelineData.Select(t => 
                $"{{x: '{t.CreationTime:yyyy-MM-dd HH:mm}', y: 1}}"));
            var timelineModifiedData = string.Join(", ", timelineData.Where(t => t.IsCorrupted).Select(t => 
                $"{{x: '{t.LastModifiedTime:yyyy-MM-dd HH:mm}', y: 2}}"));

            html.AppendLine("        new Chart(document.getElementById('timelineChart'), {");
            html.AppendLine("            type: 'scatter',");
            html.AppendLine("            data: {");
            html.AppendLine("                datasets: [{");
            html.AppendLine("                    label: 'File Creation Time',");
            html.AppendLine($"                    data: [{timelineCreationData}],");
            html.AppendLine("                    backgroundColor: 'rgba(16, 185, 129, 0.6)',");
            html.AppendLine("                    borderColor: 'rgba(16, 185, 129, 1)',");
            html.AppendLine("                    pointRadius: 4,");
            html.AppendLine("                    showLine: false");
            html.AppendLine("                }, {");
            html.AppendLine("                    label: 'Last Modified (Corrupted Files)',");
            html.AppendLine($"                    data: [{timelineModifiedData}],");
            html.AppendLine("                    backgroundColor: 'rgba(239, 68, 68, 0.6)',");
            html.AppendLine("                    borderColor: 'rgba(239, 68, 68, 1)',");
            html.AppendLine("                    pointRadius: 4,");
            html.AppendLine("                    showLine: false");
            html.AppendLine("                }]");
            html.AppendLine("            },");
            html.AppendLine("            options: {");
            html.AppendLine("                responsive: true,");
            html.AppendLine("                maintainAspectRatio: true,");
            html.AppendLine("                scales: {");
            html.AppendLine("                    x: { type: 'time', time: { unit: 'day' }, title: { display: true, text: 'Date & Time' } },");
            html.AppendLine("                    y: { display: false, min: 0, max: 3 }");
            html.AppendLine("                },");
            html.AppendLine("                plugins: {");
            html.AppendLine("                    legend: { position: 'top' },");
            html.AppendLine("                    tooltip: {");
            html.AppendLine("                        callbacks: {");
            html.AppendLine("                            label: function(context) {");
            html.AppendLine("                                return context.dataset.label + ': ' + context.parsed.x;");
            html.AppendLine("                            }");
            html.AppendLine("                        }");
            html.AppendLine("                    }");
            html.AppendLine("                }");
            html.AppendLine("            }");
            html.AppendLine("        });");

            html.AppendLine("    </script>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            // Save the report
            string reportPath = Path.Combine(outputDir, $"global-report-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            File.WriteAllText(reportPath, html.ToString(), Encoding.UTF8);

            WriteSuccess($"\n✅ Global analysis report generated successfully!");
            WriteSuccess($"📄 Report location: {reportPath}\n");

            // Try to open the report in the default browser
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = reportPath,
                    UseShellExecute = true
                });
                WriteInfo("Opening report in your default browser...\n");
            }
            catch
            {
                WriteWarning("Could not open report automatically. Please open it manually.\n");
            }
        }
    }
}