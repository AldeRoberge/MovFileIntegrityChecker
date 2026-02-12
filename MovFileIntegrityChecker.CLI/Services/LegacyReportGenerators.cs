using System.Text.Json;
using MovFileIntegrityChecker.Core.Models;
using MovFileIntegrityChecker.Core.Services;
using MovFileIntegrityChecker.Core.Utilities;

namespace MovFileIntegrityChecker.CLI.Services
{
    public static class LegacyReportGenerators
    {
        public static void CreateErrorReport(FileCheckResult result)
        {
            // Delegate to the new Core implementation
            LegacyReportGenerator.CreateErrorReport(result);
        }

        public static void RunGlobalAnalysis()
        {
            // Replicating the logic to find the report directory
            string rootPath = @"T:\SPT\SP\Mont\Projets\3_PRJ\9-ALEXANDRE_DEMERS-ROBERGE\Fichiers Corrompus";

            if (!Directory.Exists(rootPath))
            {
                rootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            }
            
            if (!Directory.Exists(rootPath))
            {
                ConsoleHelper.WriteWarning($"Report directory not found: {rootPath}");
                return;
            }

            ConsoleHelper.WriteInfo($"Searching for JSON reports in: {rootPath}");
            var jsonFiles = Directory.GetFiles(rootPath, "*_report.json", SearchOption.AllDirectories);
            
            if (jsonFiles.Length == 0)
            {
                ConsoleHelper.WriteWarning("No JSON reports found.");
                return;
            }

            var reports = new List<JsonCorruptionReport>();
            foreach (var file in jsonFiles)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var report = JsonSerializer.Deserialize<JsonCorruptionReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (report != null)
                    {
                        reports.Add(report);
                    }
                }
                catch
                {
                    // Ignore parsing errors
                }
            }

            // Generate the global HTML report
            GlobalReportGenerator.GenerateGlobalHtmlReport(reports, rootPath);
        }
    }
}
