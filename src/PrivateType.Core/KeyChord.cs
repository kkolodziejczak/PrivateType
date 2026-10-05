using System.Text.Json.Serialization;

namespace PrivateType.Core;

// A global shortcut with any modifiers, such as Win+Shift+V for recent dictations.
public sealed record KeyChord(bool Control, bool Shift, bool Alt, bool Windows, int VirtualKey)
{
    public static KeyChord DefaultHistory { get; } = new(Control: false, Shift: true, Alt: false, Windows: true, VirtualKey: 0x56);

    [JsonIgnore]
    public string Label => string.Join("+", LabelParts());

    private IEnumerable<string> LabelParts()
    {
        if (Windows)
            yield return "Win";
        if (Control)
            yield return "Ctrl";
        if (Alt)
            yield return "Alt";
        if (Shift)
            yield return "Shift";
        yield return KeyNames.Label(VirtualKey);
    }
}

public static class KeyNames
{
    public static bool IsLetterNumberOrFunctionKey(int virtualKey) =>
        virtualKey is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) or (>= 0x70 and <= 0x87);

    public static string Label(int virtualKey) => virtualKey switch
    {
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
        _ => $"VK-{virtualKey:X2}"
    };
}

public static class HistoryShortcutRules
{
    public static string? Validate(KeyChord? chord, IReadOnlyList<ShortcutBinding> dictationShortcuts)
    {
        if (chord is null || !KeyNames.IsLetterNumberOrFunctionKey(chord.VirtualKey))
            return "Use a letter, number, or function key for the recent dictations shortcut.";

        // One modifier would swallow ordinary typing, or the Ctrl+V that PrivateType itself sends to paste.
        var modifiers = (chord.Control ? 1 : 0) + (chord.Shift ? 1 : 0) + (chord.Alt ? 1 : 0);
        if (!chord.Windows && modifiers < 2)
            return "Use Win, or two of Ctrl, Alt, and Shift, so the recent dictations shortcut doesn't block typing or pasting.";

        if (chord is { Control: true, Alt: true, Windows: false })
            return "Ctrl+Alt is AltGr on many keyboards and types letters such as ą. Choose another recent dictations shortcut.";

        // Dictation shortcuts are Ctrl+Shift plus a key; the history shortcut wins only on an exact match.
        if (chord is { Control: true, Shift: true, Alt: false, Windows: false }
            && dictationShortcuts.Any(shortcut => shortcut?.VirtualKey == chord.VirtualKey))
            return $"{chord.Label} is already a dictation shortcut.";

        return null;
    }
}
