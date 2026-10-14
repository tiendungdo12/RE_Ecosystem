namespace RE.Core.Abstractions
{
    public interface IPlatformEnvironment
    {
        string PythonExecutable { get; }
        string DieDbPath { get; }
        void SetLibrarySearchPath(string path);
    }
}