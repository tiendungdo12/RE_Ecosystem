using System.Security.Cryptography;
using System.Text;
using RE.Core.Abstractions;

namespace RE.Platform.Windows;

/// <summary>DPAPI CurrentUser ciphertext, kept outside the source/output directories.</summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _directory;

    public DpapiSecretStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(RE.Core.UserDataPaths.SettingsDirectory, "Secrets");
    }

    private string Resolve(string reference)
    {
        if (!Guid.TryParseExact(reference, "N", out var id))
            throw new ArgumentException("Invalid credential reference.", nameof(reference));
        return Path.Combine(_directory, id.ToString("N") + ".bin");
    }

    public async Task SaveAsync(string reference, string value, CancellationToken cancellationToken = default)
    {
        string target = Resolve(reference);
        byte[] plaintext = Encoding.UTF8.GetBytes(value);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Directory.CreateDirectory(_directory);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<string?> ReadAsync(string reference, CancellationToken cancellationToken = default)
    {
        string path = Resolve(reference);
        if (!File.Exists(path)) return null;
        byte[] encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        byte[] plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(Resolve(reference));
        return Task.CompletedTask;
    }
}
