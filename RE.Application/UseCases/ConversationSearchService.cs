using RE.Domain.Models;

namespace RE.Application.UseCases;

/// <summary>
/// Searches conversation titles independently from any UI framework.
/// </summary>
public sealed class ConversationSearchService
{
    public IReadOnlyList<ChatSessionInfo> Search(IEnumerable<ChatSessionInfo> sessions, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return sessions.ToList();

        return sessions
            .Where(session => session.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}