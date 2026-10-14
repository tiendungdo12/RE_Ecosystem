using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RE.Core.Abstractions;
using RE.Core.Mcp;
using RE.Core.Services;
using RE.Infrastructure.Persistence;
using RE.Platform.Windows;
using Xunit;

namespace App_Test;

public sealed class PublicationSafetyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "re-publication-tests", Guid.NewGuid().ToString("N"));
    private const string Value = "synthetic-credential-for-local-tests";
    public PublicationSafetyTests() => Directory.CreateDirectory(_directory);
    public void Dispose()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "re-publication-tests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_directory).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing cleanup outside the test directory.");
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Dpapi_RoundTrip_Delete_AndNoPlaintextOnDisk()
    {
        var secrets = new DpapiSecretStore(Path.Combine(_directory, "secrets"));
        string reference = Guid.NewGuid().ToString("N");
        await secrets.SaveAsync(reference, Value);
        Assert.Equal(Value, await secrets.ReadAsync(reference));
        Assert.DoesNotContain(Value, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(_directory, "secrets", reference + ".bin"))));
        await secrets.DeleteAsync(reference);
        Assert.Null(await secrets.ReadAsync(reference));
    }

    [Fact]
    public async Task Dpapi_CorruptCiphertext_ThrowsWithoutPlaintextFallback()
    {
        var secrets = new DpapiSecretStore(_directory);
        string reference = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(_directory, reference + ".bin"), Value);
        await Assert.ThrowsAsync<CryptographicException>(() => secrets.ReadAsync(reference));
        await Assert.ThrowsAsync<ArgumentException>(() => secrets.SaveAsync("../escape", Value));
    }

    [Fact]
    public async Task Settings_MigrateLegacy_PreservesPreferences_AndRemovesPlaintext()
    {
        var secrets = new DpapiSecretStore(Path.Combine(_directory, "secrets"));
        string template = Path.Combine(_directory, "legacy.json");
        string target = Path.Combine(_directory, "runtime", "settings.json");
        var legacy = new JsonObject
        {
            ["app_language"] = "vi-VN",
            ["api_keys"] = new JsonArray(new JsonObject { ["id"] = "record-1", ["provider"] = "Synthetic", ["key"] = Value })
        };
        await File.WriteAllTextAsync(template, legacy.ToJsonString());
        var store = new UserSettingsStore(target, secrets);
        string imported = await store.LoadAsync(template);
        var node = JsonNode.Parse(imported)!;
        Assert.Equal("vi-VN", node["app_language"]!.GetValue<string>());
        Assert.False(node["api_keys"]![0]!.AsObject().ContainsKey("key"));
        Assert.DoesNotContain(Value, await File.ReadAllTextAsync(target));
        Assert.Equal(Value, await secrets.ReadAsync(node["api_keys"]![0]!["secret_reference"]!.GetValue<string>()));
        await File.WriteAllTextAsync(template, "{}");
        Assert.Equal(node.ToJsonString(), JsonNode.Parse(await store.LoadAsync(template))!.ToJsonString());
    }

    [Fact]
    public async Task Settings_FailedWrite_PreservesPreviousFile_AndCredential()
    {
        var secrets = new DpapiSecretStore(Path.Combine(_directory, "secrets"));
        string target = Path.Combine(_directory, "settings.json");
        var store = new UserSettingsStore(target, secrets);
        string json = await store.SaveAsync("{\"app_language\":\"vi-VN\",\"api_keys\":[{\"id\":\"record-1\"}]}",
            new Dictionary<string, string> { ["record-1"] = Value });
        string previousReference = JsonNode.Parse(json)!["api_keys"]![0]!["secret_reference"]!.GetValue<string>();
        var failing = new UserSettingsStore(target, secrets, (_, _, _) => throw new IOException("Synthetic write failure"));
        await Assert.ThrowsAsync<IOException>(() => failing.SaveAsync(json,
            new Dictionary<string, string> { ["record-1"] = "replacement-synthetic-value" }));
        Assert.Equal(json, await File.ReadAllTextAsync(target));
        Assert.Equal(Value, await secrets.ReadAsync(previousReference));
        Assert.Single(Directory.GetFiles(Path.Combine(_directory, "secrets"), "*.bin"));
    }

    [Fact]
    public async Task Settings_CorruptRuntime_DoesNotFallbackOrOverwrite()
    {
        string target = Path.Combine(_directory, "settings.json");
        string template = Path.Combine(_directory, "template.json");
        await File.WriteAllTextAsync(target, "not-json");
        await File.WriteAllTextAsync(template, "{}");
        var store = new UserSettingsStore(target, new DpapiSecretStore(_directory));
        await Assert.ThrowsAnyAsync<JsonException>(() => store.LoadAsync(template));
        Assert.Equal("not-json", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Settings_ParallelSaves_AreSerializedAndLeaveValidJson()
    {
        string target = Path.Combine(_directory, "settings.json");
        int active = 0, maximum = 0;
        var store = new UserSettingsStore(target, new DpapiSecretStore(_directory), async (p, json, ct) =>
        {
            int count = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, count);
            await Task.Delay(5, ct);
            await File.WriteAllTextAsync(p, json, ct);
            Interlocked.Decrement(ref active);
        });
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => store.SaveAsync($"{{\"sequence\":{i}}}")));
        Assert.Equal(1, maximum);
        Assert.NotNull(JsonNode.Parse(await File.ReadAllTextAsync(target)));
    }

    [Theory]
    [InlineData("interactive.ida", "patch_bytes")]
    [InlineData("interactive.ida", "rename_function")]
    [InlineData("network.burp", "send_http1_request")]
    [InlineData("network.burp", "send_http2_request")]
    [InlineData("network.burp", "send_to_intruder")]
    [InlineData("network.burp", "generate_collaborator_payload")]
    [InlineData("network.burp", "set_task_execution_engine_state")]
    [InlineData("static.md5", "unknown_tool")]
    [InlineData("unregistered", "md5_file")]
    public async Task Policy_DeniesBeforeTransport(string toolId, string toolName)
    {
        int calls = 0;
        var manager = new McpClientManager(new WindowsEnvironment(), (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(JsonSerializer.SerializeToElement(new { }));
        });
        await Assert.ThrowsAsync<ToolPolicyDeniedException>(() => manager.CallToolAsync(toolId, toolName, new { }));
        Assert.Equal(0, calls);
        await Assert.ThrowsAsync<ToolPolicyDeniedException>(() => new ToolExecutionService().ExecuteToolAsync(toolId, toolName, "unused"));
#pragma warning disable CS0618
        await using var legacy = new McpProcessClient(new McpToolConfig { Id = toolId, Command = "must-not-start" }, "test");
        await Assert.ThrowsAsync<ToolPolicyDeniedException>(() => legacy.CallToolAsync(toolName, new { }));
#pragma warning restore CS0618
    }

    [Theory]
    [InlineData("static.md5", "md5_file")]
    [InlineData("interactive.ida", "decompile")]
    [InlineData("network.burp", "get_proxy_http_history")]
    public async Task Policy_ReadOnlyPairs_ReachFakeTransport(string toolId, string toolName)
    {
        int calls = 0;
        var manager = new McpClientManager(new WindowsEnvironment(), (id, name, _, _) =>
        {
            Assert.Equal(toolId, id); Assert.Equal(toolName, name); calls++;
            return Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true }));
        });
        var result = await manager.CallToolAsync(toolId, toolName, new { });
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(1, calls);
    }
}
