using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace RE.Core.StaticAnalysis
{
    public static class DieWrapper
    {
        // Import C ABI function to load the signature database
        [DllImport("die_core.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int DIE_LoadDatabaseA(string databasePath);

        // Import C ABI function to scan a file
        // nFlags is typically 0 for default.
        [DllImport("die_core.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr DIE_ScanFileA(string fileName, uint flags, string databasePath);

        // Import C ABI function to free the returned string
        [DllImport("die_core.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DIE_FreeMemoryA(IntPtr ptr);

        private static bool _databaseLoaded = false;
        private static readonly object _dbInitLock = new object();
        private static readonly object _scanLock = new object();

        // Testing hooks for deterministic initialization & concurrency tests
        internal static Func<string, int>? LoadDatabaseOverride { get; set; }
        internal static Func<string, uint, string, IntPtr>? ScanFileOverride { get; set; }

        internal static void ResetForTesting()
        {
            lock (_dbInitLock)
            {
                _databaseLoaded = false;
                LoadDatabaseOverride = null;
                ScanFileOverride = null;
            }
        }

        /// <summary>
        /// Analyzes a file using Detect-It-Easy (DIE) core.
        /// Serializes calls to prevent overlapping native executions in die_core.dll.
        /// </summary>
        /// <param name="filePath">Absolute path to the target file.</param>
        /// <param name="dbPath">Absolute path to the Rules/die_db directory.</param>
        /// <returns>String containing the analysis results (usually plain text format).</returns>
        public static string? Scan(string filePath, string dbPath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            if (!Directory.Exists(dbPath))
            {
                throw new DirectoryNotFoundException($"DIE database directory not found: {dbPath}");
            }

            bool acquired = false;
            try
            {
                // Level 1 Gate: Fail-fast scan lock serialization covering both initialization and scan.
                Monitor.TryEnter(_scanLock, TimeSpan.Zero, ref acquired);
                if (!acquired)
                {
                    throw new InvalidOperationException("DIE scanner is busy executing a prior operation.");
                }

                // Level 2: Database initialization under _scanLock.
                // Strict hierarchy: Level 1 (_scanLock) -> Level 2 (_dbInitLock). Zero deadlock possibility.
                if (!_databaseLoaded)
                {
                    lock (_dbInitLock)
                    {
                        if (!_databaseLoaded)
                        {
                            int status = LoadDatabaseOverride != null
                                ? LoadDatabaseOverride(dbPath)
                                : DIE_LoadDatabaseA(dbPath);

                            if (status <= 0)
                            {
                                throw new InvalidOperationException($"DIE database failed to load from '{dbPath}' (status: {status}).");
                            }
                            _databaseLoaded = true;
                        }
                    }
                }

                IntPtr resultPtr = IntPtr.Zero;
                try
                {
                    // Call the C++ function (or test override if configured)
                    resultPtr = ScanFileOverride != null
                        ? ScanFileOverride(filePath, 0, dbPath)
                        : DIE_ScanFileA(filePath, 0, dbPath);

                    if (resultPtr == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"DIE native scan returned null pointer for '{filePath}'.");
                    }

                    // Convert unmanaged pointer to C# string
                    string? result = Marshal.PtrToStringAnsi(resultPtr);
                    return result;
                }
                finally
                {
                    // ALWAYS free the pointer to prevent memory leaks
                    if (resultPtr != IntPtr.Zero)
                    {
                        if (ScanFileOverride != null)
                        {
                            Marshal.FreeHGlobal(resultPtr);
                        }
                        else
                        {
                            DIE_FreeMemoryA(resultPtr);
                        }
                    }
                }
            }
            finally
            {
                if (acquired)
                {
                    Monitor.Exit(_scanLock);
                }
            }
        }
    }
}