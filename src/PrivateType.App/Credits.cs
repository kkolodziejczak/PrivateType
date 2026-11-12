using System.IO;
using System.Text.Json;

namespace PrivateType.App;

// One part of the portable release, as Credits.json declares it. Group and role are the window's; release and texts the notices'.
internal sealed record CreditedComponent(string Group, string Name, string? Maker, string Role, string License, string? Includes);

// A model the person downloads separately, under its own terms.
internal sealed record SeparateModel(string Name, string License);

// What the portable release is declared to contain: the file the release script also writes THIRD-PARTY-NOTICES.txt from.
// Whether the license texts are in this copy is a separate fact, read from the licenses folder.
internal sealed record Credits(IReadOnlyList<CreditedComponent> Components, IReadOnlyList<SeparateModel> Models)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static Credits Declared { get; } = Load();

    private static Credits Load()
    {
        using var stream = typeof(Credits).Assembly.GetManifestResourceStream("PrivateType.Credits.json")
            ?? throw new InvalidOperationException("The app was built without Credits.json.");
        return JsonSerializer.Deserialize<Credits>(stream, Json) ?? throw new InvalidDataException("Credits.json is empty.");
    }

    // "CC-BY-4.0 (Parakeet) and OpenMDW-1.1 (Nemotron)", the same wording the notices use.
    public string ModelTerms => JoinWithAnd(Models.Select(model => $"{model.License} ({model.Name})").ToList());

    private static string JoinWithAnd(IReadOnlyList<string> items)
        => items.Count < 2 ? string.Concat(items) : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";
}
