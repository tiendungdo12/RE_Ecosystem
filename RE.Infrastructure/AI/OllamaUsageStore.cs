using System.Text.Json;
using RE.Core;

namespace RE.Infrastructure.AI;

public sealed record OllamaUsageModel(
    string Name,
    int RequestCount,
    DateTimeOffset LastUsedAt);

public sealed record OllamaUsageSnapshot(
    int FiveHourRequestCount,
    int WeeklyRequestCount,
    DateTimeOffset? FiveHourWindowStart,
    DateTimeOffset? WeeklyWindowStart,
    IReadOnlyList<OllamaUsageModel> Models);

/// <summary>
/// Tracks Ollama activity locally. Ollama's public API currently does not expose
/// account quota/reset percentages, so this store deliberately reports activity
/// counts instead of inventing a quota percentage.
/// </summary>
public sealed class OllamaUsageStore
{
    private sealed record UsageEntry(
        string Model,
        DateTimeOffset UsedAt);

    private static readonly TimeSpan FiveHourWindow = TimeSpan.FromHours(5);
    private static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static string UsageFilePath => Path.Combine(
        AgentConfigLoader.GetAppRootPath(), ".ai_data", "ollama", "usage.json");

    public async Task RecordRequestAsync(string model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadEntriesAsync(cancellationToken);
            entries.Add(new UsageEntry(model.Trim(), DateTimeOffset.UtcNow));

            // Keep the local file bounded while retaining enough history for the
            // seven-day window and a small amount of diagnostic context.
            var cutoff = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(30));
            entries = entries.Where(entry => entry.UsedAt >= cutoff).TakeLast(2000).ToList();
            await WriteEntriesAsync(entries, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OllamaUsageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadEntriesAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var fiveHourEntries = entries.Where(entry => entry.UsedAt >= now.Subtract(FiveHourWindow)).ToList();
            var weeklyEntries = entries.Where(entry => entry.UsedAt >= now.Subtract(WeeklyWindow)).ToList();
            var models = weeklyEntries
                .GroupBy(entry => entry.Model, StringComparer.OrdinalIgnoreCase)
                .Select(group => new OllamaUsageModel(
                    group.Key,
                    group.Count(),
                    group.Max(entry => entry.UsedAt)))
                .OrderByDescending(model => model.LastUsedAt)
                .ToList();

            return new OllamaUsageSnapshot(
                fiveHourEntries.Count,
                weeklyEntries.Count,
                fiveHourEntries.Count == 0 ? null : now.Subtract(FiveHourWindow),
                weeklyEntries.Count == 0 ? null : now.Subtract(WeeklyWindow),
                models);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<List<UsageEntry>> ReadEntriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(UsageFilePath))
                return new List<UsageEntry>();

            await using var stream = File.OpenRead(UsageFilePath);
            return await JsonSerializer.DeserializeAsync<List<UsageEntry>>(stream, cancellationToken: cancellationToken)
                   ?? new List<UsageEntry>();
        }
        catch
        {
            return new List<UsageEntry>();
        }
    }

    private static async Task WriteEntriesAsync(List<UsageEntry> entries, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(UsageFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(UsageFilePath);
        await JsonSerializer.SerializeAsync(stream, entries, cancellationToken: cancellationToken);
    }
}