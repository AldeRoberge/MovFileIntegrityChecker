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
                    FileName = "ffprobe",
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
                            if (!p.WaitForExit(10000))
                            {
                                try { p.Kill(true); } catch { }
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
                    catch
                    {
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

        public static void CreateJsonReport(FileCheckResult result)
        {
            try
            {
                string filePath = result.FilePath;
                FileInfo fileInfo = new FileInfo(filePath);

                string jsonReportDir = @"T:\SPT\SP\Mont\Projets\3_PRJ\9-ALEXANDRE_DEMERS-ROBERGE\Fichiers Corrompus";

                if (!Directory.Exists(jsonReportDir))
                {
                    jsonReportDir = Path.Combine(Path.GetDirectoryName(filePath) ?? ".", "Reports");
                    if (!Directory.Exists(jsonReportDir))
                        Directory.CreateDirectory(jsonReportDir);
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

                ConsoleHelper.WriteSuccess($"✅ JSON report saved: {jsonReportPath}");
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteWarning($"⚠️ Unable to create JSON report: {ex.Message}");
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
                sb.AppendLine("    </style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");
                sb.AppendLine("    <div class=\"container\">");

                sb.AppendLine("        <div class=\"header\">");
                sb.AppendLine("            <h1>Rapport d'intégrité</h1>");
                sb.AppendLine($"            <div class=\"subtitle\">{System.Security.SecurityElement.Escape(Path.GetFileName(filePath))}</div>");
                sb.AppendLine("            <div class=\"warning\">");
                sb.AppendLine("                <strong>Fichier potentiellement incomplet</strong>");
                sb.AppendLine("                Ce fichier a été détecté comme potentiellement incomplet. ");
                sb.AppendLine("                Veuillez le vérifier dans VLC pour confirmer sa lecture.");
                sb.AppendLine("            </div>");
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
                ConsoleHelper.WriteWarning($"Impossible d'écrire le rapport d'erreur : {ex.Message}");
            }
        }
    }
}
