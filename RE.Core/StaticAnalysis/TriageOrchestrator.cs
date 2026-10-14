using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RE.Core.Abstractions;
using RE.Domain.Models;

namespace RE.Core.StaticAnalysis
{
    /// <summary>
    /// Legacy static orchestrator wrapper.
    /// Deprecated in favor of the canonical ITriagePipeline / TriageService.
    /// Maintained for backwards compatibility by delegating directly to the canonical pipeline configured from the composition root.
    /// </summary>
    [Obsolete("TriageOrchestrator is deprecated. Use ITriagePipeline / TriageService instead.")]
    public static class TriageOrchestrator
    {
        private static ITriagePipeline? _pipeline;

        /// <summary>
        /// Explicitly configures the static orchestrator with an ITriagePipeline instance.
        /// </summary>
        public static void Configure(ITriagePipeline pipeline)
        {
            _pipeline = pipeline;
        }

        /// <summary>
        /// Explicitly configures the static orchestrator with an ITriagePipeline resolver function.
        /// </summary>
        public static void Configure(Func<ITriagePipeline> pipelineResolver)
        {
            PipelineResolver = pipelineResolver;
        }

        /// <summary>
        /// Pipeline resolver delegate configured from DI / Composition Root.
        /// </summary>
        public static Func<ITriagePipeline>? PipelineResolver { get; set; }

        public static async Task<string> RunTriageAsync(string targetFile, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(targetFile))
            {
                return JsonSerializer.Serialize(new { error = "File not found." });
            }

            ITriagePipeline pipeline = _pipeline ?? PipelineResolver?.Invoke()
                ?? throw new InvalidOperationException("TriageOrchestrator must be configured with an ITriagePipeline instance at application startup.");

            try
            {
                var result = await pipeline.RunTriageAsync(targetFile, cancellationToken);
                return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Triage failed: {ex.Message}" });
            }
        }

        public static string GetAIFriendlyTriageContext(string rawJson)
        {
            try
            {
                var result = JsonSerializer.Deserialize<TriageResult>(rawJson);
                if (result != null)
                {
                    return JsonSerializer.Serialize(result.ToAIFriendlyContext(), new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch
            {
            }

            return "{\"error\": \"Could not parse triage data for AI\"}";
        }
    }
}