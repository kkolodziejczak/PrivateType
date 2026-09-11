using System.IO;
using System.Security.Cryptography;

namespace PrivateType.App;

internal static class ReadySoundStorage
{
    public static void Validate(string sourcePath) => _ = ReadySoundAudio.ReadCustom(sourcePath);

    public static string Import(string sourcePath, string dataDirectory)
    {
        Validate(sourcePath);
        var directory = Path.Combine(Path.GetFullPath(dataDirectory), "ready-sounds");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath).ToLowerInvariant()}");
        try
        {
            File.Copy(sourcePath, temporaryPath);
            Validate(temporaryPath);
            string hash;
            using (var content = File.OpenRead(temporaryPath))
                hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var soundDirectory = Path.Combine(directory, hash);
            Directory.CreateDirectory(soundDirectory);
            var storedPath = Path.Combine(soundDirectory, Path.GetFileName(sourcePath));
            if (!File.Exists(storedPath))
                File.Move(temporaryPath, storedPath);
            return storedPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
