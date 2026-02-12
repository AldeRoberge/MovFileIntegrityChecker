// Central definition of MOV/MP4 atom types used throughout the application.
// This ensures consistency and eliminates duplication across multiple files.

namespace MovFileIntegrityChecker.Core.Constants
{
    public static class AtomConstants
    {
        /// <summary>
        /// Common MOV/MP4 atom types that are recognized as valid in the file structure.
        /// </summary>
        public static readonly HashSet<string> ValidAtomTypes = new()
        {
            "ftyp", "moov", "mdat", "free", "skip", "wide", "pnot",
            "mvhd", "trak", "tkhd", "mdia", "mdhd", "hdlr", "minf",
            "vmhd", "smhd", "dinf", "stbl", "stsd", "stts", "stsc",
            "stsz", "stco", "co64", "edts", "elst", "udta", "meta"
        };
    }
}

