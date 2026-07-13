using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RE_CommandCenter.Core.StaticAnalysis
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
        private static readonly object _lock = new object();

        /// <summary>
        /// Analyzes a file using Detect-It-Easy (DIE) core.
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

            // Ensure database is loaded into memory only once
            lock (_lock)
            {
                if (!_databaseLoaded)
                {
                    int status = DIE_LoadDatabaseA(dbPath);
                    // Status == 1 usually means success
                    _databaseLoaded = true; 
                }
            }

            IntPtr resultPtr = IntPtr.Zero;
            try
            {
                // Call the C++ function
                resultPtr = DIE_ScanFileA(filePath, 0, dbPath);

                if (resultPtr == IntPtr.Zero)
                {
                    return null;
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
                    DIE_FreeMemoryA(resultPtr);
                }
            }
        }
    }
}
