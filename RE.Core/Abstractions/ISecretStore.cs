namespace RE.Core.Abstractions;

/// <summary>OS-protected credentials; persisted settings contain references only.</summary>
public interface ISecretStore
{
    Task SaveAsync(string reference, string value, CancellationToken cancellationToken = default);
    Task<string?> ReadAsync(string reference, CancellationToken cancellationToken = default);
    Task DeleteAsync(string reference, CancellationToken cancellationToken = default);
}
