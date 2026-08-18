using System.Security.Cryptography;

namespace PrivateType.Core;

public static class ModelArtifactVerifier
{
    public static bool IsVerified(string path, long expectedBytes, string expectedSha256)
    {
        if (!File.Exists(path))
            return false;

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length != expectedBytes)
            return false;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actualSha256 = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static string NormalizeSha256(string sha256)
    {
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("The model SHA-256 must contain exactly 64 hexadecimal characters.", nameof(sha256));

        return sha256.ToLowerInvariant();
    }
}
