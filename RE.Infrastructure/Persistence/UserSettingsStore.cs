using System.Text.Json;
using System.Text.Json.Nodes;
using RE.Core.Abstractions;

namespace RE.Infrastructure.Persistence;

/// <summary>Serial, atomic JSON preferences with versioned credential references.</summary>
public sealed class UserSettingsStore
{
    private readonly string _path;
    private readonly ISecretStore _secrets;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<string, string, CancellationToken, Task> _write;

    public UserSettingsStore(string path, ISecretStore secrets)
        : this(path, secrets, WriteAtomicAsync) { }

    internal UserSettingsStore(string path, ISecretStore secrets,
        Func<string, string, CancellationToken, Task> write)
    {
        _path = path;
        _secrets = secrets;
        _write = write;
    }

    public async Task<string> LoadAsync(string templatePath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            bool runtimeExists = File.Exists(_path);
            string json = runtimeExists ? await File.ReadAllTextAsync(_path, cancellationToken)
                : File.Exists(templatePath) ? await File.ReadAllTextAsync(templatePath, cancellationToken) : "{}";
            var node = Parse(json); // Corrupt runtime must not fall back to defaults/template.
            if (!runtimeExists || HasPlaintext(node))
                return await SaveCoreAsync(node, null, cancellationToken);
            return node.ToJsonString();
        }
        finally { _gate.Release(); }
    }

    public async Task<string> SaveAsync(string json, IReadOnlyDictionary<string, string>? pendingSecrets = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await SaveCoreAsync(Parse(json), pendingSecrets, cancellationToken); }
        finally { _gate.Release(); }
    }

    private static JsonObject Parse(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Settings must be a JSON object.");
        if (node["api_keys"] != null && node["api_keys"] is not JsonArray)
            throw new JsonException("Invalid credential metadata list.");
        return node;
    }

    private static bool HasPlaintext(JsonObject node) => node["api_keys"] is JsonArray keys
        && keys.OfType<JsonObject>().Any(k => k.ContainsKey("key"));

    private async Task<string> SaveCoreAsync(JsonObject node, IReadOnlyDictionary<string, string>? pending,
        CancellationToken cancellationToken)
    {
        var created = new List<string>();
        try
        {
            if (node["api_keys"] is JsonArray keys)
            {
                foreach (var item in keys)
                {
                    if (item is not JsonObject key) throw new JsonException("Invalid API key metadata.");
                    string id = key["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                    key["id"] = id;
                    string? value = key["key"]?.GetValue<string>();
                    if (pending != null && pending.TryGetValue(id, out var incoming)) value = incoming;
                    key.Remove("key");
                    if (!string.IsNullOrEmpty(value))
                    {
                        string reference = Guid.NewGuid().ToString("N");
                        created.Add(reference);
                        await _secrets.SaveAsync(reference, value, cancellationToken);
                        key["secret_reference"] = reference;
                    }
                }
            }
            string sanitized = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await _write(_path, sanitized, cancellationToken);
            return sanitized;
        }
        catch
        {
            foreach (string reference in created)
            {
                try { await _secrets.DeleteAsync(reference); } catch { /* Ciphertext orphan; never plaintext. */ }
            }
            throw;
        }
    }

    private static async Task WriteAtomicAsync(string path, string json, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
