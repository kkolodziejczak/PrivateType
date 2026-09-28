using System.Runtime.CompilerServices;
using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

// Validates the repository's curated packs with the production codec and canonical form.
public sealed class CuratedVocabularyPackTests
{
    [Fact]
    public void Every_curated_pack_is_valid_and_canonical()
    {
        var directory = Path.Combine(RepositoryRoot(), "vocabulary-packs");
        Assert.True(Directory.Exists(directory), "The vocabulary-packs directory is missing.");

        foreach (var path in Directory.EnumerateFiles(directory).Where(file => !file.EndsWith("README.md", StringComparison.Ordinal)))
        {
            var name = Path.GetFileName(path);
            Assert.True(name.EndsWith(VocabularyPackCodec.Extension, StringComparison.Ordinal), $"{name} must use the {VocabularyPackCodec.Extension} extension.");

            var phrases = VocabularyPackCodec.Read(path);
            Assert.True(phrases.Count > 0, $"{name} must contain at least one phrase.");
            Assert.True(VocabularyPackCodec.Serialize(phrases) == File.ReadAllText(path), $"{name} is not in canonical form; re-export it from PrivateType.");
        }
    }

    // Located from this source file, so it works wherever the test binaries are built.
    private static string RepositoryRoot([CallerFilePath] string sourcePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VOCABULARY_AND_LANGUAGE_IMPLEMENTATION_PLAN.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
