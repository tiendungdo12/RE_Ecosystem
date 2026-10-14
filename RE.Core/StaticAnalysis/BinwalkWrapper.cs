using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RE.Core.StaticAnalysis
{
    public static class BinwalkWrapper
    {
        // Import C ABI function to scan a file
        [DllImport("binwalk_core.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr binwalk_scan(string path);

        // Import C ABI function to free the returned string
        [DllImport("binwalk_core.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void binwalk_free(IntPtr ptr);

        private static readonly object _scanLock = new object();

        /// <summary>
        /// Analyzes a file using the Rust Binwalk v3 core.
        /// Serializes calls to prevent overlapping native executions in binwalk_core.dll.
        /// </summary>
        /// <param name="filePath">Absolute path to the target file.</param>
        /// <returns>JSON string containing the analysis results.</returns>
        public static string? Scan(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }

            bool acquired = false;
            try
            {
                Monitor.TryEnter(_scanLock, TimeSpan.Zero, ref acquired);
                if (!acquired)
                {
                    throw new InvalidOperationException("Binwalk scanner is busy executing a prior operation.");
                }

                IntPtr resultPtr = IntPtr.Zero;
                try
                {
                    // Call the Rust FFI function
                    resultPtr = binwalk_scan(filePath);

                    if (resultPtr == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Binwalk native scan returned null pointer for '{filePath}'.");
                    }

                    // Convert unmanaged pointer to C# string
                    string? jsonResult = Marshal.PtrToStringAnsi(resultPtr);
                    return jsonResult;
                }
                finally
                {
                    // ALWAYS free the pointer to prevent memory leaks
                    if (resultPtr != IntPtr.Zero)
                    {
                        binwalk_free(resultPtr);
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