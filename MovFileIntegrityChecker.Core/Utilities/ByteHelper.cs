// Helper methods for working with byte arrays and binary data.
// These are used when parsing MOV/MP4 file structures which use big-endian byte order.

namespace MovFileIntegrityChecker.Core.Utilities
{
    public static class ByteHelper
    {
        /// <summary>
        /// Reads a 32-bit unsigned integer from a byte array in big-endian format.
        /// </summary>
        public static uint ReadBigEndianUInt32(byte[] data)
        {
            return ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
        }

        /// <summary>
        /// Reads a 64-bit unsigned integer from a byte array in big-endian format.
        /// </summary>
        public static long ReadBigEndianUInt64(byte[] data)
        {
            return ((long)data[0] << 56) | ((long)data[1] << 48) | ((long)data[2] << 40) | ((long)data[3] << 32) |
                   ((long)data[4] << 24) | ((long)data[5] << 16) | ((long)data[6] << 8) | data[7];
        }

        /// <summary>
        /// Checks if a string contains only ASCII printable characters (32-126).
        /// </summary>
        public static bool IsAsciiPrintable(string str)
        {
            return str.All(c => c >= 32 && c <= 126);
        }
    }
}

