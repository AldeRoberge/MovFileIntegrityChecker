using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MovFileIntegrityChecker.Core.Models;
using MovFileIntegrityChecker.Core.Constants;
using static MovFileIntegrityChecker.Core.Utilities.ConsoleHelper;

namespace MovFileIntegrityChecker.CLI
{
    public static class MovIntegrityChecker
    {
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

                // HTML Header with embedded CSS - Stripe-like minimalist design
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
                sb.AppendLine("    </style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");
                sb.AppendLine("    <div class=\"container\">");

                // Header
                sb.AppendLine("        <div class=\"header\">");
                sb.AppendLine("            <h1>Rapport d'intégrité</h1>");
                sb.AppendLine($"            <div class=\"subtitle\">{System.Security.SecurityElement.Escape(Path.GetFileName(filePath))}</div>");
                sb.AppendLine("            <div class=\"warning\">");
                sb.AppendLine("                <strong>Fichier potentiellement incomplet</strong>");
                sb.AppendLine("                Ce fichier a été détecté comme potentiellement incomplet. ");
                sb.AppendLine("                Veuillez le vérifier dans VLC pour confirmer sa lecture.");
                sb.AppendLine("            </div>");
                sb.AppendLine("        </div>");

                // Content
                sb.AppendLine("        <div class=\"content\">");

                // Embed preview if available
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
                    sb.AppendLine("            <div class=\"section\">");
                    sb.AppendLine("                <div class=\"section-title\">Aperçu</div>");
                    sb.AppendLine("                <div class=\"issue-list\">");
                    sb.AppendLine("                    <div class=\"issue-item\">");
                    sb.AppendLine("                        <span class=\"issue-icon\">ℹ️</span>");
                    sb.AppendLine("                        <span>Aperçu non disponible (ffmpeg/ffprobe introuvable ou échec de l'extraction).</span>");
                    sb.AppendLine("                    </div>");
                    sb.AppendLine("                </div>");
                    sb.AppendLine("            </div>");
                }

                sb.AppendLine("            <div class=\"section\">");
                sb.AppendLine("                <div class=\"section-title\">Résumé technique</div>");
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

