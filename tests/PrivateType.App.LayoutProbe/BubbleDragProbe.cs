using System.Runtime.InteropServices;
using System.Windows.Interop;
using PrivateType.App;
using PrivateType.Core;
using Forms = System.Windows.Forms;

// Drags a real bubble between the real monitors by feeding cursor positions to its mouse hook, then
// asks Windows where the window ended up. The hook's cursor and this measurement are physical pixels,
// so the check holds whatever scale each monitor has. The user's own pointer is not moved.
internal static class BubbleDragProbe
{
    private const double RoundingPixels = 3;

    public static void Run()
    {
        var screens = Forms.Screen.AllScreens;
        if (screens.Length < 2)
        {
            Console.WriteLine("SKIP: The bubble drag probe needs two monitors.");
            return;
        }

        foreach (var source in screens)
        foreach (var target in screens.Where(screen => screen != source))
        foreach (var grabFraction in new[] { 0.3, 0.5, 0.7 })
        foreach (var followsTargetCenter in new[] { true, false })
        foreach (var startsAtTop in new[] { false, true })
        foreach (var pixelsPerStep in new[] { 1, 37 })
            VerifyDrag(source, target, grabFraction, followsTargetCenter, startsAtTop, pixelsPerStep);
    }

    // One pixel per step finds narrow bands at the monitor border; a fast mouse jumps across them.
    // Near the top of the screen the monitors' coordinates almost agree, hiding a wrong placement.
    private static void VerifyDrag(Forms.Screen source, Forms.Screen target, double grabFraction, bool followsTargetCenter, bool startsAtTop, int pixelsPerStep)
    {
        var bubble = new DictationBubble();
        bubble.ShowReady(PortableSettings.Default, modelLoaded: true);
        var handle = new WindowInteropHelper(bubble).Handle;
        bubble.MoveToScreen(target);
        bubble.UpdateLayout();
        var targetMonitor = PhysicalMonitorOf(handle);
        bubble.MoveToScreen(source);
        if (startsAtTop)
            bubble.Top = source.WorkingArea.Top / System.Windows.Media.VisualTreeHelper.GetDpi(bubble).DpiScaleY;
        bubble.UpdateLayout();

        var start = PhysicalRect(handle);
        var startX = start.Left + (int)((start.Right - start.Left) * grabFraction);
        var startY = (start.Top + start.Bottom) / 2;
        var endX = (targetMonitor.Left + targetMonitor.Right) / 2;
        var endY = followsTargetCenter ? (targetMonitor.Top + targetMonitor.Bottom) / 2 : Math.Clamp(startY, targetMonitor.Top, targetMonitor.Bottom - 1);

        if (Inject(bubble, NativeMethods.WmLButtonDown, startX, startY) != 1)
        {
            bubble.Close();
            throw new InvalidOperationException($"The bubble did not start a drag at {startX},{startY}.");
        }

        var steps = Math.Max(Math.Abs(endX - startX), Math.Abs(endY - startY)) / pixelsPerStep;
        var worstDrift = 0d;
        var worstAt = (X: startX, Y: startY);
        var smallestWidth = start.Right - start.Left;
        var largestWidth = smallestWidth;
        for (var step = 1; step <= steps; step++)
        {
            var x = startX + (endX - startX) * step / steps;
            var y = startY + (endY - startY) * step / steps;
            Inject(bubble, NativeMethods.WmMouseMove, x, y);
            var rect = PhysicalRect(handle);
            smallestWidth = Math.Min(smallestWidth, rect.Right - rect.Left);
            largestWidth = Math.Max(largestWidth, rect.Right - rect.Left);
            var drift = GrabDrift(rect, x, y, grabFraction);
            if (drift > worstDrift)
            {
                worstDrift = drift;
                worstAt = (x, y);
            }
        }

        Inject(bubble, NativeMethods.WmLButtonUp, endX, endY);
        var endRect = PhysicalRect(handle);
        NativeMethods.GetWindowRect(handle, out var windowSpaceRect);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(bubble);
        var positionOutOfStep = Math.Max(Math.Abs(bubble.Left * dpi.DpiScaleX - windowSpaceRect.Left), Math.Abs(bubble.Top * dpi.DpiScaleY - windowSpaceRect.Top));
        bubble.Close();
        if (positionOutOfStep > 1)
            throw new InvalidOperationException($"The bubble's Left and Top are {positionOutOfStep:F0} pixels from its real window after a drag, so its saved position would be wrong.");

        var route = $"{source.DeviceName} to {target.DeviceName}, grabbed at {grabFraction:P0}, {(followsTargetCenter ? "diagonal" : "level")} from the {(startsAtTop ? "top" : "bottom")}, {pixelsPerStep}px per step";
        // An off-center grab waits at the border for as long as the two sizes disagree about the monitor.
        var allowedDrift = RoundingPixels + (largestWidth - smallestWidth) * Math.Abs(grabFraction - 0.5);
        if (worstDrift > allowedDrift)
            throw new InvalidOperationException($"The bubble drifted {worstDrift:F0} physical pixels from the pointer at {worstAt.X},{worstAt.Y} dragging {route}; it ended at {endRect.Left},{endRect.Top}.");

        Console.WriteLine($"PASS: The bubble stayed within {worstDrift:F1} physical pixels of the pointer dragging {route}.");
    }

    // How far the spot the user grabbed has moved from the pointer. The window's physical size
    // changes with the monitor it is on, so the spot is a fraction of its current size.
    private static double GrabDrift(NativeMethods.NativeRect window, int cursorX, int cursorY, double grabFraction)
    {
        var grabbedX = window.Left + (window.Right - window.Left) * grabFraction;
        var grabbedY = window.Top + (window.Bottom - window.Top) * 0.5;
        return Math.Max(Math.Abs(grabbedX - cursorX), Math.Abs(grabbedY - cursorY));
    }

    private static nint Inject(DictationBubble bubble, int message, int x, int y)
    {
        var hookData = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.NativePoint>());
        try
        {
            Marshal.StructureToPtr(new NativeMethods.NativePoint { X = x, Y = y }, hookData, false);
            return bubble.OnMouseHook(0, message, hookData);
        }
        finally
        {
            Marshal.FreeHGlobal(hookData);
        }
    }

    private static NativeMethods.NativeRect PhysicalRect(nint window)
    {
        var previous = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        try
        {
            if (!NativeMethods.GetWindowRect(window, out var rect))
                throw new InvalidOperationException("Could not read the bubble's window rectangle.");
            return rect;
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previous);
        }
    }

    private static NativeMethods.NativeRect PhysicalMonitorOf(nint window)
    {
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        var previous = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        try
        {
            if (!NativeMethods.GetMonitorInfoW(NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest), ref info))
                throw new InvalidOperationException("Could not read the bubble's monitor.");
            return info.Monitor;
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previous);
        }
    }
}
