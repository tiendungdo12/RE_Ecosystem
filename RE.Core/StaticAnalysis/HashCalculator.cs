using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace RE.Core.StaticAnalysis
{
    public static class HashCalculator
    {
        public static JsonObject CalculateHashes(string filePath)
        {
            var result = new JsonObject();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return result;
            }

            try
            {
                using var md5 = MD5.Create();
                using var sha1 = SHA1.Create();
                using var sha256 = SHA256.Create();

                using var stream = File.OpenRead(filePath);

                // We use a buffer to calculate multiple hashes in a single pass over the file stream
                byte[] buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    md5.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                    sha1.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                    sha256.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                }

                // Finalize hashes
                md5.TransformFinalBlock(buffer, 0, 0);
                sha1.TransformFinalBlock(buffer, 0, 0);
                sha256.TransformFinalBlock(buffer, 0, 0);

                result["md5"] = BitConverter.ToString(md5.Hash ?? Array.Empty<byte>()).Replace("-", "")
                    .ToLowerInvariant();
                result["sha1"] = BitConverter.ToString(sha1.Hash ?? Array.Empty<byte>()).Replace("-", "")
                    .ToLowerInvariant();
                result["sha256"] = BitConverter.ToString(sha256.Hash ?? Array.Empty<byte>()).Replace("-", "")
                    .ToLowerInvariant();
            }
            catch (Exception ex)
            {
                result["error"] = $"Hashing failed: {ex.Message}";
            }

            return result;
        }
        public static async System.Threading.Tasks.Task<JsonObject> CalculateHashesAsync(string filePath, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = new JsonObject();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var md5 = MD5.Create();
                using var sha1 = SHA1.Create();
                using var sha256 = SHA256.Create();

                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);

                byte[] buffer = new byte[64 * 1024];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    md5.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                    sha1.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                    sha256.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                }

                md5.TransformFinalBlock(buffer, 0, 0);
                sha1.TransformFinalBlock(buffer, 0, 0);
                sha256.TransformFinalBlock(buffer, 0, 0);

                result["md5"] = BitConverter.ToString(md5.Hash ?? Array.Empty<byte>()).Replace("-", "")
                    .ToLowerInvariant();
                result["sha1"] = BitConverter.ToString(sha1.Hash ?? Array.Empty<byte>()).Replace("-", "")
                    .ToLowerInvariant();
                result["sha256"] = BitConverter.ToString(sha256.Hash ?? Array.Empty<byte>()).Replace("-", "")
                    .ToLowerInvariant();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result["error"] = $"Hashing failed: {ex.Message}";
            }

            return result;
        }
    }
}