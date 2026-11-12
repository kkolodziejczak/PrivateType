using System.Globalization;
using System.Windows.Data;

namespace PrivateType.App;

// One keycap of a shortcut such as "Ctrl+Shift+R". The final key is the main one; text that is not a chord is a prompt.
public sealed record Keycap(string Legend, bool IsMain, bool IsPrompt);

// Splits a shortcut label into the keycaps Settings draws for it.
public sealed class KeycapLegendsConverter : IValueConverter
{
    internal static IReadOnlyList<Keycap> Split(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return [];
        if (label.Contains(' '))
            return [new Keycap(label, IsMain: false, IsPrompt: true)];

        // "Ctrl+Shift++" names the plus key itself.
        var plusKey = label.EndsWith("++", StringComparison.Ordinal);
        var keys = label.Split('+', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (plusKey)
            keys.Add("+");
        return keys.Select((key, index) => new Keycap(key, IsMain: index == keys.Count - 1, IsPrompt: false)).ToArray();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Split(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
