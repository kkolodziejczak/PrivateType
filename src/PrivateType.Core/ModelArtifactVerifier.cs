using System.Security.Cryptography;

namespace PrivateType.Core;

public static class ModelArtifactVerifier
{
    public static bool IsVerified(string path, long expectedBytes, string expectedSha256)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length != expectedBytes)
                return false;

            var actualSha256 = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static string NormalizeSha256(string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("The model SHA-256 must contain exactly 64 hexadecimal characters.", nameof(sha256));

        return sha256.ToLowerInvariant();
    }
}
