using RE.Application.Models;

namespace RE.Application.UseCases;

public sealed class UsageSummaryService
{
    public IReadOnlyList<UsageSummary> Sort(IEnumerable<UsageSummary> summaries)
    {
        return summaries
            .OrderBy(summary => summary.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.BucketName.Equals("5h", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();
    }
}