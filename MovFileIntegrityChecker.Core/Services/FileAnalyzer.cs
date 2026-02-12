// This is where the magic happens - we dig into the MOV/MP4 file structure.
// It reads the atoms (the building blocks of video files) and checks if everything's intact.
// Think of it like a health checkup for your video files, but without the waiting room.

using System.Text;
using MovFileIntegrityChecker.Core.Models;
using MovFileIntegrityChecker.Core.Utilities;
using MovFileIntegrityChecker.Core.Constants;

namespace MovFileIntegrityChecker.Core.Services
{
    public class FileAnalyzer
    {

        public FileCheckResult CheckFileIntegrity(string filePath)
        {
            var result = new FileCheckResult
            {
                FilePath = filePath,
            };

            if (!File.Exists(filePath))
            {
                result.Issues.Add("File does not exist");
                return result;
            }

            try
            {
                // Check if the file is locked by another process before we try to read it
                if (!FileSecurityHelper.TryOpenFile(filePath, out string? lockError))
                {
                    result.Issues.Add($"File is in use or locked: {lockError}");
                    return result;
                }

                // Open in read-only mode with sharing allowed - we're just looking, not touching
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long fileLength = fs.Length;
                result.FileSize = fileLength;

                if (fileLength < 8)
                {
                    result.Issues.Add("File too small to be a valid MOV (< 8 bytes)");
                    return result;
                }

                // Parse all atoms in the file
                bool hasStructuralErrors = false;
                bool hasIncompleteAtoms = false;
                long position = 0;

                while (position < fileLength)
                {
                    fs.Position = position;

                    // Read atom size (4 bytes, big-endian)
                    byte[] sizeBytes = new byte[4];
                    int bytesRead = fs.Read(sizeBytes, 0, 4);
                    if (bytesRead < 4)
                    {
                        result.Issues.Add($"Incomplete atom header at offset {position:N0}");
                        hasIncompleteAtoms = true;
                        break;
                    }

                    long atomSize = ByteHelper.ReadBigEndianUInt32(sizeBytes);

                    // Read atom type (4 bytes)
                    byte[] typeBytes = new byte[4];
                    bytesRead = fs.Read(typeBytes, 0, 4);
                    if (bytesRead < 4)
                    {
                        result.Issues.Add($"Incomplete atom type at offset {position:N0}");
                        hasIncompleteAtoms = true;
                        break;
                    }

                    string atomType = Encoding.ASCII.GetString(typeBytes);

                    // Handle extended size (size == 1)
                    long headerSize = 8;
                    if (atomSize == 1)
                    {
                        byte[] extSizeBytes = new byte[8];
                        bytesRead = fs.Read(extSizeBytes, 0, 8);
                        if (bytesRead < 8)
                        {
                            result.Issues.Add($"Incomplete extended size for atom '{atomType}' at offset {position:N0}");
                            hasIncompleteAtoms = true;
                            break;
                        }

                        atomSize = ByteHelper.ReadBigEndianUInt64(extSizeBytes);
                        headerSize = 16;
                    }
                    // Handle size == 0 (atom extends to end of file)
                    else if (atomSize == 0)
                    {
                        atomSize = fileLength - position;
                    }

                    // Validate atom size
                    if (atomSize < headerSize)
                    {
                        result.Issues.Add($"Invalid atom size ({atomSize}) at offset {position:N0} for type '{atomType}'");
                        hasStructuralErrors = true;
                        break;
                    }

                    // Check if atom extends beyond file
                    bool isComplete = (position + atomSize) <= fileLength;

                    var atom = new AtomInfo
                    {
                        Type = atomType,
                        Size = atomSize,
                        Offset = position,
                        IsComplete = isComplete
                    };
                    result.Atoms.Add(atom);

                    if (!isComplete)
                    {
                        long available = fileLength - position;
                        long missing = atomSize - available;
                        result.Issues.Add($"Incomplete atom '{atomType}' at offset {position:N0}: Expected {atomSize:N0} bytes, available {available:N0} bytes, missing {missing:N0} bytes ({(missing * 100.0 / atomSize):F1}%)");
                        hasIncompleteAtoms = true;
                        break;
                    }

                    // Warn about unknown atom types
                    if (!AtomConstants.ValidAtomTypes.Contains(atomType) && !ByteHelper.IsAsciiPrintable(atomType))
                    {
                        result.Issues.Add($"Unknown/invalid atom type '{atomType}' at offset {position:N0}");
                    }

                    position += atomSize;
                }

                result.BytesValidated = position;

                // Check for required atoms
                bool hasFtyp = result.Atoms.Any(a => a.Type == "ftyp");
                bool hasMoov = result.Atoms.Any(a => a.Type == "moov");
                bool hasMdat = result.Atoms.Any(a => a.Type == "mdat");

                if (!hasFtyp && result.Atoms.Count > 0)
                {
                    result.Issues.Add("Missing 'ftyp' atom (file type header)");
                }

                if (!hasMoov && result.Atoms.Count > 0)
                {
                    result.Issues.Add("Missing 'moov' atom (metadata)");
                }

                if (!hasMdat && result.Atoms.Count > 0)
                {
                    result.Issues.Add("Missing 'mdat' atom (media data)");
                }

                // Validate ftyp is first (if present)
                if (hasFtyp && result.Atoms.Count > 0 && result.Atoms[0].Type != "ftyp")
                {
                    result.Issues.Add($"'ftyp' atom should be first, but found at offset {result.Atoms.First(a => a.Type == "ftyp").Offset:N0}");
                }

                // Check last atom alignment
                if (result.Atoms.Count > 0)
                {
                    var lastAtom = result.Atoms.Last();
                    long declaredEnd = lastAtom.Offset + lastAtom.Size;

                    if (lastAtom.IsComplete && declaredEnd != fileLength)
                    {
                        long gap = fileLength - declaredEnd;
                        result.Issues.Add($"Gap of {gap:N0} bytes after last atom '{lastAtom.Type}' at offset {declaredEnd:N0}");
                    }
                }

                // Final verdict
                if (hasStructuralErrors || hasIncompleteAtoms || result.Atoms.Count == 0)
                {
                    result.Issues.Add("File structure is invalid or incomplete");
                }

                // Get video duration using ffprobe
                var videoAnalyzer = new VideoAnalyzer();
                result.TotalDuration = videoAnalyzer.GetVideoDuration(filePath);

                // Estimate playable duration based on bytes validated
                if (result.TotalDuration > 0 && result.FileSize > 0)
                {
                    double completionRatio = (double)result.BytesValidated / result.FileSize;
                    result.PlayableDuration = result.TotalDuration * completionRatio;
                }
                else
                {
                    result.PlayableDuration = 0;
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add($"Error reading file: {ex.Message}");
            }

            return result;
        }
    }
}

