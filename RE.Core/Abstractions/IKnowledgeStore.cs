using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RE.Domain.Models;

namespace RE.Core.Abstractions;

/// <summary>
/// Application port abstraction for persisting and retrieving triage analysis history and knowledge.
/// Allows Presentation / UI layer to remain decoupled from SQLite and Infrastructure persistence details.
/// </summary>
public interface IKnowledgeStore
{
    string DatabasePath { get; }
    Task SaveTriageResultAsync(TriageResult result);
    Task<List<TriageResult>> GetRecentTriageResultsAsync(int limit = 20);
    Task<List<TriageResult>> SearchTriageResultsAsync(string query, int limit = 50);
    Task DeleteTriageResultAsync(string filePath);
}
