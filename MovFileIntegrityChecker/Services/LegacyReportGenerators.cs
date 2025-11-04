using MovFileIntegrityChecker.Models;

namespace MovFileIntegrityChecker.Services
{
    /// <summary>
    /// Legacy HTML and Dashboard report generators.
    /// These are large methods that will be refactored in Phase 2.
    /// For now, they are kept separate from the main Program class.
    /// </summary>
    public static class LegacyReportGenerators
    {
        public static void CreateErrorReport(FileCheckResult result)
        {
            // Delegate to the legacy implementation in Program.Legacy.cs
            // This will be extracted and refactored in Phase 2
            MovIntegrityChecker.CreateErrorReport(result);
        }

        public static void RunGlobalAnalysis()
        {
            // Delegate to the legacy implementation in Program.Legacy.cs
            // This will be extracted and refactored in Phase 2
            MovIntegrityChecker.RunGlobalAnalysis();
        }
    }
}

