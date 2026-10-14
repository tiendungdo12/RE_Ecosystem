using RE.Domain.Models;

namespace RE.Core.Abstractions
{
    public interface ISignatureVerifier
    {
        SignatureResult Verify(string filePath);
    }
}