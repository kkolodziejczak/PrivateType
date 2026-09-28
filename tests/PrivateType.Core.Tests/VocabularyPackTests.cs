using System.Text;
using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class VocabularyPackTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"vocabulary-pack-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Parses_a_string_array_with_or_without_a_byte_order_mark()
    {
        var json = """["MVVM", " dependency injection ", "Zólw"]""";

        var plain = VocabularyPackCodec.Parse(Encoding.UTF8.GetBytes(json));
        var withBom = VocabularyPackCodec.Parse([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)]);

        Assert.Equal(["MVVM", "dependency injection", "Zólw"], plain);
        Assert.Equal(plain, withBom);
    }

    [Theory]
    [InlineData("""{"phrases": ["a"]}""")]
    [InlineData("""[{"phrase": "MVVM", "weight": "strong"}]""")]
    [InlineData("""["ok", 5]""")]
    [InlineData("""["ok", null]""")]
    [InlineData("""[["nested"]]""")]
    [InlineData("""["unterminated""")]
    [InlineData("""["a", ]""")]
    [InlineData("""["a"] // comment""")]
    [InlineData("""["", "b"]""")]
    [InlineData("""["two\nlines"]""")]
    [InlineData("""["Same", " Same "]""")]
    public void Rejects_anything_but_valid_unique_phrase_strings(string json)
    {
        Assert.Throws<VocabularyPackFormatException>(() => VocabularyPackCodec.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Errors_never_quote_file_content()
    {
        var secret = "confidential-codename";
        var exception = Assert.Throws<VocabularyPackFormatException>(() =>
            VocabularyPackCodec.Parse(Encoding.UTF8.GetBytes($$"""["{{secret}}", "{{secret}}"]""")));

        Assert.DoesNotContain(secret, exception.Message);
    }

    [Fact]
    public void Rejects_invalid_utf8_and_oversized_files()
    {
        Assert.Throws<VocabularyPackFormatException>(() => VocabularyPackCodec.Parse([(byte)'[', (byte)'"', 0xC3, 0x28, (byte)'"', (byte)']']));

        var tooBig = new byte[VocabularyPackCodec.MaximumFileBytes + 1];
        Assert.Throws<VocabularyPackFormatException>(() => VocabularyPackCodec.Parse(tooBig));

        var tooMany = "[" + string.Join(",", Enumerable.Range(0, 201).Select(index => $"\"p{index}\"")) + "]";
        Assert.Throws<VocabularyPackFormatException>(() => VocabularyPackCodec.Parse(Encoding.UTF8.GetBytes(tooMany)));
    }

    [Fact]
    public void Serializes_a_canonical_sorted_indented_array_that_parses_back()
    {
        var json = VocabularyPackCodec.Serialize(["zeta", "Alpha", "Żółw"]);

        Assert.Equal("[\n  \"Alpha\",\n  \"zeta\",\n  \"Żółw\"\n]\n", json);
        Assert.Equal(["Alpha", "zeta", "Żółw"], VocabularyPackCodec.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Writes_atomically_and_leaves_an_existing_file_on_failure()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "pack.privatetype-vocabulary.json");
        File.WriteAllText(path, "old");

        VocabularyPackCodec.WriteAtomic(path, ["New"]);
        Assert.Equal(["New"], VocabularyPackCodec.Read(path));

        Assert.Throws<ArgumentException>(() => VocabularyPackCodec.WriteAtomic(path, ["two\nlines"]));
        Assert.Equal(["New"], VocabularyPackCodec.Read(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void Composes_personal_and_applicable_enabled_packs_once_each()
    {
        IReadOnlyList<VocabularyEntry> personal = [new("MVVM", "shared"), new("Wiesław", "pl")];
        IReadOnlyList<VocabularyPack> packs =
        [
            new("Dev", "en", true, ["Kubernetes", "MVVM"]),
            new("Shared names", "shared", true, ["PrivateType"]),
            new("Off", "shared", false, ["Disabled"]),
            new("Polish", "pl", true, ["Żółw"])
        ];

        Assert.Equal(["Kubernetes", "MVVM", "PrivateType"], VocabularyComposer.Compose(personal, packs, "en-GB"));
        Assert.Equal(["MVVM", "PrivateType", "Wiesław", "Żółw"], VocabularyComposer.Compose(personal, packs, "pl-PL"));
        Assert.Equal(["MVVM", "PrivateType"], VocabularyComposer.Compose(personal, packs, "auto"));
    }

    [Fact]
    public void Enforces_the_per_dictation_budget_per_language_and_names_it()
    {
        var english = Enumerable.Range(0, 150).Select(index => $"en {index}").ToArray();
        var shared = Enumerable.Range(0, 60).Select(index => $"shared {index}").ToArray();

        var fine = VocabularyRules.Validate([], [new("English", "en", true, english), new("Other", "pl", true, english)]);
        Assert.Null(fine);

        var error = VocabularyRules.Validate([], [new("English", "en", true, english), new("Shared", "shared", true, shared)]);
        Assert.NotNull(error);
        Assert.Contains("English", error);

        Assert.Null(VocabularyRules.Validate([], [new("English", "en", false, english), new("Shared", "shared", true, shared)]));
    }

    [Fact]
    public void Enforces_stored_totals_pack_names_and_in_pack_duplicates()
    {
        var packs = Enumerable.Range(0, 6)
            .Select(index => new VocabularyPack($"Pack {index}", "shared", false, Enumerable.Range(0, 180).Select(phrase => $"p{index}-{phrase}").ToArray()))
            .ToArray();
        Assert.NotNull(VocabularyRules.Validate([], packs));

        Assert.NotNull(VocabularyRules.Validate([], [new("Same", "en", true, ["a"]), new("Same", "pl", true, ["b"])]));
        Assert.NotNull(VocabularyRules.Validate([], [new("  ", "en", true, ["a"])]));
        Assert.NotNull(VocabularyRules.Validate([], [new("Dup", "en", true, ["a", "a"])]));
        Assert.NotNull(VocabularyRules.Validate([], [new("Scope", "xx", true, ["a"])]));
        Assert.NotNull(VocabularyRules.Validate([], Enumerable.Range(0, 51).Select(index => new VocabularyPack($"P{index}", "en", false, ["a"])).ToArray()));
    }

    [Fact]
    public void Round_trips_packs_and_loads_legacy_settings_without_packs()
    {
        var store = new PortableSettingsStore(directory);
        var settings = PortableSettings.Default with { VocabularyPacks = [new("Dev", "en", false, ["Kubernetes", "Nginx"])] };
        store.Save(settings);

        var loaded = store.Load();
        Assert.Null(loaded.Warning);
        var pack = Assert.Single(loaded.Settings.VocabularyPacks);
        Assert.Equal(("Dev", "en", false), (pack.Name, pack.Scope, pack.IsEnabled));
        Assert.Equal(["Kubernetes", "Nginx"], pack.Phrases);

        File.WriteAllText(store.SettingsPath, """{ "MicrophoneId": "default", "Shortcuts": [{ "Language": 0, "VirtualKey": 82 }] }""");
        Assert.Empty(store.Load().Settings.VocabularyPacks);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
