using System.IO;
using System.Runtime.InteropServices;
using PrivateType.Core;
using Forms = System.Windows.Forms;

namespace PrivateType.App;

// Inserts text by pasting it, then puts the user's previous clipboard back. Dictated text is
// marked so Windows clipboard history, cloud clipboard, and clipboard monitors skip it.
internal sealed class ClipboardPasteInjector : ITextInjector
{
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(600);
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkV = 0x56;

    public void Inject(string text)
    {
        Forms.IDataObject? previous = null;
        uint sequence = 0;
        RunOnStaThread(() =>
        {
            previous = Snapshot(Forms.Clipboard.GetDataObject());
            try
            {
                Forms.Clipboard.SetDataObject(CreatePasteData(text), copy: true, retryTimes: 10, retryDelay: 50);
            }
            catch (ExternalException exception)
            {
                throw new InvalidOperationException("The clipboard is busy, so the text was not pasted.", exception);
            }
            sequence = GetClipboardSequenceNumber();
        });

        var inputs = PasteInputs(IsPressed(VkShift), IsPressed(VkMenu));
        if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) != (uint)inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the paste shortcut.");

        _ = RestoreLaterAsync(previous, sequence);
    }

    internal static Forms.DataObject CreatePasteData(string text)
    {
        var data = new Forms.DataObject();
        data.SetData(Forms.DataFormats.UnicodeText, text);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));
        return data;
    }

    // Held Shift or Alt would turn Ctrl+V into a different shortcut in many apps, so release them first.
    internal static NativeMethods.Input[] PasteInputs(bool shiftDown, bool altDown)
    {
        var inputs = new List<NativeMethods.Input>();
        if (shiftDown)
            inputs.Add(Key(VkShift, keyUp: true));
        if (altDown)
            inputs.Add(Key(VkMenu, keyUp: true));
        inputs.Add(Key(VkControl, keyUp: false));
        inputs.Add(Key(VkV, keyUp: false));
        inputs.Add(Key(VkV, keyUp: true));
        inputs.Add(Key(VkControl, keyUp: true));
        return inputs.ToArray();
    }

    private static async Task RestoreLaterAsync(Forms.IDataObject? previous, uint sequence)
    {
        // Give the target time to read the clipboard before restoring it.
        await Task.Delay(RestoreDelay).ConfigureAwait(false);
        try
        {
            RunOnStaThread(() =>
            {
                // Something else replaced the clipboard after the paste; keep the newer content.
                if (GetClipboardSequenceNumber() != sequence)
                    return;

                if (previous is null)
                    Forms.Clipboard.Clear();
                else
                    Forms.Clipboard.SetDataObject(previous, copy: true, retryTimes: 10, retryDelay: 50);
            });
        }
        catch (Exception)
        {
            // Restoring is best effort; the paste itself already succeeded.
        }
    }

    // Copies every readable format whose value can be put back without serialization (text, file
    // lists, raw streams, images), so the snapshot survives after the clipboard owner changes.
    private static Forms.IDataObject? Snapshot(Forms.IDataObject? current)
    {
        if (current is null)
            return null;

        var copy = new Forms.DataObject();
        var copied = 0;
        foreach (var format in current.GetFormats(autoConvert: false))
        {
            try
            {
                if (current.GetData(format, autoConvert: false) is { } value and (string or string[] or MemoryStream or System.Drawing.Image))
                {
                    copy.SetData(format, value);
                    copied++;
                }
            }
            catch (Exception)
            {
                // Some owners refuse or cannot render a format; skip it.
            }
        }

        return copied == 0 ? null : copy;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
    }

    private static NativeMethods.Input Key(ushort virtualKey, bool keyUp) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeybdInput { Vk = virtualKey, Flags = keyUp ? NativeMethods.KeyEventKeyUp : 0 }
        }
    };

    private static bool IsPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
