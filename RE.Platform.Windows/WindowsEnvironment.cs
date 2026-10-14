using System;
using System.IO;
using System.Runtime.InteropServices;
using RE.Core.Abstractions;

namespace RE.Platform.Windows
{
    public class WindowsEnvironment : IPlatformEnvironment
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        public WindowsEnvironment()
        {
            string root = GetEcosystemRoot();
            string libDir = Path.Combine(root, "lib");
            if (Directory.Exists(libDir))
            {
                SetDllDirectory(libDir);
            }
        }

        public string PythonExecutable
        {
            get
            {
                string root = GetEcosystemRoot();
                return Path.Combine(root, @"Python_Runtime\python.exe");
            }
        }

        public string DieDbPath
        {
            get
            {
                string root = GetEcosystemRoot();
                return Path.Combine(root, @"Rules\die_db");
            }
        }

        public void SetLibrarySearchPath(string path)
        {
            SetDllDirectory(path);
        }

        private string GetEcosystemRoot()
        {
            string? currentDir = AppDomain.CurrentDomain.BaseDirectory;
            while (currentDir != null)
            {
                if (Directory.Exists(Path.Combine(currentDir, "Python_Runtime")))
                {
                    return currentDir;
                }

                currentDir = Directory.GetParent(currentDir)?.FullName;
            }

            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\"));
        }
    }
}