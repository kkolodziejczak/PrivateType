using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PrivateType.Core;

// Messages describe the problem without quoting file content.
public sealed class VocabularyPackFormatException(string message) : Exception(message);

// A share file is a UTF-8 JSON array of phrase strings and nothing else: no scope, strength,
// name, ID, version, or provenance. Reading is bounded and never follows content-provided paths.
public static class VocabularyPackCodec
{
    public const string Extension = ".privatetype-vocabulary.json";
    public const int MaximumFileBytes = 64 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static IReadOnlyList<string> Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new VocabularyPackFormatException("Choose a regular vocabulary pack file.");
        if (info.Length > MaximumFileBytes)
            throw new VocabularyPackFormatException($"Vocabulary pack files can be at most {MaximumFileBytes / 1024} KB.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[MaximumFileBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0)
            length += read;
        return Parse(buffer.AsSpan(0, length));
    }

    public static IReadOnlyList<string> Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumFileBytes)
            throw new VocabularyPackFormatException($"Vocabulary pack files can be at most {MaximumFileBytes / 1024} KB.");
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            bytes = bytes[3..];

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new VocabularyPackFormatException("The file is not UTF-8 text.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 2 });
        }
        catch (JsonException)
        {
            throw new VocabularyPackFormatException("The file is not valid JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new VocabularyPackFormatException("A vocabulary pack must be a list of phrases in square brackets.");
            if (document.RootElement.GetArrayLength() > VocabularyRules.MaximumEntries)
                throw new VocabularyPackFormatException($"A vocabulary pack can hold at most {VocabularyRules.MaximumEntries} phrases.");

            var phrases = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var position = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                position++;
                if (item.ValueKind != JsonValueKind.String)
                    throw new VocabularyPackFormatException($"Item {position} is not a phrase in quotes. Packs contain phrases only.");

                var phrase = VocabularyRules.Normalize(item.GetString()!);
                if (VocabularyRules.ValidatePhrase(phrase) is { } error)
                    throw new VocabularyPackFormatException($"Item {position}: {error}");
                if (!seen.Add(phrase))
                    throw new VocabularyPackFormatException($"Item {position} repeats an earlier phrase.");
                phrases.Add(phrase);
            }

            return phrases;
        }
    }

    // Canonical form: normalized, ordinal-sorted, two-space indented, LF line endings.
    public static string Serialize(IEnumerable<string> phrases)
    {
        var normalized = phrases.Select(VocabularyRules.Normalize).ToArray();
        if (normalized.Select(VocabularyRules.ValidatePhrase).FirstOrDefault(error => error is not null) is { } error)
            throw new ArgumentException(error, nameof(phrases));
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("A vocabulary pack cannot contain the same phrase twice.", nameof(phrases));

        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            writer.WriteStartArray();
            foreach (var phrase in normalized.Order(StringComparer.Ordinal))
                writer.WriteStringValue(phrase);
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(output.ToArray()) + "\n";
    }

    // Writes through a temporary sibling and an atomic replace, so a failure leaves any existing file intact.
    public static void WriteAtomic(string path, IEnumerable<string> phrases)
    {
        var content = Serialize(phrases);
        var fullPath = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, content, StrictUtf8);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
