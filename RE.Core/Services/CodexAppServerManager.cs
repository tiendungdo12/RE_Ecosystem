using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace RE.Core.Services;

/// <summary>
/// Starts the official Codex app-server with an isolated CODEX_HOME.
/// The JSON-RPC transport/client is intentionally kept separate from process
/// lifetime management so it can be added without sharing the default Codex
/// profile.
/// </summary>
public sealed class CodexAppServerManager : IDisposable
{
    private Process? _process;

    public static CodexAppServerManager Instance { get; } = new();

    public bool IsRunning => _process is { HasExited: false };

    public string ProfilePath => RuntimeDataPaths.CodexHomePath;

    public Process? Process => _process;

    public Task<Process> StartAsync()
    {
        if (IsRunning)
            return Task.FromResult(_process!);

        Directory.CreateDirectory(ProfilePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "codex",
            Arguments = "app-server --stdio",
            WorkingDirectory = RuntimeDataPaths.WorkspacePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // CODEX_HOME is the supported isolation boundary for Codex settings,
        // authentication and session data.
        startInfo.Environment["CODEX_HOME"] = ProfilePath;

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Exited += (_, _) =>
        {
            _process?.Dispose();
            _process = null;
        };

        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("Codex app-server could not be started.");
        }
        catch
        {
            _process.Dispose();
            _process = null;
            throw;
        }

        return Task.FromResult(_process);
    }

    public async Task StopAsync()
    {
        Process? process = _process;
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch
            {
            }
        }

        _process = null;
        process.Dispose();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}