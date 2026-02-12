using System.Globalization;
using System.Text;
using MovFileIntegrityChecker.Core.Models;
using MovFileIntegrityChecker.Core.Utilities;

namespace MovFileIntegrityChecker.Core.Services
{
    public static class GlobalReportGenerator
    {
        public static void GenerateGlobalHtmlReport(List<JsonCorruptionReport> reports, string outputDir)
        {
            ConsoleHelper.WriteInfo("Analyzing data and generating global report...\n");

            // Calculate statistics
            int totalFiles = reports.Count;
            int corruptedFiles = reports.Count(r => r.Status.IsCorrupted);
            int completeFiles = reports.Count(r => r.Status.IsComplete);
            int incompleteFiles = totalFiles - completeFiles; // same as corrupted in this context

            long totalBytes = reports.Sum(r => r.FileMetadata.FileSizeBytes);
            double totalMB = totalBytes / (1024.0 * 1024.0);
            double totalGB = totalBytes / (1024.0 * 1024.0 * 1024.0);

            // Duration analysis
            var reportsWithDuration = reports.Where(r => r.VideoDuration != null).ToList();
            double totalDurationSeconds = reportsWithDuration.Sum(r => r.VideoDuration!.TotalDurationSeconds);
            double playableDurationSeconds = reportsWithDuration.Sum(r => r.VideoDuration!.PlayableDurationSeconds);
            double missingDurationSeconds = reportsWithDuration.Sum(r => r.VideoDuration!.MissingDurationSeconds);

            // Hour-based heatmap data
            var hourlyFailures = new int[24];
            var hourlyTotal = new int[24];
            foreach (var report in reports)
            {
                int hour = report.FileMetadata.LastModifiedTimeUtc.ToLocalTime().Hour;
                hourlyTotal[hour]++;
                if (report.Status.IsCorrupted)
                    hourlyFailures[hour]++;
            }

            // Scatter plot data (Size vs Integrity) - simplified for JS injection
            var scatterData = new StringBuilder();
            foreach (var r in reports)
            {
                double sizeMB = r.FileMetadata.FileSizeMB;
                // If corrupted, use 0 or some indicator? Actually let's use 1 for good, 0 for bad on Y axis?
                // Or maybe Y axis is % valid?
                double validPercent = r.IntegrityAnalysis.ValidationPercentage;
                string statusColor = r.Status.IsCorrupted ? "#cd5120" : "#0cce6b";
                scatterData.Append($"{{ x: {sizeMB.ToString(CultureInfo.InvariantCulture)}, y: {validPercent.ToString(CultureInfo.InvariantCulture)}, status: '{(r.Status.IsCorrupted ? "Corrompu" : "Valide")}', name: '{r.FileMetadata.FileName.Replace("'", "\\'")}' }},");
            }

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"fr\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("    <title>Rapport Global - MovFileIntegrityChecker</title>");
            sb.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        :root { --primary: #635bff; --success: #0cce6b; --danger: #cd5120; --bg: #f6f9fc; --card-bg: #ffffff; --text: #0a2540; --text-muted: #6b7c93; }");
            sb.AppendLine("        * { margin: 0; padding: 0; box-sizing: border-box; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }");
            sb.AppendLine("        body { background: var(--bg); color: var(--text); padding-bottom: 40px; }");
            sb.AppendLine("        .container { max-width: 1200px; margin: 0 auto; padding: 0 20px; }");
            sb.AppendLine("        .header { background: var(--card-bg); padding: 40px 0; border-bottom: 1px solid #e6e6e6; margin-bottom: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.02); }");
            sb.AppendLine("        .header-content { display: flex; justify-content: space-between; align-items: center; }");
            sb.AppendLine("        h1 { font-size: 28px; font-weight: 700; color: var(--text); }");
            sb.AppendLine("        .subtitle { color: var(--text-muted); margin-top: 8px; }");
            
            sb.AppendLine("        .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 24px; margin-bottom: 40px; }");
            sb.AppendLine("        .stat-card { background: var(--card-bg); padding: 24px; border-radius: 8px; box-shadow: 0 4px 6px rgba(50,50,93,0.11), 0 1px 3px rgba(0,0,0,0.08); }");
            sb.AppendLine("        .stat-value { font-size: 32px; font-weight: 700; color: var(--primary); margin: 8px 0; }");
            sb.AppendLine("        .stat-label { color: var(--text-muted); font-size: 14px; font-weight: 500; text-transform: uppercase; letter-spacing: 0.5px; }");
            sb.AppendLine("        .stat-sub { font-size: 13px; color: var(--text-muted); margin-top: 4px; }");
            sb.AppendLine("        .stat-card.danger .stat-value { color: var(--danger); }");
            sb.AppendLine("        .stat-card.success .stat-value { color: var(--success); }");

            sb.AppendLine("        .charts-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(500px, 1fr)); gap: 24px; margin-bottom: 40px; }");
            sb.AppendLine("        .chart-card { background: var(--card-bg); padding: 24px; border-radius: 8px; box-shadow: 0 4px 6px rgba(50,50,93,0.11), 0 1px 3px rgba(0,0,0,0.08); height: 400px; }");
            sb.AppendLine("        .chart-title { font-size: 18px; font-weight: 600; margin-bottom: 20px; color: var(--text); }");

            sb.AppendLine("        .table-card { background: var(--card-bg); border-radius: 8px; box-shadow: 0 4px 6px rgba(50,50,93,0.11), 0 1px 3px rgba(0,0,0,0.08); overflow: hidden; }");
            sb.AppendLine("        table { width: 100%; border-collapse: collapse; }");
            sb.AppendLine("        th { background: #f7fafc; text-align: left; padding: 16px 24px; font-size: 12px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; border-bottom: 1px solid #e6e6e6; }");
            sb.AppendLine("        td { padding: 16px 24px; border-bottom: 1px solid #f6f9fc; vertical-align: middle; font-size: 14px; }");
            sb.AppendLine("        tr:hover td { background: #fbfcfe; }");
            sb.AppendLine("        .status-badge { display: inline-flex; align-items: center; padding: 4px 12px; border-radius: 9999px; font-size: 12px; font-weight: 600; }");
            sb.AppendLine("        .status-badge.success { background: #d4edda; color: #155724; }");
            sb.AppendLine("        .status-badge.danger { background: #fff4ed; color: #cd5120; }");
            sb.AppendLine("        .progress-mini { width: 100px; height: 6px; background: #e6e6e6; border-radius: 3px; overflow: hidden; display: inline-block; vertical-align: middle; margin-right: 8px; }");
            sb.AppendLine("        .progress-mini-fill { height: 100%; background: var(--primary); }");
            
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            
            sb.AppendLine("    <div class=\"header\">");
            sb.AppendLine("        <div class=\"container\">");
            sb.AppendLine("            <div class=\"header-content\">");
            sb.AppendLine("                <div>");
            sb.AppendLine("                    <h1>Rapport Global d'Intégrité</h1>");
            sb.AppendLine($"                    <div class=\"subtitle\">Généré le {DateTime.Now:dd MMMM yyyy à HH:mm} | {totalFiles} fichiers analysés</div>");
            sb.AppendLine("                </div>");
            sb.AppendLine("            </div>");
            sb.AppendLine("        </div>");
            sb.AppendLine("    </div>");

            sb.AppendLine("    <div class=\"container\">");
            
            // Stats Grid
            sb.AppendLine("        <div class=\"stats-grid\">");
            
            // Total Files
            sb.AppendLine("            <div class=\"stat-card\">");
            sb.AppendLine("                <div class=\"stat-label\">Total Fichiers</div>");
            sb.AppendLine($"                <div class=\"stat-value\">{totalFiles}</div>");
            sb.AppendLine($"                <div class=\"stat-sub\">{totalGB:F2} GB analysés</div>");
            sb.AppendLine("            </div>");

            // Integrity Score (Files OK %)
            double integrityScore = totalFiles > 0 ? (double)completeFiles / totalFiles * 100.0 : 0;
            sb.AppendLine($"            <div class=\"stat-card {(integrityScore == 100 ? "success" : "danger")}\">");
            sb.AppendLine("                <div class=\"stat-label\">Score d'Intégrité</div>");
            sb.AppendLine($"                <div class=\"stat-value\">{integrityScore:F1}%</div>");
            sb.AppendLine($"                <div class=\"stat-sub\">{completeFiles} fichiers sains</div>");
            sb.AppendLine("            </div>");

            // Corrupted Files
            sb.AppendLine("            <div class=\"stat-card danger\">");
            sb.AppendLine("                <div class=\"stat-label\">Fichiers Corrompus</div>");
            sb.AppendLine($"                <div class=\"stat-value\">{corruptedFiles}</div>");
            sb.AppendLine($"                <div class=\"stat-sub\">Action requise</div>");
            sb.AppendLine("            </div>");

            // Duration Loss
            if (reportsWithDuration.Any())
            {
                double lossPercent = totalDurationSeconds > 0 ? (missingDurationSeconds / totalDurationSeconds) * 100.0 : 0;
                sb.AppendLine("            <div class=\"stat-card\">");
                sb.AppendLine("                <div class=\"stat-label\">Perte de Contenu</div>");
                sb.AppendLine($"                <div class=\"stat-value\">{lossPercent:F2}%</div>");
                sb.AppendLine($"                <div class=\"stat-sub\">{ConsoleHelper.FormatDuration(missingDurationSeconds)} manquants</div>");
                sb.AppendLine("            </div>");
            }

            sb.AppendLine("        </div>"); // End stats grid

            // Charts Grid
            sb.AppendLine("        <div class=\"charts-grid\">");
            sb.AppendLine("            <div class=\"chart-card\">");
            sb.AppendLine("                <div class=\"chart-title\">Répartition des résultats</div>");
            sb.AppendLine("                <canvas id=\"pieChart\"></canvas>");
            sb.AppendLine("            </div>");
            sb.AppendLine("            <div class=\"chart-card\">");
            sb.AppendLine("                <div class=\"chart-title\">Corruptions par heure (Upload/Modif)</div>");
            sb.AppendLine("                <canvas id=\"barChart\"></canvas>");
            sb.AppendLine("            </div>");
            sb.AppendLine("        </div>");
            
            sb.AppendLine("        <div class=\"charts-grid\">");
            sb.AppendLine("            <div class=\"chart-card\" style=\"grid-column: span 2;\">");
            sb.AppendLine("                <div class=\"chart-title\">Analyse: Taille vs Intégrité (%)</div>");
            sb.AppendLine("                <canvas id=\"scatterChart\"></canvas>");
            sb.AppendLine("            </div>");
            sb.AppendLine("        </div>");

            // Detailed Table
            sb.AppendLine("        <div class=\"table-card\">");
            sb.AppendLine("            <table>");
            sb.AppendLine("                <thead>");
            sb.AppendLine("                    <tr>");
            sb.AppendLine("                        <th>Fichier</th>");
            sb.AppendLine("                        <th>Taille</th>");
            sb.AppendLine("                        <th>Validation</th>");
            sb.AppendLine("                        <th>Durée (Total / Lisible)</th>");
            sb.AppendLine("                        <th>Statut</th>");
            sb.AppendLine("                    </tr>");
            sb.AppendLine("                </thead>");
            sb.AppendLine("                <tbody>");

            // Sort by corrupted first, then name
            var sortedReports = reports.OrderByDescending(r => r.Status.IsCorrupted).ThenBy(r => r.FileMetadata.FileName);

            foreach (var report in sortedReports)
            {
                string statusClass = report.Status.IsCorrupted ? "danger" : "success";
                string statusText = report.Status.IsCorrupted ? "Corrompu" : "Valide";
                string rowClass = report.Status.IsCorrupted ? "style=\"background-color: #fff4ed;\"" : "";

                sb.AppendLine($"                <tr {rowClass}>");
                sb.AppendLine($"                    <td><strong>{System.Security.SecurityElement.Escape(report.FileMetadata.FileName)}</strong><br><span style=\"color: #6b7c93; font-size: 12px;\">{report.FileMetadata.FullPath}</span></td>");
                sb.AppendLine($"                    <td>{report.FileMetadata.FileSizeMB:F2} MB</td>");
                sb.AppendLine($"                    <td>{report.IntegrityAnalysis.ValidationPercentage:F1}%</td>");
                
                string durationInfo = "-";
                if (report.VideoDuration != null)
                {
                    durationInfo = $"{ConsoleHelper.FormatDuration(report.VideoDuration.TotalDurationSeconds)} / {ConsoleHelper.FormatDuration(report.VideoDuration.PlayableDurationSeconds)}";
                    if (report.Status.IsCorrupted)
                    {
                         durationInfo += $" <span style=\"color: #cd5120;\">(-{ConsoleHelper.FormatDuration(report.VideoDuration.MissingDurationSeconds)})</span>";
                    }
                }
                sb.AppendLine($"                    <td>{durationInfo}</td>");
                
                sb.AppendLine($"                    <td><span class=\"status-badge {statusClass}\">{statusText}</span></td>");
                sb.AppendLine("                </tr>");
            }

            sb.AppendLine("                </tbody>");
            sb.AppendLine("            </table>");
            sb.AppendLine("        </div>"); // End table card
            sb.AppendLine("    </div>"); // End container

            // Scripts
            sb.AppendLine("    <script>");
            
            // Pie Chart
            sb.AppendLine("    const ctxPie = document.getElementById('pieChart').getContext('2d');");
            sb.AppendLine("    new Chart(ctxPie, {");
            sb.AppendLine("        type: 'doughnut',");
            sb.AppendLine("        data: {");
            sb.AppendLine("            labels: ['Sains', 'Corrompus'],");
            sb.AppendLine("            datasets: [{");
            sb.AppendLine($"                data: [{completeFiles}, {corruptedFiles}],");
            sb.AppendLine("                backgroundColor: ['#0cce6b', '#cd5120'],");
            sb.AppendLine("                borderWidth: 0");
            sb.AppendLine("            }]");
            sb.AppendLine("        },");
            sb.AppendLine("        options: { cutout: '70%', responsive: true, plugins: { legend: { position: 'bottom' } } }");
            sb.AppendLine("    });");

            // Bar Chart
            string hourlyTotalJs = string.Join(",", hourlyTotal);
            string hourlyFailuresJs = string.Join(",", hourlyFailures);
            
            sb.AppendLine("    const ctxBar = document.getElementById('barChart').getContext('2d');");
            sb.AppendLine("    new Chart(ctxBar, {");
            sb.AppendLine("        type: 'bar',");
            sb.AppendLine("        data: {");
            sb.AppendLine("            labels: Array.from({length: 24}, (_, i) => i + 'h'),");
            sb.AppendLine("            datasets: [");
            sb.AppendLine("                { label: 'Total Fichiers', data: [" + hourlyTotalJs + "], backgroundColor: '#e6e6e6' },");
            sb.AppendLine("                { label: 'Corrompus', data: [" + hourlyFailuresJs + "], backgroundColor: '#cd5120' }");
            sb.AppendLine("            ]");
            sb.AppendLine("        },");
            sb.AppendLine("        options: { responsive: true, scales: { x: { stacked: true }, y: { stacked: true } } }");
            sb.AppendLine("    });");
            
            // Scatter Chart
            sb.AppendLine("    const ctxScatter = document.getElementById('scatterChart').getContext('2d');");
            sb.AppendLine("    new Chart(ctxScatter, {");
            sb.AppendLine("        type: 'scatter',");
            sb.AppendLine("        data: {");
            sb.AppendLine("            datasets: [{");
            sb.AppendLine("                label: 'Fichiers',");
            sb.AppendLine("                data: [" + scatterData.ToString().TrimEnd(',') + "],");
            sb.AppendLine("                backgroundColor: function(context) { return context.raw ? (context.raw.status === 'Corrompu' ? '#cd5120' : '#0cce6b') : '#ccc'; }");
            sb.AppendLine("            }]");
            sb.AppendLine("        },");
            sb.AppendLine("        options: {");
            sb.AppendLine("            scales: {");
            sb.AppendLine("                x: { type: 'linear', position: 'bottom', title: { display: true, text: 'Taille (MB)' } },");
            sb.AppendLine("                y: { title: { display: true, text: '% Validé' }, min: 0, max: 105 }");
            sb.AppendLine("            },");
            sb.AppendLine("            plugins: {");
            sb.AppendLine("                tooltip: {");
            sb.AppendLine("                    callbacks: {");
            sb.AppendLine("                        label: function(context) {");
            sb.AppendLine("                            return context.raw.name + ': ' + context.raw.y + '% (' + context.raw.x + ' MB)';");
            sb.AppendLine("                        }");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    });");
            
            sb.AppendLine("    </script>");

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            
            string reportPath = Path.Combine(outputDir, $"Global_Report_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            ConsoleHelper.WriteSuccess($"✅ Rapport global créé: {reportPath}");
        }
    }
}