                // Add duration timeline if available
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
                        sb.AppendLine($"                            <div class=\"time\">{System.Security.SecurityElement.Escape(FormatDuration(result.PlayableDuration))}</div>");
                        sb.AppendLine("                            <div style=\"color: #cd5120;\">Point de rupture</div>");
                        sb.AppendLine("                        </div>");
                    }

                    sb.AppendLine("                        <div style=\"text-align: right;\">");
                    sb.AppendLine($"                            <div class=\"time\">{System.Security.SecurityElement.Escape(FormatDuration(result.TotalDuration))}</div>");
                    sb.AppendLine("                            <div>Fin</div>");
                    sb.AppendLine("                        </div>");
                    sb.AppendLine("                    </div>");

                    sb.AppendLine("                    <div class=\"timeline-info\">");
                    sb.AppendLine($"                        <div><strong>Durée totale :</strong> {System.Security.SecurityElement.Escape(FormatDuration(result.TotalDuration))}</div>");
                    sb.AppendLine($"                        <div><strong>Durée lisible :</strong> {System.Security.SecurityElement.Escape(FormatDuration(result.PlayableDuration))} ({playablePercent:F1}%)</div>");
                    if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
                    {
                        double missingDuration = result.TotalDuration - result.PlayableDuration;
                        sb.AppendLine($"                        <div style=\"color: #cd5120;\"><strong>Durée manquante :</strong> {System.Security.SecurityElement.Escape(FormatDuration(missingDuration))} ({brokenPercent:F1}%)</div>");
                    }

                    sb.AppendLine("                    </div>");
                    sb.AppendLine("                </div>");
                }

                sb.AppendLine("            </div>");

                if (result.Issues.Count > 0)
                {
                    sb.AppendLine("            <div class=\"section\">");
                    sb.AppendLine("                <div class=\"section-title\">Problèmes détectés</div>");
                    sb.AppendLine("                <div class=\"issue-list\">");
                    foreach (var issue in result.Issues)
                    {
                        sb.AppendLine("                    <div class=\"issue-item\">");
                        sb.AppendLine("                        <span class=\"issue-icon\">•</span>");
                        sb.AppendLine("                        <span>" + System.Security.SecurityElement.Escape(issue) + "</span>");
                        sb.AppendLine("                    </div>");
                    }

                    sb.AppendLine("                </div>");
                    sb.AppendLine("            </div>");
                }

                if (result.Atoms.Count > 0)
                {
                    sb.AppendLine("            <div class=\"section\">");
                    sb.AppendLine("                <div class=\"section-title\">Structure des atomes</div>");
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
                    sb.AppendLine("            </div>");
                }

                sb.AppendLine("        </div>");

                // Footer
                sb.AppendLine("        <div class=\"footer\">");
                sb.AppendLine("            Généré le " + DateTime.Now.ToString("dd/MM/yyyy") + " à " + DateTime.Now.ToString("HH:mm:ss") + " · MovFileIntegrityChecker");
                sb.AppendLine("        </div>");

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

        public static void RunGlobalAnalysis()
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║              Global Analysis Mode                              ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Ask for the directory containing JSON reports
            Console.Write("Enter the directory containing JSON reports\n(default: TestReports): ");
            string? jsonDir = Console.ReadLine()?.Trim().Trim('"');

            if (string.IsNullOrEmpty(jsonDir))
            {
                // Use relative path to demo files
                jsonDir = Path.Combine("..", "..", "..", "DemoFiles");
                Console.WriteLine($"Using default path: {jsonDir}");
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
                .Select(r => new
                {
                    SizeMB = r.FileMetadata.FileSizeMB,
                    PlayablePercent = r.VideoDuration!.PlayablePercentage,
                    FileName = r.FileMetadata.FileName,
                    IsCorrupted = true
                })
                .ToList();

            var scatterDataValid = reportsWithDuration
                .Where(r => !r.Status.IsCorrupted)
                .Select(r => new
                {
                    SizeMB = r.FileMetadata.FileSizeMB,
                    PlayablePercent = r.VideoDuration!.PlayablePercentage,
                    FileName = r.FileMetadata.FileName,
                    IsCorrupted = false
                })
                .ToList();

            var scatterData = scatterDataCorrupted.Concat(scatterDataValid).ToList();

            // Timeline data: creation vs last modified times
            var timelineData = reports
                .Select(r => new
                {
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
            html.AppendLine("<html lang=\"fr\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine("    <title>Tableau de bord d'analyse</title>");
            html.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js\"></script>");
            html.AppendLine("    <style>");
            html.AppendLine("        * { margin: 0; padding: 0; box-sizing: border-box; }");
            html.AppendLine("        body {");
            html.AppendLine("            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;");
            html.AppendLine("            background: #f6f9fc;");
            html.AppendLine("            color: #1a1f36;");
            html.AppendLine("            padding: 32px 20px;");
            html.AppendLine("            min-height: 100vh;");
            html.AppendLine("        }");
            html.AppendLine("        .container {");
            html.AppendLine("            max-width: 1400px;");
            html.AppendLine("            margin: 0 auto;");
            html.AppendLine("        }");
            html.AppendLine("        .header {");
            html.AppendLine("            background: #ffffff;");
            html.AppendLine("            padding: 48px 32px;");
            html.AppendLine("            border-radius: 8px;");
            html.AppendLine("            margin-bottom: 24px;");
            html.AppendLine("            border: 1px solid #e6e6e6;");
            html.AppendLine("        }");
            html.AppendLine("        .header h1 {");
            html.AppendLine("            font-size: 32px;");
            html.AppendLine("            margin-bottom: 8px;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("            color: #0a2540;");
            html.AppendLine("        }");
            html.AppendLine("        .header .subtitle {");
            html.AppendLine("            font-size: 15px;");
            html.AppendLine("            color: #425466;");
            html.AppendLine("        }");
            html.AppendLine("        .stats-grid {");
            html.AppendLine("            display: grid;");
            html.AppendLine("            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));");
            html.AppendLine("            gap: 16px;");
            html.AppendLine("            margin-bottom: 24px;");
            html.AppendLine("        }");
            html.AppendLine("        .stat-card {");
            html.AppendLine("            background: #ffffff;");
            html.AppendLine("            padding: 24px;");
            html.AppendLine("            border-radius: 8px;");
            html.AppendLine("            border: 1px solid #e6e6e6;");
            html.AppendLine("        }");
            html.AppendLine("        .stat-card .label {");
            html.AppendLine("            font-size: 13px;");
            html.AppendLine("            color: #6b7c93;");
            html.AppendLine("            margin-bottom: 8px;");
            html.AppendLine("        }");
            html.AppendLine("        .stat-card .value {");
            html.AppendLine("            font-size: 32px;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("            color: #0a2540;");
            html.AppendLine("        }");
            html.AppendLine("        .stat-card.success .value { color: #0cce6b; }");
            html.AppendLine("        .stat-card.error .value { color: #cd5120; }");
            html.AppendLine("        .stat-card.warning .value { color: #635bff; }");
            html.AppendLine("        .charts-grid {");
            html.AppendLine("            display: grid;");
            html.AppendLine("            grid-template-columns: repeat(auto-fit, minmax(500px, 1fr));");
            html.AppendLine("            gap: 16px;");
            html.AppendLine("            margin-bottom: 24px;");
            html.AppendLine("        }");
            html.AppendLine("        .chart-container {");
            html.AppendLine("            background: #ffffff;");
            html.AppendLine("            padding: 24px;");
            html.AppendLine("            border-radius: 8px;");
            html.AppendLine("            border: 1px solid #e6e6e6;");
            html.AppendLine("        }");
            html.AppendLine("        .chart-container h2 {");
            html.AppendLine("            margin-bottom: 16px;");
            html.AppendLine("            color: #0a2540;");
            html.AppendLine("            font-size: 16px;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("        }");
            html.AppendLine("        .chart-container canvas {");
            html.AppendLine("            max-height: 350px;");
            html.AppendLine("        }");
            html.AppendLine("        .full-width {");
            html.AppendLine("            grid-column: 1 / -1;");
            html.AppendLine("        }");
            html.AppendLine("        .insights {");
            html.AppendLine("            background: #ffffff;");
            html.AppendLine("            padding: 32px;");
            html.AppendLine("            border-radius: 8px;");
            html.AppendLine("            border: 1px solid #e6e6e6;");
            html.AppendLine("            margin-top: 24px;");
            html.AppendLine("        }");
            html.AppendLine("        .insights h2 {");
            html.AppendLine("            margin-bottom: 16px;");
            html.AppendLine("            color: #0a2540;");
            html.AppendLine("            font-size: 20px;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("        }");
            html.AppendLine("        .insights ul {");
            html.AppendLine("            list-style: none;");
            html.AppendLine("            padding-left: 0;");
            html.AppendLine("        }");
            html.AppendLine("        .insights li {");
            html.AppendLine("            padding: 12px 0;");
            html.AppendLine("            border-bottom: 1px solid #f6f9fc;");
            html.AppendLine("            font-size: 14px;");
            html.AppendLine("            line-height: 1.6;");
            html.AppendLine("            color: #425466;");
            html.AppendLine("        }");
            html.AppendLine("        .insights li:last-child {");
            html.AppendLine("            border-bottom: none;");
            html.AppendLine("        }");
            html.AppendLine("        .insights li:before {");
            html.AppendLine("            content: '• ';");
            html.AppendLine("            color: #635bff;");
            html.AppendLine("            font-weight: bold;");
            html.AppendLine("            margin-right: 8px;");
            html.AppendLine("        }");
            html.AppendLine("        .insights .conclusion {");
            html.AppendLine("            margin-top: 16px;");
            html.AppendLine("            padding: 16px;");
            html.AppendLine("            background: #fff4ed;");
            html.AppendLine("            border-left: 3px solid #cd5120;");
            html.AppendLine("            border-radius: 6px;");
            html.AppendLine("            font-size: 14px;");
            html.AppendLine("            color: #0a2540;");
            html.AppendLine("        }");
            html.AppendLine("        .data-table {");
            html.AppendLine("            width: 100%;");
            html.AppendLine("            border-collapse: collapse;");
            html.AppendLine("            margin-top: 16px;");
            html.AppendLine("            font-size: 14px;");
            html.AppendLine("        }");
            html.AppendLine("        .data-table th {");
            html.AppendLine("            background: #fafbfc;");
            html.AppendLine("            padding: 12px;");
            html.AppendLine("            text-align: left;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("            color: #6b7c93;");
            html.AppendLine("            border-bottom: 1px solid #e6e6e6;");
            html.AppendLine("            font-size: 13px;");
            html.AppendLine("        }");
            html.AppendLine("        .data-table td {");
            html.AppendLine("            padding: 12px;");
            html.AppendLine("            border-bottom: 1px solid #f6f9fc;");
            html.AppendLine("            color: #0a2540;");
            html.AppendLine("        }");
            html.AppendLine("        .data-table tr:hover {");
            html.AppendLine("            background: #fafbfc;");
            html.AppendLine("        }");
            html.AppendLine("        .corrupted-row {");
            html.AppendLine("            color: #cd5120;");
            html.AppendLine("        }");
            html.AppendLine("        .valid-row {");
            html.AppendLine("            color: #0a2540;");
            html.AppendLine("        }");
            html.AppendLine("        .status-badge {");
            html.AppendLine("            padding: 4px 10px;");
            html.AppendLine("            border-radius: 4px;");
            html.AppendLine("            font-size: 12px;");
            html.AppendLine("            font-weight: 600;");
            html.AppendLine("        }");
            html.AppendLine("        .footer {");
            html.AppendLine("            text-align: center;");
            html.AppendLine("            margin-top: 32px;");
            html.AppendLine("            padding: 16px;");
            html.AppendLine("            color: #6b7c93;");
            html.AppendLine("            font-size: 13px;");
            html.AppendLine("        }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("    <div class=\"container\">");
            html.AppendLine("        <div class=\"header\">");
            html.AppendLine("            <h1>Tableau de bord d'analyse</h1>");
            html.AppendLine($"            <div class=\"subtitle\">Généré le {DateTime.Now:dd/MM/yyyy} à {DateTime.Now:HH:mm:ss} · {totalFiles} fichiers analysés</div>");
            html.AppendLine("        </div>");

            // Stats cards
            html.AppendLine("        <div class=\"stats-grid\">");
            html.AppendLine($"            <div class=\"stat-card\">");
            html.AppendLine($"                <div class=\"label\">Fichiers totaux</div>");
            html.AppendLine($"                <div class=\"value\">{totalFiles}</div>");
            html.AppendLine($"            </div>");
            html.AppendLine($"            <div class=\"stat-card success\">");
            html.AppendLine($"                <div class=\"label\">Fichiers complets</div>");
            html.AppendLine($"                <div class=\"value\">{completeFiles}</div>");
            html.AppendLine($"            </div>");
            html.AppendLine($"            <div class=\"stat-card error\">");
            html.AppendLine($"                <div class=\"label\">Fichiers corrompus</div>");
            html.AppendLine($"                <div class=\"value\">{corruptedFiles}</div>");
            html.AppendLine($"            </div>");
            html.AppendLine($"            <div class=\"stat-card warning\">");
            html.AppendLine($"                <div class=\"label\">Taille totale</div>");
            html.AppendLine($"                <div class=\"value\">{(totalGB >= 1 ? $"{totalGB:F2} GB" : $"{totalMB:F2} MB")}</div>");
            html.AppendLine($"            </div>");
            html.AppendLine("        </div>");

            // Charts
            html.AppendLine("        <div class=\"charts-grid\">");

            // Complete vs Incomplete pie chart
            html.AppendLine("            <div class=\"chart-container\">");
            html.AppendLine("                <h2>Statut de complétude</h2>");
            html.AppendLine("                <canvas id=\"completenessChart\"></canvas>");
            html.AppendLine("            </div>");

            // Corrupted vs Non-corrupted pie chart
            html.AppendLine("            <div class=\"chart-container\">");
            html.AppendLine("                <h2>Statut de corruption</h2>");
            html.AppendLine("                <canvas id=\"corruptionChart\"></canvas>");
            html.AppendLine("            </div>");

            // File size vs corruption bar chart
            html.AppendLine("            <div class=\"chart-container full-width\">");
            html.AppendLine("                <h2>Taux de corruption par taille de fichier</h2>");
            html.AppendLine("                <canvas id=\"fileSizeChart\"></canvas>");
            html.AppendLine("            </div>");

            // Duration vs corruption bar chart (if data available)
            if (reportsWithDuration.Count > 0)
            {
                html.AppendLine("            <div class=\"chart-container full-width\">");
                html.AppendLine("                <h2>Taux de corruption par durée de vidéo</h2>");
                html.AppendLine("                <canvas id=\"durationChart\"></canvas>");
                html.AppendLine("            </div>");
            }

            // Heatmap: Transfer failure by hour
            html.AppendLine("            <div class=\"chart-container full-width\">");
            html.AppendLine("                <h2>Fréquence des échecs par heure</h2>");
            html.AppendLine("                <canvas id=\"hourlyHeatmap\"></canvas>");
            html.AppendLine("            </div>");

            // Scatter plot: File size vs Playable percentage
            if (scatterData.Count > 0)
            {
                html.AppendLine("            <div class=\"chart-container\">");
                html.AppendLine("                <h2>Taille vs % lisible</h2>");
                html.AppendLine("                <canvas id=\"scatterSizePlayable\"></canvas>");
                html.AppendLine("            </div>");
            }

            // Scatter plot: Last modified hour vs Corruption
            html.AppendLine("            <div class=\"chart-container\">");
            html.AppendLine("                <h2>Heure de modification vs Corruption</h2>");
            html.AppendLine("                <canvas id=\"scatterHourCorruption\"></canvas>");
            html.AppendLine("            </div>");

            // Timeline: Creation vs Last Modified
            html.AppendLine("            <div class=\"chart-container full-width\">");
            html.AppendLine("                <h2>Chronologie de création vs modification</h2>");
            html.AppendLine("                <canvas id=\"timelineChart\"></canvas>");
            html.AppendLine("            </div>");

            html.AppendLine("        </div>");

            // Data table - show all files sorted by corruption percentage (corrupted first)
            html.AppendLine("        <div class=\"chart-container\">");
            html.AppendLine("            <h2>Analyse détaillée des fichiers</h2>");
            html.AppendLine("            <table class=\"data-table\">");
            html.AppendLine("                <thead>");
            html.AppendLine("                    <tr>");
            html.AppendLine("                        <th>Nom du fichier</th>");
            html.AppendLine("                        <th>Taille (MB)</th>");
            html.AppendLine("                        <th>Durée</th>");
            html.AppendLine("                        <th>% lisible</th>");
            html.AppendLine("                        <th>% corrompu</th>");
            html.AppendLine("                        <th>Heure modif.</th>");
            html.AppendLine("                        <th>Statut</th>");
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
                var statusBadge = report.Status.IsCorrupted ? "Corrompu" : "Valide";

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
            html.AppendLine("            <h2>Aperçus et corrélations clés</h2>");
            html.AppendLine("            <ul>");

            // Generate insights
            double corruptionRate = (double)corruptedFiles / totalFiles * 100;
            html.AppendLine($"                <li><strong>Taux de corruption global :</strong> {corruptionRate:F1}% ({corruptedFiles} sur {totalFiles} fichiers sont corrompus ou incomplets)</li>");

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

                    html.AppendLine($"                <li><strong>Fenêtre à haut risque :</strong> {peakRate:F1}% des fichiers modifiés à {top.Hour:D2}:00-{(top.Hour + 1) % 24:D2}:00 sont corrompus (pic d'échecs)</li>");

                    if (timeRanges.Count > 1)
                    {
                        html.AppendLine($"                <li><strong>Fenêtres de risque additionnelles :</strong> {string.Join(", ", timeRanges.Skip(1))}</li>");
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
                html.AppendLine($"                <li><strong>Corrélation taille de fichier :</strong> La plage {highestRiskSize.Key} présente le plus haut risque de corruption à {riskRate:F1}%</li>");
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
                    html.AppendLine($"                <li><strong>Corrélation durée :</strong> Les vidéos de {highestRiskDuration.Key} ont un taux de corruption de {riskRate:F1}%</li>");
                }
            }

            // Average playable percentage for corrupted files
            var corruptedWithDuration = reportsWithDuration.Where(r => r.Status.IsCorrupted).ToList();
            if (corruptedWithDuration.Count > 0)
            {
                double avgPlayable = corruptedWithDuration.Average(r => r.VideoDuration!.PlayablePercentage);
                html.AppendLine($"                <li><strong>Potentiel de récupération :</strong> Les fichiers corrompus conservent {avgPlayable:F1}% de contenu lisible en moyenne</li>");
            }

            // Transfer interruption patterns
            var abruptStops = timelineData
                .Count(x => x.IsCorrupted && x.DurationHours < 1);

            if (abruptStops > 0)
            {
                double abruptRate = (double)abruptStops / corruptedFiles * 100;
                html.AppendLine($"                <li><strong>Schéma d'interruption :</strong> {abruptRate:F1}% des fichiers corrompus montrent des signes d'arrêt brutal (modifiés dans l'heure suivant la création)</li>");
            }

            // Most common issues
            var allIssues = reports
                .SelectMany(r => r.IntegrityAnalysis.Issues)
                .Where(i => !string.IsNullOrEmpty(i))
                .GroupBy(i => i.Contains("Incomplete atom") ? "Atome incomplet" :
                    i.Contains("Missing") ? "Atome manquant" :
                    i.Contains("Gap") ? "Écart après le dernier atome" : i)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToList();

            if (allIssues.Count > 0)
            {
                html.AppendLine($"                <li><strong>Problème structurel le plus courant :</strong> \"{allIssues[0].Key}\" détecté dans {allIssues[0].Count()} fichiers ({(double)allIssues[0].Count() / totalFiles * 100:F1}%)</li>");
            }

            html.AppendLine("            </ul>");

            // Root cause conclusion
            html.AppendLine("            <div class=\"conclusion\">");
            html.AppendLine("                <strong>Cause probable :</strong> ");

            // Determine likely cause based on patterns
            if (peakHours.Any() && (double)peakHours[0].Count / peakHours[0].Total * 100 > corruptionRate * 1.5)
            {
                var topHour = peakHours[0].Hour;
                if (topHour >= 2 && topHour <= 5)
                {
                    html.AppendLine("                Une maintenance programmée ou un arrêt automatique durant les heures nocturnes (03:00-05:00) interrompt probablement les transferts de fichiers en cours.");
                }
                else if (topHour >= 12 && topHour <= 14)
                {
                    html.AppendLine("                La congestion réseau ou la charge serveur durant les heures de pointe (12:00-14:00) peut causer des timeouts et des écritures incomplètes.");
                }
                else
                {
                    html.AppendLine($"                Des échecs systématiques se produisent principalement à {topHour:D2}:00-{(topHour + 1) % 24:D2}:00, suggérant des opérations programmées ou des contraintes de ressources durant cette fenêtre.");
                }
            }
            else if (highestRiskSize.Key != null && highestRiskSize.Key.Contains("GB"))
            {
                html.AppendLine("                Les fichiers volumineux sont disproportionnellement affectés, indiquant des problèmes de timeout réseau ou des tailles de buffer insuffisantes pour les gros transferts.");
            }
            else
            {
                html.AppendLine("                Les transferts de fichiers sont interrompus avant leur achèvement, probablement en raison d'instabilité réseau, de problèmes de stockage ou de plantages d'application durant l'écriture.");
            }

            html.AppendLine("            </div>");
            html.AppendLine("        </div>");

            // Footer
            html.AppendLine("        <div class=\"footer\">");
            html.AppendLine("            <p>Généré par MovFileIntegrityChecker</p>");
            html.AppendLine("        </div>");
            html.AppendLine("    </div>");

            // Chart.js scripts
            html.AppendLine("    <script>");
            html.AppendLine("        Chart.defaults.color = '#6b7c93';");
            html.AppendLine("        Chart.defaults.borderColor = '#e6e6e6';");

            // Completeness chart
            html.AppendLine("        new Chart(document.getElementById('completenessChart'), {");
            html.AppendLine("            type: 'pie',");
            html.AppendLine("            data: {");
            html.AppendLine($"                labels: ['Complet ({completeFiles})', 'Incomplet ({incompleteFiles})'],");
            html.AppendLine("                datasets: [{");
            html.AppendLine($"                    data: [{completeFiles}, {incompleteFiles}],");
            html.AppendLine("                    backgroundColor: ['#0cce6b', '#cd5120'],");
            html.AppendLine("                    borderWidth: 0");
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
            html.AppendLine($"                labels: ['Valide ({nonCorrupted})', 'Corrompu ({corruptedFiles})'],");
            html.AppendLine("                datasets: [{");
            html.AppendLine($"                    data: [{nonCorrupted}, {corruptedFiles}],");
            html.AppendLine("                    backgroundColor: ['#0cce6b', '#cd5120'],");
            html.AppendLine("                    borderWidth: 0");
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
            html.AppendLine("                    label: 'Fichiers totaux',");
            html.AppendLine($"                    data: [{sizeTotals}],");
            html.AppendLine("                    backgroundColor: '#635bff',");
            html.AppendLine("                    borderWidth: 0");
            html.AppendLine("                }, {");
            html.AppendLine("                    label: 'Fichiers corrompus',");
            html.AppendLine($"                    data: [{sizeCorrupted}],");
            html.AppendLine("                    backgroundColor: '#cd5120',");
            html.AppendLine("                    borderWidth: 0");
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
            html.AppendLine("                                    return 'Taux de corruption : ' + percentages[context.dataIndex] + '%';");
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
                html.AppendLine("                    label: 'Fichiers totaux',");
                html.AppendLine($"                    data: [{durationTotals}],");
                html.AppendLine("                    backgroundColor: '#635bff',");
                html.AppendLine("                    borderWidth: 0");
                html.AppendLine("                }, {");
                html.AppendLine("                    label: 'Fichiers corrompus',");
                html.AppendLine($"                    data: [{durationCorrupted}],");
                html.AppendLine("                    backgroundColor: '#cd5120',");
                html.AppendLine("                    borderWidth: 0");
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
                html.AppendLine("                                    return 'Taux de corruption : ' + percentages[context.dataIndex] + '%';");
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
            html.AppendLine("                    label: 'Fichiers modifiés',");
            html.AppendLine($"                    data: [{hourTotalData}],");
            html.AppendLine("                    backgroundColor: '#635bff',");
            html.AppendLine("                    borderWidth: 0");
            html.AppendLine("                }, {");
            html.AppendLine("                    label: 'Fichiers corrompus',");
            html.AppendLine($"                    data: [{hourFailureData}],");
            html.AppendLine("                    backgroundColor: '#cd5120',");
            html.AppendLine("                    borderWidth: 0");
            html.AppendLine("                }]");
            html.AppendLine("            },");
            html.AppendLine("            options: {");
            html.AppendLine("                responsive: true,");
            html.AppendLine("                maintainAspectRatio: true,");
            html.AppendLine("                scales: {");
            html.AppendLine("                    y: { beginAtZero: true, title: { display: true, text: 'Nombre de fichiers' } },");
            html.AppendLine("                    x: { title: { display: true, text: 'Heure de la journée' } }");
            html.AppendLine("                },");
            html.AppendLine("                plugins: {");
            html.AppendLine("                    legend: { position: 'top' },");
            html.AppendLine("                    tooltip: {");
            html.AppendLine("                        callbacks: {");
            html.AppendLine("                            afterLabel: function(context) {");
            html.AppendLine($"                                const percentages = [{hourPercentages}];");
            html.AppendLine("                                if (context.datasetIndex === 1) {");
            html.AppendLine("                                    return 'Taux d\\'échec : ' + percentages[context.dataIndex] + '%';");
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
                html.AppendLine("                    label: 'Fichiers valides (100%)',");
                html.AppendLine($"                    data: [{scatterPointsValid}],");
                html.AppendLine("                    backgroundColor: '#0cce6b',");
                html.AppendLine("                    borderWidth: 0,");
                html.AppendLine("                    pointRadius: 5");
                html.AppendLine("                }, {");
                html.AppendLine("                    label: 'Fichiers corrompus',");
                html.AppendLine($"                    data: [{scatterPointsCorrupted}],");
                html.AppendLine("                    backgroundColor: '#cd5120',");
                html.AppendLine("                    borderWidth: 0,");
                html.AppendLine("                    pointRadius: 5");
                html.AppendLine("                }]");
                html.AppendLine("            },");
                html.AppendLine("            options: {");
                html.AppendLine("                responsive: true,");
                html.AppendLine("                maintainAspectRatio: true,");
                html.AppendLine("                scales: {");
                html.AppendLine("                    x: { title: { display: true, text: 'Taille (MB)' }, beginAtZero: true },");
                html.AppendLine("                    y: { title: { display: true, text: 'Pourcentage lisible (%)' }, beginAtZero: true, max: 100 }");
                html.AppendLine("                },");
                html.AppendLine("                plugins: {");
                html.AppendLine("                    legend: { display: true, position: 'top' },");
                html.AppendLine("                    tooltip: {");
                html.AppendLine("                        callbacks: {");
                html.AppendLine("                            label: function(context) {");
                html.AppendLine("                                return context.raw.label + ' : ' + context.parsed.y.toFixed(1) + '% lisible';");
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
                .Select(g => new
                {
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
            html.AppendLine("                    label: 'Taux de corruption par heure',");
            html.AppendLine($"                    data: [{hourScatterPoints}],");
            html.AppendLine("                    backgroundColor: 'rgba(99, 91, 255, 0.6)',");
            html.AppendLine("                    borderColor: '#635bff',");
            html.AppendLine("                    borderWidth: 0");
            html.AppendLine("                }]");
            html.AppendLine("            },");
            html.AppendLine("            options: {");
            html.AppendLine("                responsive: true,");
            html.AppendLine("                maintainAspectRatio: true,");
            html.AppendLine("                scales: {");
            html.AppendLine("                    x: { title: { display: true, text: 'Heure (0-23)' }, min: 0, max: 23, ticks: { stepSize: 1 } },");
            html.AppendLine("                    y: { title: { display: true, text: 'Taux de corruption (%)' }, beginAtZero: true, max: 100 }");
            html.AppendLine("                },");
            html.AppendLine("                plugins: {");
            html.AppendLine("                    legend: { display: false },");
            html.AppendLine("                    tooltip: {");
            html.AppendLine("                        callbacks: {");
            html.AppendLine("                            label: function(context) {");
            html.AppendLine("                                return 'Heure ' + context.parsed.x + ':00 - ' + context.parsed.y.toFixed(1) + '% corruption';");
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
            html.AppendLine("                    label: 'Création de fichier',");
            html.AppendLine($"                    data: [{timelineCreationData}],");
            html.AppendLine("                    backgroundColor: '#0cce6b',");
            html.AppendLine("                    borderWidth: 0,");
            html.AppendLine("                    pointRadius: 4,");
            html.AppendLine("                    showLine: false");
            html.AppendLine("                }, {");
            html.AppendLine("                    label: 'Modification (corrompus)',");
            html.AppendLine($"                    data: [{timelineModifiedData}],");
            html.AppendLine("                    backgroundColor: '#cd5120',");
            html.AppendLine("                    borderWidth: 0,");
            html.AppendLine("                    pointRadius: 4,");
            html.AppendLine("                    showLine: false");
            html.AppendLine("                }]");
            html.AppendLine("            },");
            html.AppendLine("            options: {");
            html.AppendLine("                responsive: true,");
            html.AppendLine("                maintainAspectRatio: true,");
            html.AppendLine("                scales: {");
            html.AppendLine("                    x: { type: 'time', time: { unit: 'day' }, title: { display: true, text: 'Date et heure' } },");
            html.AppendLine("                    y: { display: false, min: 0, max: 3 }");
            html.AppendLine("                },");
            html.AppendLine("                plugins: {");
            html.AppendLine("                    legend: { position: 'top' },");
            html.AppendLine("                    tooltip: {");
            html.AppendLine("                        callbacks: {");
            html.AppendLine("                            label: function(context) {");
            html.AppendLine("                                return context.dataset.label + ' : ' + context.parsed.x;");
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