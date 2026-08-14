using PrivateType.Core;

namespace PrivateType.App;

internal sealed class UnicodeTextInjector : ITextInjector
{
    public void Inject(string text)
    {
        var inputs = text.SelectMany(character => new[]
        {
            new NativeMethods.Input { Type = NativeMethods.InputKeyboard, Data = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeybdInput { Scan = character, Flags = NativeMethods.KeyEventUnicode } } },
            new NativeMethods.Input { Type = NativeMethods.InputKeyboard, Data = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeybdInput { Scan = character, Flags = NativeMethods.KeyEventUnicode | NativeMethods.KeyEventKeyUp } } }
        }).ToArray();

        if (inputs.Length > 0 && NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Input>()) != (uint)inputs.Length)
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Windows rejected part of the dictated text.");
    }
}
