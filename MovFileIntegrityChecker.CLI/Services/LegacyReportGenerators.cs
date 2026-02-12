// Wrapper for the old HTML report generator code that we haven't refactored yet.
// It works fine, but the code is pretty gnarly. We'll clean it up eventually.
// For now, this just keeps it separate so the main code doesn't get messy.

using MovFileIntegrityChecker.Core.Models;

namespace MovFileIntegrityChecker.CLI.Services
{
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


