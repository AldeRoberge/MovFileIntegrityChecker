// Quick test program to demonstrate the new security features.
// Run this to verify that file lock detection and read-only access work correctly.

using MovFileIntegrityChecker.Utilities;

namespace MovFileIntegrityChecker.Tests
{
    public class SecurityTests
    {
        public static void RunTests()
        {
            Console.WriteLine("=== MovFileIntegrityChecker Security Features Test ===\n");

            // Test 1: Check if FileSecurityHelper detects locked files
            TestFileLockDetection();

            // Test 2: Verify read-only access works
            TestReadOnlyAccess();

            // Test 3: Test path validation
            TestPathValidation();

            Console.WriteLine("\n=== All Tests Complete ===");
        }

        private static void TestFileLockDetection()
        {
            Console.WriteLine("Test 1: File Lock Detection");
            Console.WriteLine("-----------------------------");

            string testFile = Path.Combine(Path.GetTempPath(), "test_lock.mp4");
            
            try
            {
                // Create a test file
                File.WriteAllText(testFile, "test content");

                // Test 1a: File is accessible
                if (FileSecurityHelper.TryOpenFile(testFile, out string? error))
                {
                    Console.WriteLine("✅ PASS: Can open unlocked file");
                }
                else
                {
                    Console.WriteLine($"❌ FAIL: Should be able to open file. Error: {error}");
                }

                // Test 1b: File is locked
                using (var lockStream = File.Open(testFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    if (!FileSecurityHelper.TryOpenFile(testFile, out error))
                    {
                        Console.WriteLine($"✅ PASS: Detected locked file. Error: {error}");
                    }
                    else
                    {
                        Console.WriteLine("❌ FAIL: Should detect locked file");
                    }
                }

                // Test 1c: File is accessible again after unlock
                if (FileSecurityHelper.TryOpenFile(testFile, out error))
                {
                    Console.WriteLine("✅ PASS: Can open file after unlock");
                }
                else
                {
                    Console.WriteLine($"❌ FAIL: Should be able to open file after unlock. Error: {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FAIL: Exception during test: {ex.Message}");
            }
            finally
            {
                if (File.Exists(testFile))
                {
                    try { File.Delete(testFile); } catch { }
                }
            }

            Console.WriteLine();
        }

        private static void TestReadOnlyAccess()
        {
            Console.WriteLine("Test 2: Read-Only Access");
            Console.WriteLine("------------------------");

            string testFile = Path.Combine(Path.GetTempPath(), "test_readonly.mp4");

            try
            {
                // Create a test file
                File.WriteAllText(testFile, "test content for read-only");

                // Make it read-only
                File.SetAttributes(testFile, FileAttributes.ReadOnly);

                // Test 2a: Should still be able to open for reading
                if (FileSecurityHelper.TryOpenFile(testFile, out string? error))
                {
                    Console.WriteLine("✅ PASS: Can open read-only file for reading");
                }
                else
                {
                    Console.WriteLine($"❌ FAIL: Should be able to read read-only file. Error: {error}");
                }

                // Test 2b: Verify we're using FileShare.Read (allows concurrent reads)
                using (var stream1 = FileSecurityHelper.OpenSecureReadOnlyStream(testFile))
                using (var stream2 = FileSecurityHelper.OpenSecureReadOnlyStream(testFile))
                {
                    Console.WriteLine("✅ PASS: Multiple concurrent read streams allowed (FileShare.Read works)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FAIL: Exception during test: {ex.Message}");
            }
            finally
            {
                if (File.Exists(testFile))
                {
                    try 
                    { 
                        File.SetAttributes(testFile, FileAttributes.Normal);
                        File.Delete(testFile); 
                    } 
                    catch { }
                }
            }

            Console.WriteLine();
        }

        private static void TestPathValidation()
        {
            Console.WriteLine("Test 3: Path Validation");
            Console.WriteLine("-----------------------");

            string testFile = Path.Combine(Path.GetTempPath(), "test_path.mp4");

            try
            {
                // Create a test file
                File.WriteAllText(testFile, "test content for path validation");

                // Test 3a: Valid path
                if (FileSecurityHelper.IsPathSafe(testFile))
                {
                    Console.WriteLine("✅ PASS: Valid path accepted");
                }
                else
                {
                    Console.WriteLine("❌ FAIL: Valid path should be accepted");
                }

                // Test 3b: Path with directory traversal attempt
                string badPath = testFile.Replace(Path.GetFileName(testFile), "..\\..\\malicious.mp4");
                if (!FileSecurityHelper.IsPathSafe(badPath))
                {
                    Console.WriteLine("✅ PASS: Path traversal attempt rejected");
                }
                else
                {
                    Console.WriteLine("❌ FAIL: Should reject path with '..'");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FAIL: Exception during test: {ex.Message}");
            }
            finally
            {
                if (File.Exists(testFile))
                {
                    try { File.Delete(testFile); } catch { }
                }
            }

            Console.WriteLine();
        }
    }
}

