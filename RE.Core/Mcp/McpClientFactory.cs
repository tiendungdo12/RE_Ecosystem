using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RE.Core.Abstractions;

namespace RE.Core.Mcp
{
    internal static class McpClientFactory
    {
        public static async Task<McpClient> CreateAsync(
            McpToolConfig config,
            IPlatformEnvironment? platform = null,
            CancellationToken ct = default)
        {
            string transportType = (config.Transport ?? "stdio").Trim().ToLowerInvariant();

            IClientTransport transport = transportType switch
            {
                "stdio" => CreateStdioTransport(config, platform),
                "http" or "sse" => CreateHttpTransport(config),
                _ => throw new NotSupportedException($"Transport type '{config.Transport}' is not supported.")
            };

            var options = new McpClientOptions
            {
                ClientInfo = new Implementation
                {
                    Name = "RE_Ecosystem_Client",
                    Version = "2.0.0"
                }
            };

            return await McpClient.CreateAsync(transport, options, loggerFactory: null, cancellationToken: ct);
        }

        private static IClientTransport CreateHttpTransport(McpToolConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.Endpoint))
            {
                throw new InvalidOperationException(
                    $"MCP tool '{config.Id}' specifies HTTP transport but has no endpoint configured.");
            }

            var httpOptions = new HttpClientTransportOptions
            {
                Endpoint = new Uri(config.Endpoint)
            };

            string mode = (config.TransportMode ?? "auto").Trim().ToLowerInvariant();
            if (mode == "sse")
            {
                httpOptions.TransportMode = HttpTransportMode.Sse;
            }
            else if (mode == "streamable_http" || mode == "streamablehttp" || mode == "http")
            {
                httpOptions.TransportMode = HttpTransportMode.StreamableHttp;
            }
            else
            {
                httpOptions.TransportMode = HttpTransportMode.AutoDetect;
            }

            return new HttpClientTransport(httpOptions);
        }

        private static IClientTransport CreateStdioTransport(McpToolConfig config, IPlatformEnvironment? platform)
        {
            string ecosystemRoot = GetEcosystemRoot();
            string command = config.Command;

            if (platform != null && !string.IsNullOrWhiteSpace(platform.PythonExecutable)
                && (string.IsNullOrWhiteSpace(command) || command.Contains("python", StringComparison.OrdinalIgnoreCase)))
            {
                command = platform.PythonExecutable;
            }
            else if (string.IsNullOrWhiteSpace(command) ||
                     command.Contains("python", StringComparison.OrdinalIgnoreCase))
            {
                command = Path.Combine(ecosystemRoot, @"Python_Runtime\python.exe");
            }
            else if (!Path.IsPathRooted(command))
            {
                // Bare commands (e.g. uvx) are resolved on PATH; relative file paths are rooted in the ecosystem.
                if (command.Contains('/') || command.Contains('\\')) command = Path.Combine(ecosystemRoot, command);
            }

            if (platform != null)
            {
                platform.SetLibrarySearchPath(Path.Combine(ecosystemRoot, "lib"));
            }

            var args = new string[config.Args.Count];
            for (int i = 0; i < config.Args.Count; i++)
            {
                string arg = config.Args[i];
                if (!Path.IsPathRooted(arg))
                {
                    string cand1 = !string.IsNullOrWhiteSpace(config.WorkingDirectory)
                        ? Path.Combine(ecosystemRoot, config.WorkingDirectory, arg)
                        : "";
                    string cand2 = Path.Combine(ecosystemRoot, "mcp_server", arg);
                    string cand3 = Path.Combine(ecosystemRoot, arg);

                    if (!string.IsNullOrEmpty(cand1) && File.Exists(cand1))
                    {
                        arg = cand1;
                    }
                    else if (File.Exists(cand2))
                    {
                        arg = cand2;
                    }
                    else if (File.Exists(cand3))
                    {
                        arg = cand3;
                    }
                }

                args[i] = arg;
            }

            return new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = command,
                Arguments = args
            });
        }

        private static string GetEcosystemRoot()
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
