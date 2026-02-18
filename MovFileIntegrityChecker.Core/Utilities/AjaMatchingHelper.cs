using System.Text.RegularExpressions;

namespace MovFileIntegrityChecker.Core.Utilities
{
    public static class AjaMatchingHelper
    {
        /// <summary>
        /// Normalizes a filename by removing common suffixes (like _1, _2, _5) 
        /// that might be added to local corrupted versions of AJA clips.
        /// Example: SCL1416H6-voxpop_6_1_5.mov -> SCL1416H6-voxpop_6_1.mov
        /// </summary>
        public static string GetBaseAjaName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return fileName;

            // Pattern: match base name, then an underscore followed by digits, then .mov extension
            // We use a non-greedy match for the base to ensure we only strip the LAST suffix
            var match = Regex.Match(fileName, @"^(.*?)_\d+(\.mov)$", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return match.Groups[1].Value + match.Groups[2].Value;
            }

            return fileName;
        }

        /// <summary>
        /// Checks if a local filename matches an AJA clip name, 
        /// potentially ignoring a suffix on the local filename.
        /// </summary>
        public static bool IsMatch(string localFileName, string ajaClipName)
        {
            if (string.Equals(localFileName, ajaClipName, StringComparison.OrdinalIgnoreCase))
                return true;

            var normalizedLocal = GetBaseAjaName(localFileName);
            return string.Equals(normalizedLocal, ajaClipName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
