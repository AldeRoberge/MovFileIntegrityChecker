using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MovFileIntegrityChecker.Core.Constants;
using MovFileIntegrityChecker.Core.Models;
using MovFileIntegrityChecker.Core.Utilities;

namespace MovFileIntegrityChecker.Core.Services
{
    public static class LegacyReportGenerator
    {
        // --- Helpers for extracting a random frame using ffprobe/ffmpeg ---
        private static double GetVideoDuration(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FfmpegHelper.GetFfprobePath(),
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p == null) return 0;

                string output = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(3000);

                if (!string.IsNullOrWhiteSpace(output) && double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var duration))
                    return duration;

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    var digits = new string(stderr.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
                    if (double.TryParse(digits.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dur2))
                        return dur2;
                }
            }
            catch
            {
            }

            return 0;
        }

        public static string? GetRandomFrameBase64(string filePath)
        {
            try
            {
                // Check if ffmpeg is available
                if (!FfmpegHelper.IsFfmpegAvailable())
                {
                    return null;
                }

                double duration = GetVideoDuration(filePath);
                var rnd = new Random();

                var timestamps = new List<double> { 0 };
                if (duration > 0)
                {
                    timestamps.Add(Math.Min(Math.Max(0.1, duration / 2.0), Math.Max(0.1, duration - 0.5)));
                    if (duration > 2.0) timestamps.Add(Math.Max(0.5, duration - 1.0));
                }

                if (duration > 0.5)
                {
                    double max = Math.Max(0.1, duration - 0.5);
                    timestamps.Add(rnd.NextDouble() * max);
                }

                timestamps = timestamps.Distinct().Select(t => Math.Max(0, t)).Take(5).ToList();

                foreach (var ts in timestamps)
                {
                    var tmp = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ".png");
                    try
                    {
                        var ffmpegPath = FfmpegHelper.GetFfmpegPath();
                        var psi = new ProcessStartInfo
                        {
                            FileName = ffmpegPath,
                            Arguments = $"-ss {ts.ToString(CultureInfo.InvariantCulture)} -i \"{filePath}\" -frames:v 1 -vf \"format=yuv420p,scale=640:-1\" -y -hide_banner -loglevel error \"{tmp}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var p = Process.Start(psi);
                        if (p != null)
                        {
                            string stderr = p.StandardError.ReadToEnd();
                            string stdout = p.StandardOutput.ReadToEnd();

                            if (!p.WaitForExit(10000))
                            {
                                try { p.Kill(true); } catch { }
                            }

                            // Log any errors for debugging
                            if (!string.IsNullOrWhiteSpace(stderr))
                            {
                                Console.WriteLine($"FFmpeg stderr: {stderr}");
                            }
                        }

                        if (File.Exists(tmp))
                        {
                            var fi = new FileInfo(tmp);
                            if (fi.Length > 100)
                            {
                                byte[] data = File.ReadAllBytes(tmp);
                                try { File.Delete(tmp); } catch { }
                                return Convert.ToBase64String(data);
                            }
                            try { File.Delete(tmp); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error extracting frame: {ex.Message}");
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public static void CreateJsonReport(FileCheckResult result, string? customOutputFolder = null)
        {
            try
            {
                string filePath = result.FilePath;
                FileInfo fileInfo = new FileInfo(filePath);

                string jsonReportDir;

                // Use custom folder if provided and exists
                if (!string.IsNullOrWhiteSpace(customOutputFolder) && Directory.Exists(customOutputFolder))
                {
                    jsonReportDir = customOutputFolder;
                }
                else
                {
                    jsonReportDir = @"T:\SPT\SP\Mont\Projets\3_PRJ\9-ALEXANDRE_DEMERS-ROBERGE\Fichiers Corrompus";

                    if (!Directory.Exists(jsonReportDir))
                    {
                        jsonReportDir = Path.Combine(Path.GetDirectoryName(filePath) ?? ".", "Reports");
                        if (!Directory.Exists(jsonReportDir))
                            Directory.CreateDirectory(jsonReportDir);
                    }
                }

                string baseName = Path.GetFileNameWithoutExtension(filePath);
                string jsonFileName = $"{baseName}_report.json";
                string jsonReportPath = Path.Combine(jsonReportDir, jsonFileName);

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
                            IsKnownType = AtomConstants.ValidAtomTypes.Contains(a.Type)
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

                if (result.TotalDuration > 0)
                {
                    double missingDuration = result.TotalDuration - result.PlayableDuration;
                    double playablePercent = (result.PlayableDuration / result.TotalDuration) * 100.0;
                    double corruptedPercent = 100.0 - playablePercent;

                    jsonReport.VideoDuration = new JsonVideoDuration
                    {
                        TotalDurationSeconds = result.TotalDuration,
                        TotalDurationFormatted = ConsoleHelper.FormatDuration(result.TotalDuration),
                        PlayableDurationSeconds = result.PlayableDuration,
                        PlayableDurationFormatted = ConsoleHelper.FormatDuration(result.PlayableDuration),
                        MissingDurationSeconds = missingDuration,
                        MissingDurationFormatted = ConsoleHelper.FormatDuration(missingDuration),
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

                // Store the report path in the result
                result.JsonReportPath = jsonReportPath;

                ConsoleHelper.WriteSuccess($"✅ JSON report saved: {jsonReportPath}");
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteWarning($"⚠️ Unable to create JSON report: {ex.Message}");
            }
        }

        public static void CreateErrorReport(FileCheckResult result, string? customOutputFolder = null)
        {
            try
            {
                string filePath = result.FilePath;
                string baseName = Path.GetFileNameWithoutExtension(filePath);

                // Use custom folder if provided, otherwise use the same folder as the video file
                string folder = !string.IsNullOrWhiteSpace(customOutputFolder) && Directory.Exists(customOutputFolder)
                    ? customOutputFolder
                    : Path.GetDirectoryName(filePath)!;

                string reportPath = Path.Combine(folder, $"{baseName}-Incomplet.html");

                // Store the report path in the result
                result.HtmlReportPath = reportPath;

                var sb = new StringBuilder();

                string? frameBase64 = GetRandomFrameBase64(filePath);

                sb.AppendLine("<!DOCTYPE html>");
                sb.AppendLine("<html lang=\"fr\">");
                sb.AppendLine("<head>");
                sb.AppendLine("    <meta charset=\"UTF-8\">");
                sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
                sb.AppendLine($"    <title>Rapport d'Intégrité - {Path.GetFileName(filePath)}</title>");
                sb.AppendLine("    <style>");
                sb.AppendLine("        * { margin: 0; padding: 0; box-sizing: border-box; }");
                sb.AppendLine("        body {");
                sb.AppendLine("            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Helvetica Neue', Arial, sans-serif;");
                sb.AppendLine("            background: #ffffff;");
                sb.AppendLine("            color: #1a1f36;");
                sb.AppendLine("            line-height: 1.6;");
                sb.AppendLine("            min-height: 100vh;");
                sb.AppendLine("            padding: 0;");
                sb.AppendLine("        }");
                sb.AppendLine("        .container {");
                sb.AppendLine("            max-width: 900px;");
                sb.AppendLine("            margin: 0 auto;");
                sb.AppendLine("            background: #ffffff;");
                sb.AppendLine("        }");
                sb.AppendLine("        .header {");
                sb.AppendLine("            padding: 48px 32px 32px;");
                sb.AppendLine("            border-bottom: 1px solid #e6e6e6;");
                sb.AppendLine("        }");
                sb.AppendLine("        .header h1 {");
                sb.AppendLine("            font-size: 28px;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("            color: #0a2540;");
                sb.AppendLine("            margin-bottom: 8px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .header .subtitle {");
                sb.AppendLine("            font-size: 15px;");
                sb.AppendLine("            color: #425466;");
                sb.AppendLine("            margin-bottom: 16px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .header .warning {");
                sb.AppendLine("            font-size: 14px;");
                sb.AppendLine("            color: #cd5120;");
                sb.AppendLine("            background: #fff4ed;");
                sb.AppendLine("            padding: 16px;");
                sb.AppendLine("            border-radius: 6px;");
                sb.AppendLine("            border-left: 3px solid #cd5120;");
                sb.AppendLine("            margin-top: 16px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .header .warning strong {");
                sb.AppendLine("            display: block;");
                sb.AppendLine("            margin-bottom: 4px;");
                sb.AppendLine("            color: #0a2540;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("        }");
                sb.AppendLine("        .content {");
                sb.AppendLine("            padding: 32px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .section {");
                sb.AppendLine("            margin-bottom: 40px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .section-title {");
                sb.AppendLine("            font-size: 16px;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("            color: #0a2540;");
                sb.AppendLine("            margin-bottom: 16px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .info-grid {");
                sb.AppendLine("            display: grid;");
                sb.AppendLine("            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));");
                sb.AppendLine("            gap: 16px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .info-card {");
                sb.AppendLine("            background: #fafbfc;");
                sb.AppendLine("            padding: 20px;");
                sb.AppendLine("            border-radius: 6px;");
                sb.AppendLine("            border: 1px solid #e6e6e6;");
                sb.AppendLine("        }");
                sb.AppendLine("        .info-card .label {");
                sb.AppendLine("            font-size: 13px;");
                sb.AppendLine("            color: #6b7c93;");
                sb.AppendLine("            margin-bottom: 6px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .info-card .value {");
                sb.AppendLine("            font-size: 20px;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("            color: #0a2540;");
                sb.AppendLine("        }");
                sb.AppendLine("        .issue-list {");
                sb.AppendLine("            background: #fff4ed;");
                sb.AppendLine("            border-left: 3px solid #cd5120;");
                sb.AppendLine("            padding: 16px;");
                sb.AppendLine("            border-radius: 6px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .issue-item {");
                sb.AppendLine("            padding: 8px 0;");
                sb.AppendLine("            color: #0a2540;");
                sb.AppendLine("            font-size: 14px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .issue-item:last-child {");
                sb.AppendLine("            padding-bottom: 0;");
                sb.AppendLine("        }");
                sb.AppendLine("        .issue-icon {");
                sb.AppendLine("            color: #cd5120;");
                sb.AppendLine("            margin-right: 8px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table {");
                sb.AppendLine("            width: 100%;");
                sb.AppendLine("            border-collapse: collapse;");
                sb.AppendLine("            font-size: 14px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table thead {");
                sb.AppendLine("            border-bottom: 1px solid #e6e6e6;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table th {");
                sb.AppendLine("            padding: 12px 16px;");
                sb.AppendLine("            text-align: left;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("            color: #6b7c93;");
                sb.AppendLine("            font-size: 13px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table td {");
                sb.AppendLine("            padding: 12px 16px;");
                sb.AppendLine("            border-bottom: 1px solid #f6f9fc;");
                sb.AppendLine("            color: #0a2540;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-table tbody tr:hover {");
                sb.AppendLine("            background: #fafbfc;");
                sb.AppendLine("        }");
                sb.AppendLine("        .status-badge {");
                sb.AppendLine("            display: inline-block;");
                sb.AppendLine("            padding: 4px 10px;");
                sb.AppendLine("            border-radius: 4px;");
                sb.AppendLine("            font-size: 12px;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("        }");
                sb.AppendLine("        .status-complete {");
                sb.AppendLine("            background: #d4edda;");
                sb.AppendLine("            color: #155724;");
                sb.AppendLine("        }");
                sb.AppendLine("        .status-incomplete {");
                sb.AppendLine("            background: #fff4ed;");
                sb.AppendLine("            color: #cd5120;");
                sb.AppendLine("        }");
                sb.AppendLine("        .atom-type {");
                sb.AppendLine("            font-family: 'SF Mono', Monaco, 'Courier New', monospace;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("            color: #635bff;");
                sb.AppendLine("        }");
                sb.AppendLine("        .footer {");
                sb.AppendLine("            padding: 24px 32px;");
                sb.AppendLine("            text-align: center;");
                sb.AppendLine("            color: #6b7c93;");
                sb.AppendLine("            font-size: 13px;");
                sb.AppendLine("            border-top: 1px solid #e6e6e6;");
                sb.AppendLine("        }");
                sb.AppendLine("        .progress-bar {");
                sb.AppendLine("            width: 100%;");
                sb.AppendLine("            height: 8px;");
                sb.AppendLine("            background: #e6e6e6;");
                sb.AppendLine("            border-radius: 4px;");
                sb.AppendLine("            overflow: hidden;");
                sb.AppendLine("            margin-top: 16px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .progress-fill {");
                sb.AppendLine("            height: 100%;");
                sb.AppendLine("            background: #635bff;");
                sb.AppendLine("            transition: width 0.3s ease;");
                sb.AppendLine("        }");
                sb.AppendLine("        .progress-label {");
                sb.AppendLine("            font-size: 13px;");
                sb.AppendLine("            color: #6b7c93;");
                sb.AppendLine("            margin-top: 8px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-container {");
                sb.AppendLine("            margin-top: 24px;");
                sb.AppendLine("            padding: 20px;");
                sb.AppendLine("            background: #fafbfc;");
                sb.AppendLine("            border-radius: 6px;");
                sb.AppendLine("            border: 1px solid #e6e6e6;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-title {");
                sb.AppendLine("            font-size: 14px;");
                sb.AppendLine("            color: #0a2540;");
                sb.AppendLine("            margin-bottom: 12px;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-bar {");
                sb.AppendLine("            width: 100%;");
                sb.AppendLine("            height: 8px;");
                sb.AppendLine("            background: #e6e6e6;");
                sb.AppendLine("            border-radius: 4px;");
                sb.AppendLine("            overflow: hidden;");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-good {");
                sb.AppendLine("            background: #0cce6b;");
                sb.AppendLine("            height: 100%;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-bad {");
                sb.AppendLine("            background: #cd5120;");
                sb.AppendLine("            height: 100%;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-labels {");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("            justify-content: space-between;");
                sb.AppendLine("            margin-top: 8px;");
                sb.AppendLine("            font-size: 13px;");
                sb.AppendLine("            color: #6b7c93;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-label .time {");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("            color: #0a2540;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-info {");
                sb.AppendLine("            margin-top: 12px;");
                sb.AppendLine("            padding: 12px;");
                sb.AppendLine("            background: #ffffff;");
                sb.AppendLine("            border-radius: 6px;");
                sb.AppendLine("            font-size: 13px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-info div {");
                sb.AppendLine("            color: #425466;");
                sb.AppendLine("            margin: 4px 0;");
                sb.AppendLine("        }");
                sb.AppendLine("        .timeline-info strong {");
                sb.AppendLine("            color: #0a2540;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("        }");
                sb.AppendLine("        .preview img { max-width: 100%; border-radius: 6px; border: 1px solid #e6e6e6; }");
                sb.AppendLine("        .audio-tracks-container {");
                sb.AppendLine("            margin-top: 24px;");
                sb.AppendLine("            padding: 20px;");
                sb.AppendLine("            background: #fafbfc;");
                sb.AppendLine("            border-radius: 6px;");
                sb.AppendLine("            border: 1px solid #e6e6e6;");
                sb.AppendLine("        }");
                sb.AppendLine("        .audio-track {");
                sb.AppendLine("            margin-bottom: 20px;");
                sb.AppendLine("            padding: 16px;");
                sb.AppendLine("            background: #ffffff;");
                sb.AppendLine("            border-radius: 6px;");
                sb.AppendLine("            border: 1px solid #e6e6e6;");
                sb.AppendLine("        }");
                sb.AppendLine("        .audio-track:last-child {");
                sb.AppendLine("            margin-bottom: 0;");
                sb.AppendLine("        }");
                sb.AppendLine("        .audio-track-header {");
                sb.AppendLine("            display: flex;");
                sb.AppendLine("            justify-content: space-between;");
                sb.AppendLine("            align-items: center;");
                sb.AppendLine("            margin-bottom: 12px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .audio-track-title {");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("            color: #0a2540;");
                sb.AppendLine("            font-size: 14px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .audio-track-info {");
                sb.AppendLine("            font-size: 12px;");
                sb.AppendLine("            color: #6b7c93;");
                sb.AppendLine("            margin-bottom: 12px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .audio-waveform {");
                sb.AppendLine("            width: 100%;");
                sb.AppendLine("            height: 60px;");
                sb.AppendLine("            background: #f6f9fc;");
                sb.AppendLine("            border-radius: 4px;");
                sb.AppendLine("            overflow: hidden;");
                sb.AppendLine("        }");
                sb.AppendLine("        .audio-waveform svg {");
                sb.AppendLine("            width: 100%;");
                sb.AppendLine("            height: 100%;");
                sb.AppendLine("        }");
                sb.AppendLine("        .no-audio-badge {");
                sb.AppendLine("            display: inline-block;");
                sb.AppendLine("            padding: 4px 10px;");
                sb.AppendLine("            background: #fff4ed;");
                sb.AppendLine("            color: #cd5120;");
                sb.AppendLine("            border-radius: 4px;");
                sb.AppendLine("            font-size: 12px;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("        }");
                sb.AppendLine("        .teams-button {");
                sb.AppendLine("            display: inline-flex;");
                sb.AppendLine("            align-items: center;");
                sb.AppendLine("            gap: 8px;");
                sb.AppendLine("            background: #6264A7;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            padding: 12px 20px;");
                sb.AppendLine("            border-radius: 6px;");
                sb.AppendLine("            text-decoration: none;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("            font-size: 14px;");
                sb.AppendLine("            transition: background 0.2s;");
                sb.AppendLine("            border: none;");
                sb.AppendLine("            cursor: pointer;");
                sb.AppendLine("            margin-top: 16px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .teams-button:hover {");
                sb.AppendLine("            background: #464775;");
                sb.AppendLine("        }");
                sb.AppendLine("        .teams-button svg {");
                sb.AppendLine("            width: 20px;");
                sb.AppendLine("            height: 20px;");
                sb.AppendLine("            fill: currentColor;");
                sb.AppendLine("        }");
                sb.AppendLine("        .download-button {");
                sb.AppendLine("            display: inline-flex;");
                sb.AppendLine("            align-items: center;");
                sb.AppendLine("            gap: 8px;");
                sb.AppendLine("            background: #0cce6b;");
                sb.AppendLine("            color: #ffffff;");
                sb.AppendLine("            padding: 12px 20px;");
                sb.AppendLine("            border-radius: 6px;");
                sb.AppendLine("            text-decoration: none;");
                sb.AppendLine("            font-weight: 600;");
                sb.AppendLine("            font-size: 14px;");
                sb.AppendLine("            transition: background 0.2s;");
                sb.AppendLine("            border: none;");
                sb.AppendLine("            cursor: pointer;");
                sb.AppendLine("            margin-top: 16px;");
                sb.AppendLine("            margin-left: 12px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .download-button:hover {");
                sb.AppendLine("            background: #0aa557;");
                sb.AppendLine("        }");
                sb.AppendLine("        .download-button svg {");
                sb.AppendLine("            width: 18px;");
                sb.AppendLine("            height: 18px;");
                sb.AppendLine("            fill: currentColor;");
                sb.AppendLine("        }");
                sb.AppendLine("        .collapsible-section .section-title {");
                sb.AppendLine("            cursor: pointer;");
                sb.AppendLine("            user-select: none;");
                sb.AppendLine("            position: relative;");
                sb.AppendLine("            padding-right: 30px;");
                sb.AppendLine("        }");
                sb.AppendLine("        .collapsible-section .section-title::after {");
                sb.AppendLine("            content: '▼';");
                sb.AppendLine("            position: absolute;");
                sb.AppendLine("            right: 0;");
                sb.AppendLine("            transition: transform 0.3s ease;");
                sb.AppendLine("            font-size: 12px;");
                sb.AppendLine("            color: #6b7c93;");
                sb.AppendLine("        }");
                sb.AppendLine("        .collapsible-section.collapsed .section-title::after {");
                sb.AppendLine("            transform: rotate(-90deg);");
                sb.AppendLine("        }");
                sb.AppendLine("        .collapsible-section .collapsible-content {");
                sb.AppendLine("            overflow: hidden;");
                sb.AppendLine("            transition: max-height 0.3s ease, opacity 0.3s ease;");
                sb.AppendLine("            max-height: 5000px;");
                sb.AppendLine("            opacity: 1;");
                sb.AppendLine("        }");
                sb.AppendLine("        .collapsible-section.collapsed .collapsible-content {");
                sb.AppendLine("            max-height: 0;");
                sb.AppendLine("            opacity: 0;");
                sb.AppendLine("        }");
                sb.AppendLine("    </style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");
                sb.AppendLine("    <div class=\"container\">");

                sb.AppendLine("        <div class=\"header\">");
                sb.AppendLine("            <h1>Rapport d'intégrité</h1>");
                sb.AppendLine($"            <div class=\"subtitle\">{System.Security.SecurityElement.Escape(Path.GetFileName(filePath))}</div>");
                sb.AppendLine("            <div class=\"warning\">");
                sb.AppendLine("                Ce fichier est corrompu.");
                sb.AppendLine("                <strong>Veuillez l'ajouter à la liste des fichiers corrompus et le télécharger à nouveau.</strong>");
                sb.AppendLine("            </div>");
                sb.AppendLine("            <a href=\"https://teams.microsoft.com/l/message/19:af1a5fdd42fa480da8be81ac3b198cd4@thread.skype/1760486492589?tenantId=f5da7850-c1d8-429f-8907-85d7b2606108&groupId=d7812529-55f0-4ee7-a71c-198414378a6c&parentMessageId=1760486492589&teamName=SPUFAD&channelName=04%20%F0%9F%93%BD%EF%B8%8F%20Production%20num%C3%A9rique&createdTime=1760486492589\" class=\"teams-button\" target=\"_blank\">");
                sb.AppendLine("                <svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 2228.833 2073.333\"><path d=\"M1554.637 777.5h575.713c54.391 0 98.483 44.092 98.483 98.483v524.398c0 199.901-162.051 361.952-361.952 361.952h-1.711c-199.901.028-361.975-162-362.004-361.901V828.971c.001-28.427 23.045-51.471 51.471-51.471z\"/><circle cx=\"1943.75\" cy=\"440.583\" r=\"233.25\"/><path d=\"M1218.64 1148.02c0 307.304 249.022 556.326 556.326 556.326 72.742 0 142.012-14.124 205.522-39.576 8.989-3.6 13.436-14.029 9.838-23.018-3.598-8.989-14.03-13.436-23.018-9.838-59.642 23.894-124.794 37.146-192.342 37.146-288.619 0-523.04-234.421-523.04-523.04 0-288.618 234.421-523.039 523.04-523.039 118.638 0 228.198 39.718 316.135 106.628 8.214 6.25 19.972 4.658 26.222-3.555s4.658-19.972-3.555-26.222C2029.81 654.339 1912.93 612.297 1774.966 612.297c-307.304 0-556.326 249.022-556.326 556.326z\"/><path d=\"M1221.795 1148.021c0 288.618 234.421 523.039 523.04 523.039 47.911 0 94.245-6.48 138.243-18.534 9.334-2.555 14.925-12.259 12.37-21.593-2.556-9.334-12.259-14.925-21.593-12.37-41.294 11.316-84.677 17.41-129.02 17.41-271.933 0-490.751-218.818-490.751-490.751s218.818-490.751 490.751-490.751c98.921 0 191.046 29.382 268.143 79.968 8.769 5.749 20.645 3.336 26.394-5.433 5.749-8.769 3.336-20.645-5.433-26.394-82.207-53.959-180.481-85.228-289.103-85.228-288.618 0-523.039 234.421-523.039 523.039z\"/><circle cx=\"1087.562\" cy=\"1168.521\" r=\"362.52\"/><path d=\"M1574.48 1528.86c-31.038 258.086-249.581 457.392-513.324 457.392-285.005 0-516.042-231.037-516.042-516.042 0-285.004 231.037-516.042 516.042-516.042 31.127 0 61.764 2.827 91.645 8.389 9.577 1.785 18.853-4.525 20.639-14.102 1.786-9.577-4.525-18.853-14.102-20.639-31.792-5.922-64.405-8.934-97.182-8.934-303.69 0-550.329 246.639-550.329 550.329 0 303.689 246.639 550.328 550.329 550.328 281.166 0 513.059-210.575 546.085-481.763.589-4.842-2.558-9.3-7.359-10.401-4.802-1.1-9.58 1.839-11.165 6.872-5.059 16.028-11.096 31.638-18.032 46.766-2.421 5.284-.154 11.52 5.13 13.941 5.284 2.421 11.52.154 13.941-5.13 7.576-16.514 14.177-33.685 19.69-51.319 4.018-12.835-5.626-25.715-18.944-25.715h-16.226c-6.358 0-11.51 5.152-11.51 11.51 0 6.359 5.152 11.511 11.51 11.511h16.226z\"/><path d=\"M1061.157 1120.24c-32.757 0-59.331 26.574-59.331 59.331 0 32.757 26.574 59.331 59.331 59.331 32.757 0 59.331-26.574 59.331-59.331 0-32.757-26.574-59.331-59.331-59.331zM1061.157 1171.48c-32.757 0-59.331 26.574-59.331 59.331 0 32.757 26.574 59.331 59.331 59.331 32.757 0 59.331-26.574 59.331-59.331 0-32.757-26.574-59.331-59.331-59.331z\"/><path d=\"M1061.157 1222.72c-32.757 0-59.331 26.574-59.331 59.331 0 32.757 26.574 59.331 59.331 59.331 32.757 0 59.331-26.574 59.331-59.331 0-32.757-26.574-59.331-59.331-59.331z\"/></svg>");
                sb.AppendLine("                Ouvrir le document Compilation des erreurs de transfert vidéos sur Teams");
                sb.AppendLine("            </a>");
                // Add download button if AJA URL is available
                if (!string.IsNullOrEmpty(result.AjaDownloadUrl))
                {
                    string serverName = !string.IsNullOrEmpty(result.AjaServerName) ? result.AjaServerName : "AJA Server";
                    sb.AppendLine($"            <a href=\"{System.Security.SecurityElement.Escape(result.AjaDownloadUrl)}\" class=\"download-button\" target=\"_blank\" title=\"Télécharger depuis {System.Security.SecurityElement.Escape(serverName)}\">");
                    sb.AppendLine("                <svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\" fill=\"currentColor\">");
                    sb.AppendLine("                    <path d=\"M.5 9.9a.5.5 0 0 1 .5.5v2.5a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-2.5a.5.5 0 0 1 1 0v2.5a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2v-2.5a.5.5 0 0 1 .5-.5z\"/>");
                    sb.AppendLine("                    <path d=\"M7.646 11.854a.5.5 0 0 0 .708 0l3-3a.5.5 0 0 0-.708-.708L8.5 10.293V1.5a.5.5 0 0 0-1 0v8.793L5.354 8.146a.5.5 0 1 0-.708.708l3 3z\"/>");
                    sb.AppendLine("                </svg>");
                    sb.AppendLine($"                Télécharger depuis {System.Security.SecurityElement.Escape(serverName)}");
                    sb.AppendLine("            </a>");
                }

                sb.AppendLine("        </div>");

                sb.AppendLine("        <div class=\"content\">");

                if (!string.IsNullOrEmpty(frameBase64))
                {
                    sb.AppendLine("            <div class=\"section\">");
                    sb.AppendLine("                <div class=\"section-title\">Aperçu</div>");
                    sb.AppendLine("                <div class=\"preview\">");
                    sb.AppendLine($"                    <img src=\"data:image/png;base64,{frameBase64}\" alt=\"Aperçu\">");
                    sb.AppendLine("                </div>");
                    sb.AppendLine("            </div>");
                }
                else
                {
                    // FFMPEG output invalid
                }

                sb.AppendLine("            <div class=\"section collapsible-section collapsed\">");
                sb.AppendLine("                <div class=\"section-title\">Résumé technique</div>");
                sb.AppendLine("                <div class=\"collapsible-content\">");
                sb.AppendLine("                <div class=\"info-grid\">");
                sb.AppendLine("                    <div class=\"info-card\">");
                sb.AppendLine("                        <div class=\"label\">Nom du fichier</div>");
                sb.AppendLine($"                        <div class=\"value\">{System.Security.SecurityElement.Escape(Path.GetFileName(filePath))}</div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                    <div class=\"info-card\">");
                sb.AppendLine("                        <div class=\"label\">Taille du fichier</div>");
                sb.AppendLine($"                        <div class=\"value\">{result.FileSize:N0} octets</div>");
                sb.AppendLine($"                        <div class=\"label\" style=\"margin-top: 4px;\">({result.FileSize / (1024.0 * 1024.0):F2} MB)</div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                    <div class=\"info-card\">");
                sb.AppendLine("                        <div class=\"label\">Octets validés</div>");
                sb.AppendLine($"                        <div class=\"value\">{result.BytesValidated:N0}</div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                    <div class=\"info-card\">");
                sb.AppendLine("                        <div class=\"label\">Atomes détectés</div>");
                sb.AppendLine($"                        <div class=\"value\">{result.Atoms.Count}</div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                </div>");

                double validationPercent = result.FileSize > 0 ? (result.BytesValidated * 100.0 / result.FileSize) : 0;
                sb.AppendLine("                <div class=\"progress-bar\">");
                sb.AppendLine($"                    <div class=\"progress-fill\" style=\"width: {validationPercent.ToString("F1", CultureInfo.InvariantCulture)}%\"></div>");
                sb.AppendLine("                </div>");
                sb.AppendLine($"                <div class=\"progress-label\">{validationPercent:F1}% validé</div>");

                if (result.TotalDuration > 0)
                {
                    sb.AppendLine("                <div class=\"timeline-container\">");
                    sb.AppendLine("                    <div class=\"timeline-title\">Chronologie de la vidéo</div>");

                    double playablePercent = result.TotalDuration > 0 ? (result.PlayableDuration / result.TotalDuration) * 100.0 : 0;
                    double brokenPercent = 100.0 - playablePercent;

                    sb.AppendLine("                    <div class=\"timeline-bar\">");
                    if (playablePercent > 0)
                    {
                        sb.AppendLine($"                        <div class=\"timeline-good\" style=\"width: {playablePercent.ToString("F1", CultureInfo.InvariantCulture)}%\"></div>");
                    }

                    if (brokenPercent > 0)
                    {
                        sb.AppendLine($"                        <div class=\"timeline-bad\" style=\"width: {brokenPercent.ToString("F1", CultureInfo.InvariantCulture)}%\"></div>");
                    }

                    sb.AppendLine("                    </div>");

                    sb.AppendLine("                    <div class=\"timeline-labels\">");
                    sb.AppendLine("                        <div>");
                    sb.AppendLine("                            <div class=\"time\">00:00:00</div>");
                    sb.AppendLine("                            <div>Début</div>");
                    sb.AppendLine("                        </div>");

                    if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
                    {
                        sb.AppendLine("                        <div style=\"text-align: center;\">");
                        sb.AppendLine($"                            <div class=\"time\">{System.Security.SecurityElement.Escape(ConsoleHelper.FormatDuration(result.PlayableDuration))}</div>");
                        sb.AppendLine("                            <div style=\"color: #cd5120;\">Point de rupture</div>");
                        sb.AppendLine("                        </div>");
                    }

                    sb.AppendLine("                        <div style=\"text-align: right;\">");
                    sb.AppendLine($"                            <div class=\"time\">{System.Security.SecurityElement.Escape(ConsoleHelper.FormatDuration(result.TotalDuration))}</div>");
                    sb.AppendLine("                            <div>Fin</div>");
                    sb.AppendLine("                        </div>");
                    sb.AppendLine("                    </div>");

                    sb.AppendLine("                    <div class=\"timeline-info\">");
                    sb.AppendLine($"                        <div><strong>Durée totale :</strong> {System.Security.SecurityElement.Escape(ConsoleHelper.FormatDuration(result.TotalDuration))}</div>");
                    sb.AppendLine($"                        <div><strong>Durée lisible :</strong> {System.Security.SecurityElement.Escape(ConsoleHelper.FormatDuration(result.PlayableDuration))} ({playablePercent:F1}%)</div>");
                    if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
                    {
                        double missingDuration = result.TotalDuration - result.PlayableDuration;
                        sb.AppendLine($"                        <div style=\"color: #cd5120;\"><strong>Durée manquante :</strong> {System.Security.SecurityElement.Escape(ConsoleHelper.FormatDuration(missingDuration))} ({brokenPercent:F1}%)</div>");
                    }

                    sb.AppendLine("                    </div>");
                    sb.AppendLine("                </div>");
                }

                // Audio Tracks Section
                if (result.AudioTracks != null && result.AudioTracks.Count > 0)
                {
                    sb.AppendLine("                <div class=\"audio-tracks-container\">");
                    sb.AppendLine("                    <div class=\"timeline-title\">Pistes audio</div>");

                    for (int i = 0; i < result.AudioTracks.Count; i++)
                    {
                        var track = result.AudioTracks[i];
                        sb.AppendLine("                    <div class=\"audio-track\">");
                        sb.AppendLine("                        <div class=\"audio-track-header\">");
                        sb.AppendLine($"                            <div class=\"audio-track-title\">Piste {i + 1} - {System.Security.SecurityElement.Escape(track.Codec.ToUpper())}</div>");

                        if (!track.HasAudio)
                        {
                            sb.AppendLine("                            <span class=\"no-audio-badge\">Sans audio</span>");
                        }

                        sb.AppendLine("                        </div>");

                        string channelLabel = track.Channels switch
                        {
                            1 => "Mono",
                            2 => "Stéréo",
                            6 => "5.1",
                            8 => "7.1",
                            _ => $"{track.Channels} canaux"
                        };

                        sb.AppendLine($"                        <div class=\"audio-track-info\">");
                        sb.AppendLine($"                            {channelLabel} • {track.SampleRate / 1000.0:F1} kHz");
                        if (!string.IsNullOrEmpty(track.Bitrate))
                        {
                            sb.AppendLine($" • {System.Security.SecurityElement.Escape(track.Bitrate)}");
                        }
                        if (track.Language != "und")
                        {
                            sb.AppendLine($" • Langue: {System.Security.SecurityElement.Escape(track.Language)}");
                        }
                        sb.AppendLine("                        </div>");

                        // Waveform visualization
                        sb.AppendLine("                        <div class=\"audio-waveform\">");
                        sb.AppendLine("                            <svg viewBox=\"0 0 200 60\" preserveAspectRatio=\"none\">");

                        if (track.WaveformData != null && track.WaveformData.Count > 0)
                        {
                            // Generate waveform path
                            var pathData = new StringBuilder();
                            pathData.Append("M 0 30 ");

                            for (int j = 0; j < track.WaveformData.Count; j++)
                            {
                                float amplitude = track.WaveformData[j];
                                float x = (float)j / track.WaveformData.Count * 200;
                                float y = 30 - (amplitude * 25); // Center at 30, scale amplitude
                                pathData.Append($"L {x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)} ");
                            }

                            // Mirror for bottom half
                            for (int j = track.WaveformData.Count - 1; j >= 0; j--)
                            {
                                float amplitude = track.WaveformData[j];
                                float x = (float)j / track.WaveformData.Count * 200;
                                float y = 30 + (amplitude * 25);
                                pathData.Append($"L {x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)} ");
                            }

                            pathData.Append("Z");

                            string fillColor = track.HasAudio ? "#635bff" : "#e6e6e6";
                            sb.AppendLine($"                                <path d=\"{pathData}\" fill=\"{fillColor}\" opacity=\"0.8\" />");

                            // Center line
                            sb.AppendLine("                                <line x1=\"0\" y1=\"30\" x2=\"200\" y2=\"30\" stroke=\"#a0a0a0\" stroke-width=\"0.5\" opacity=\"0.3\" />");
                        }
                        else
                        {
                            // No waveform data - show flat line
                            sb.AppendLine("                                <line x1=\"0\" y1=\"30\" x2=\"200\" y2=\"30\" stroke=\"#e6e6e6\" stroke-width=\"2\" />");
                        }

                        sb.AppendLine("                            </svg>");
                        sb.AppendLine("                        </div>");
                        sb.AppendLine("                    </div>");
                    }

                    sb.AppendLine("                </div>");
                }
                else if (result.TotalDuration > 0)
                {
                    // Video has duration but no audio tracks detected
                }

                sb.AppendLine("                </div>"); // Close collapsible-content
                sb.AppendLine("            </div>"); // Close section

                if (result.Issues.Count > 0)
                {
                    sb.AppendLine("            <div class=\"section collapsible-section collapsed\">");
                    sb.AppendLine("                <div class=\"section-title\">Problèmes détectés</div>");
                    sb.AppendLine("                <div class=\"collapsible-content\">");
                    sb.AppendLine("                <div class=\"issue-list\">");
                    foreach (var issue in result.Issues)
                    {
                        sb.AppendLine("                    <div class=\"issue-item\">");
                        sb.AppendLine("                        <span class=\"issue-icon\">•</span>");
                        sb.AppendLine("                        <span>" + System.Security.SecurityElement.Escape(issue) + "</span>");
                        sb.AppendLine("                    </div>");
                    }

                    sb.AppendLine("                </div>");
                    sb.AppendLine("                </div>"); // Close collapsible-content
                    sb.AppendLine("            </div>"); // Close section
                }

                if (result.Atoms.Count > 0)
                {
                    sb.AppendLine("            <div class=\"section collapsible-section collapsed\">");
                    sb.AppendLine("                <div class=\"section-title\">Structure des atomes</div>");
                    sb.AppendLine("                <div class=\"collapsible-content\">");
                    sb.AppendLine("                <table class=\"atom-table\">");
                    sb.AppendLine("                    <thead>");
                    sb.AppendLine("                        <tr>");
                    sb.AppendLine("                            <th>Type</th>");
                    sb.AppendLine("                            <th>Taille</th>");
                    sb.AppendLine("                            <th>Position</th>");
                    sb.AppendLine("                            <th>Statut</th>");
                    sb.AppendLine("                        </tr>");
                    sb.AppendLine("                    </thead>");
                    sb.AppendLine("                    <tbody>");
                    foreach (var atom in result.Atoms)
                    {
                        string statusClass = atom.IsComplete ? "status-complete" : "status-incomplete";
                        string statusText = atom.IsComplete ? "Complet" : "Incomplet";
                        sb.AppendLine("                        <tr>");
                        sb.AppendLine("                            <td><span class=\"atom-type\">" + System.Security.SecurityElement.Escape(atom.Type) + "</span></td>");
                        sb.AppendLine("                            <td>" + atom.Size.ToString("N0") + " octets</td>");
                        sb.AppendLine("                            <td>" + atom.Offset.ToString("N0") + "</td>");
                        sb.AppendLine("                            <td><span class=\"status-badge " + statusClass + "\">" + statusText + "</span></td>");
                        sb.AppendLine("                        </tr>");
                    }

                    sb.AppendLine("                    </tbody>");
                    sb.AppendLine("                </table>");
                    sb.AppendLine("                </div>"); // Close collapsible-content
                    sb.AppendLine("            </div>"); // Close section
                }

                sb.AppendLine("        </div>");

                sb.AppendLine("        <div class=\"footer\">");
                sb.AppendLine("            Généré le " + DateTime.Now.ToString("dd/MM/yyyy") + " à " + DateTime.Now.ToString("HH:mm:ss") + " · MovFileIntegrityChecker");
                sb.AppendLine("        </div>");

                sb.AppendLine("    </div>");

                sb.AppendLine("    <script>");
                sb.AppendLine("        document.addEventListener('DOMContentLoaded', function() {");
                sb.AppendLine("            const collapsibleSections = document.querySelectorAll('.collapsible-section');");
                sb.AppendLine("            collapsibleSections.forEach(function(section) {");
                sb.AppendLine("                const title = section.querySelector('.section-title');");
                sb.AppendLine("                if (title) {");
                sb.AppendLine("                    title.addEventListener('click', function() {");
                sb.AppendLine("                        section.classList.toggle('collapsed');");
                sb.AppendLine("                    });");
                sb.AppendLine("                }");
                sb.AppendLine("            });");
                sb.AppendLine("        });");
                sb.AppendLine("    </script>");

                sb.AppendLine("</body>");
                sb.AppendLine("</html>");

                File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteWarning($"Impossible d'écrire le rapport d'erreur : {ex.Message}");
            }
        }
    }
}
