namespace RE.Domain.Models;

/// <summary>
/// Status of the deterministic triage pipeline execution.
/// Allows callers to reliably distinguish completion, timeout, cancellation, and partial failures
/// without parsing error strings.
/// </summary>
public enum TriageStatus
{
    /// <summary>Analysis completed successfully across all analyzers.</summary>
    Completed,

    /// <summary>Cancelled by caller via CancellationToken.</summary>
    Cancelled,

    /// <summary>An analyzer timed out (e.g. 15s subprocess limit).</summary>
    TimedOut,

    /// <summary>One or more analyzers encountered an error, but available partial results were preserved.</summary>
    PartialFailure
}
