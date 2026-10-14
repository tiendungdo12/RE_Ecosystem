using System;
using RE.Core;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RE.Infrastructure.AI
{
    public class AntigravityServerManager : IDisposable
    {
        private Process? _serverProcess;
        private string _csrfToken = string.Empty;
        private int _port = -1;
        private int _httpPort = -1;
        private string? _authenticationUrl;

        private TaskCompletionSource<string?> _authenticationUrlTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<bool> _startupTcs = new TaskCompletionSource<bool>();

        public static AntigravityServerManager Instance { get; } = new AntigravityServerManager();

        public int Port => _port;
        public int HttpPort => _httpPort;
        public string CsrfToken => _csrfToken;
        public string LanguageServerPath => GetLanguageServerPath();
        public string? AuthenticationUrl => _authenticationUrl;
        public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited && (_httpPort > 0 || _port > 0);

        private AntigravityServerManager()
        {
        }

        public async Task<bool> StartAsync()
        {
            if (IsRunning) return true;

            string exePath = LanguageServerPath;

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("Antigravity language_server.exe not found at " + exePath);
            }

            // Keep the test profile in the workspace during development. The
            // path helper falls back to LocalAppData for packaged builds.
            string appDataDir = RuntimeDataPaths.AntigravityProfilePath;
            StopPreviousProjectServer(appDataDir);
            string ideVersion = GetAntigravityIdeVersion(exePath);
            string isolatedHome = Path.Combine(appDataDir, "home");
            Directory.CreateDirectory(isolatedHome);

            _csrfToken = Guid.NewGuid().ToString("N");
            _authenticationUrl = null;
            _authenticationUrlTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _startupTcs = new TaskCompletionSource<bool>();

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                // The current language_server expects app_data_dir and
                // config_dir relative to gemini_dir. Passing an absolute path
                // only as app_data_dir makes it resolve the profile in the
                // wrong place on recent Antigravity builds.
                // Keep the browser session isolated from the official
                // Antigravity app. Headless is explicitly disabled so the
                // user can see and complete the Google sign-in window.
                // Use a distinct IDE name so the language server has a
                // separate credential namespace from the official app. OAuth
                // itself is opened by SettingsPage in the user's default
                // browser; do not ask the server to launch its own Chrome
                // profile.
                // Keep the user-agent expected by the backend. Testing showed
                // that neither this value nor override_ide_name changes the
                // hard-coded Windows credential target (gemini:antigravity).
                Arguments =
                    $"--standalone --override_ide_name antigravity --subclient_type hub --override_ide_version {ideVersion} --override_user_agent_name antigravity --gemini_dir \"{appDataDir}\" --app_data_dir \"app\" --config_dir \"config\" --api_server_url https://generativelanguage.googleapis.com --cloud_code_endpoint https://daily-cloudcode-pa.googleapis.com --enable_sidecars --https_server_port 0 --csrf_token {_csrfToken}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            // The standalone OAuth implementation stores its credential cache
            // under the OS home directory, independently of --gemini_dir.
            // Override the child process home so this cache cannot be shared
            // with the official Antigravity installation.
            startInfo.Environment["USERPROFILE"] = isolatedHome;
            startInfo.Environment["HOME"] = isolatedHome;
            startInfo.Environment["GEMINI_HOME"] = Path.Combine(appDataDir, "gemini");
            startInfo.Environment["APPDATA"] = Path.Combine(isolatedHome, "AppData", "Roaming");
            startInfo.Environment["LOCALAPPDATA"] = Path.Combine(isolatedHome, "AppData", "Local");

            _serverProcess = new Process { StartInfo = startInfo };

            _serverProcess.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    Debug.WriteLine($"[AGY-SERVER-STDOUT] {args.Data}");
                    ProcessServerLogLine(args.Data);
                }
            };

            _serverProcess.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    Debug.WriteLine($"[AGY-SERVER-STDERR] {args.Data}");
                    ProcessServerLogLine(args.Data);
                }
            };

            _serverProcess.Start();
            SaveProjectServerPid(appDataDir, _serverProcess.Id);
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            // Wait up to 10 seconds for the HTTP port to be discovered. The
            // current language server exposes JSON/Connect over HTTP while
            // HTTPS is its gRPC endpoint.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cts.Token.Register(() => _startupTcs.TrySetResult(false));

            bool success = await _startupTcs.Task;
            if (!success)
            {
                Stop();
                return false;
            }

            return true;
        }

        public string ProfilePath => RuntimeDataPaths.AntigravityProfilePath;

        public string HttpBaseAddress => $"http://localhost:{_httpPort}";

        public string HttpsBaseAddress => $"https://localhost:{_port}";

        public async Task<string?> WaitForAuthenticationUrlAsync(TimeSpan timeout)
        {
            if (!string.IsNullOrWhiteSpace(_authenticationUrl))
                return _authenticationUrl;

            try
            {
                using var cts = new CancellationTokenSource(timeout);
                return await _authenticationUrlTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return _authenticationUrl;
            }
        }

        private static string GetLanguageServerPath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Programs", "antigravity", "resources", "bin", "language_server.exe");
        }

        private static string GetAntigravityIdeVersion(string languageServerPath)
        {
            try
            {
                string? binDirectory = Path.GetDirectoryName(languageServerPath);
                if (!string.IsNullOrWhiteSpace(binDirectory))
                {
                    string idePath = Path.GetFullPath(Path.Combine(binDirectory, "..", "..", "Antigravity.exe"));
                    string? productVersion = FileVersionInfo.GetVersionInfo(idePath).ProductVersion;
                    var match = Regex.Match(productVersion ?? "", @"\d+\.\d+\.\d+");
                    if (match.Success)
                        return match.Value;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to read Antigravity IDE version: {ex.Message}");
            }

            return "2.6.0";
        }

        private void ProcessServerLogLine(string line)
        {
            // language_server.exe writes colored log lines even when its
            // output is redirected. If the reset sequence is captured as
            // part of the OAuth URL, it becomes %1B%5B0m in the callback's
            // state parameter and the server rejects it as invalid. Strip
            // ANSI CSI sequences before parsing ports or authentication URLs.
            line = Regex.Replace(line, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);

            var httpsMatch = Regex.Match(line, @"listening on random port at (\d+) for HTTPS\b");
            if (httpsMatch.Success && int.TryParse(httpsMatch.Groups[1].Value, out int httpsPort))
                _port = httpsPort;

            var httpMatch = Regex.Match(line, @"listening on random port at (\d+) for HTTP\b");
            if (httpMatch.Success && int.TryParse(httpMatch.Groups[1].Value, out int httpPort))
            {
                _httpPort = httpPort;
                _startupTcs.TrySetResult(true);
            }

            var authMatch = Regex.Match(line, @"https://accounts\.google\.com/o/oauth2/[^\s]+",
                RegexOptions.IgnoreCase);
            if (authMatch.Success)
            {
                _authenticationUrl = authMatch.Value;
                _authenticationUrlTcs.TrySetResult(_authenticationUrl);
            }
        }

        public void Stop()
        {
            int processId = _serverProcess?.Id ?? -1;
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try
                {
                    _serverProcess.Kill(true);
                }
                catch
                {
                }
            }

            _serverProcess?.Dispose();
            _serverProcess = null;
            _port = -1;
            _httpPort = -1;
            _authenticationUrl = null;
            RemoveProjectServerPid(ProfilePath, processId);
        }

        private static string GetProjectServerPidPath(string profilePath) =>
            Path.Combine(profilePath, ".re-language-server.pid");

        // A debug/deployed WinUI instance can be terminated by the debugger
        // before Window.Closed runs. Persisting only our own PID lets the next
        // launch clear that orphan without ever touching the official
        // Antigravity installation or its profile.
        private static void StopPreviousProjectServer(string profilePath)
        {
            try
            {
                string pidPath = GetProjectServerPidPath(profilePath);
                if (!File.Exists(pidPath) || !int.TryParse(File.ReadAllText(pidPath), out int pid))
                    return;

                using Process process = Process.GetProcessById(pid);
                if (string.Equals(process.ProcessName, "language_server", StringComparison.OrdinalIgnoreCase) &&
                    !process.HasExited)
                {
                    process.Kill(true);
                    process.WaitForExit(3000);
                }
            }
            catch (ArgumentException)
            {
                // The recorded server already exited.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to stop prior project language server: {ex.Message}");
            }
            finally
            {
                try
                {
                    File.Delete(GetProjectServerPidPath(profilePath));
                }
                catch
                {
                }
            }
        }

        private static void SaveProjectServerPid(string profilePath, int pid)
        {
            try
            {
                Directory.CreateDirectory(profilePath);
                File.WriteAllText(GetProjectServerPidPath(profilePath), pid.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to save project language-server PID: {ex.Message}");
            }
        }

        private static void RemoveProjectServerPid(string profilePath, int expectedPid)
        {
            try
            {
                string pidPath = GetProjectServerPidPath(profilePath);
                if (File.Exists(pidPath) &&
                    int.TryParse(File.ReadAllText(pidPath), out int recordedPid) &&
                    recordedPid == expectedPid)
                {
                    File.Delete(pidPath);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}