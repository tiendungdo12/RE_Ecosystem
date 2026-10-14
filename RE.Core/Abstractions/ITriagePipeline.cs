using System.Threading;
using System.Threading.Tasks;
using RE.Domain.Models;

namespace RE.Core.Abstractions
{
    /// <summary>
    /// Canonical contract for Phase 1 Deterministic Triage.
    /// Runs 100% deterministically without calling LLM and without passing through MCP.
    /// Direct subprocess execution is used for non-native tools/libraries (e.g. LIEF, AXML).
    /// </summary>
    public interface ITriagePipeline
    {
        /// <summary>
        /// Executes Phase 1 deterministic static triage on the target binary or package.
        /// </summary>
        /// <param name="filePath">Target binary or package path.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Populated <see cref="TriageResult"/>.</returns>
        Task<TriageResult> RunTriageAsync(string filePath, CancellationToken cancellationToken = default);
    }
}