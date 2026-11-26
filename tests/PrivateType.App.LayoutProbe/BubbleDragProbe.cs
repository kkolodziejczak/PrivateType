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
        VerifyDragRestoresRestingKey();

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
        foreach (var regrabsAtBorder in pixelsPerStep == 1 ? new[] { false, true } : [false])
            VerifyDrag(source, target, grabFraction, followsTargetCenter, startsAtTop, pixelsPerStep, regrabsAtBorder);

        foreach (var source in screens)
        foreach (var target in screens.Where(screen => screen != source))
        foreach (var grabFraction in new[] { 0.3, 0.5, 0.7 })
        foreach (var startsAtTop in new[] { false, true })
            VerifyJumpsAcrossBorder(source, target, grabFraction, startsAtTop);
    }

    // A drag starts on the hovered key, so its shortcut card is open and the key drawn at full strength. Grabbing the key
    // closes the card for the drag. The bubble lands under the pointer, which still rests on the key, so the card is back;
    // once the pointer leaves, the key returns to its resting look instead of staying solid until the next hover.
    private static void VerifyDragRestoresRestingKey()
    {
        foreach (var modelLoaded in new[] { true, false })
        {
            var bubble = new DictationBubble();
            bubble.ShowReady(PortableSettings.Default, modelLoaded);
            bubble.UpdateLayout();
            var popup = (System.Windows.Controls.Primitives.Popup)bubble.FindName("BindingsPopup");
            var shell = (System.Windows.UIElement)bubble.FindName("BubbleShell");
            var key = (System.Windows.UIElement)bubble.FindName("IconTile");
            bubble.ShowBindingsCard();
            var handle = new WindowInteropHelper(bubble).Handle;
            var start = PhysicalRect(handle);
            var x = (start.Left + start.Right) / 2;
            var y = (start.Top + start.Bottom) / 2;
            var cardOpened = popup.IsOpen;
            var grabbed = Inject(bubble, NativeMethods.WmLButtonDown, x, y) == 1;
            Inject(bubble, NativeMethods.WmMouseMove, x + 30, y);
            var cardOpenWhileDragging = popup.IsOpen;
            Inject(bubble, NativeMethods.WmLButtonUp, x + 30, y);
            var moved = PhysicalRect(handle).Left != start.Left;
            var cardOpenAfterDrop = popup.IsOpen;
            key.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            var opacity = shell.Opacity;
            var cardOpenAfterLeave = popup.IsOpen;
            bubble.Close();

            var model = modelLoaded ? "with" : "without";
            if (!cardOpened || !grabbed || !moved)
                throw new InvalidOperationException($"The drag from the hovered key did not run {model} a model: card opened {cardOpened}, grabbed {grabbed}, moved {moved}.");
            if (cardOpenWhileDragging || !cardOpenAfterDrop)
                throw new InvalidOperationException(
                    $"Dragging {model} a model, the card was {(cardOpenWhileDragging ? "open" : "closed")} during the drag and {(cardOpenAfterDrop ? "open" : "closed")} after the drop with the pointer on the key; it belongs closed, then open.");
            var expected = DictationBubble.OpacityForReadyState(modelLoaded);
            if (cardOpenAfterLeave || Math.Abs(opacity - expected) > 0.001)
                throw new InvalidOperationException(
                    $"After a drag {model} a model and the pointer leaving, the bubble is at opacity {opacity:F2} with the card {(cardOpenAfterLeave ? "open" : "closed")}; at rest it is {expected:F2} with the card closed.");
        }

        Console.WriteLine("PASS: A drag from the hovered key closes the card, brings it back on the drop, and leaves the resting look once the pointer goes, with and without a model.");

        // The key is round. A drop in a corner of its square, outside the circle, is a pointer Windows passes through and
        // WPF never sees over the key, so no leave would ever close a card opened there.
        var corner = new DictationBubble();
        corner.ShowReady(PortableSettings.Default, modelLoaded: true);
        corner.UpdateLayout();
        var cornerKey = (System.Windows.UIElement)corner.FindName("IconTile");
        var grab = cornerKey.PointToScreen(new System.Windows.Point(2, 2));
        var (gx, gy) = ((int)grab.X, (int)grab.Y);
        var grabbedCorner = Inject(corner, NativeMethods.WmLButtonDown, gx, gy) == 1;
        Inject(corner, NativeMethods.WmMouseMove, gx + 30, gy);
        Inject(corner, NativeMethods.WmLButtonUp, gx + 30, gy);
        var cardOpenAfterCornerDrop = ((System.Windows.Controls.Primitives.Popup)corner.FindName("BindingsPopup")).IsOpen;
        corner.Close();
        if (!grabbedCorner || cardOpenAfterCornerDrop)
            throw new InvalidOperationException($"A drop outside the key's circle: grabbed {grabbedCorner}, card {(cardOpenAfterCornerDrop ? "open" : "closed")}; it belongs closed.");

        Console.WriteLine("PASS: A drop outside the key's circle leaves the shortcut card shut.");
    }

    // A fast mouse lands anywhere near the border from outside the bubble. The bubble may leave the
    // grabbed spot for a moment there, but it must stay under the pointer and keep a saved position.
    private static void VerifyJumpsAcrossBorder(Forms.Screen source, Forms.Screen target, double grabFraction, bool startsAtTop)
    {
        const int JumpPixels = 40;
        const int BorderZonePixels = 60;
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
        Inject(bubble, NativeMethods.WmLButtonDown, startX, startY);

        var direction = Math.Sign(endX - startX);
        var border = direction > 0 ? targetMonitor.Left : targetMonitor.Right;
        var jumps = 0;
        for (var x = border - BorderZonePixels; x <= border + BorderZonePixels; x++)
        foreach (var from in new[] { x - direction * JumpPixels, x + direction * JumpPixels })
        {
            Inject(bubble, NativeMethods.WmMouseMove, from, startY);
            Inject(bubble, NativeMethods.WmMouseMove, x, startY);
            jumps++;
            var rect = PhysicalRect(handle);
            if (x < rect.Left || x >= rect.Right || startY < rect.Top || startY >= rect.Bottom)
            {
                bubble.Close();
                throw new InvalidOperationException($"A jump from {from} to {x},{startY} left the bubble at {rect.Left},{rect.Top} ({rect.Right - rect.Left} wide), away from the pointer, grabbed at {grabFraction:P0}.");
            }
        }

        Inject(bubble, NativeMethods.WmLButtonUp, border, startY);
        bubble.Close();
        Console.WriteLine($"PASS: The bubble stayed under the pointer through {jumps} jumps across the border from {source.DeviceName}, grabbed at {grabFraction:P0}, from the {(startsAtTop ? "top" : "bottom")}.");
    }

    // One pixel per step finds narrow bands at the monitor border; a fast mouse jumps across them.
    // Near the top of the screen the monitors' coordinates almost agree, hiding a wrong placement.
    // Regrabbing drops the bubble and picks it up again at every position straddling the border.
    private static void VerifyDrag(Forms.Screen source, Forms.Screen target, double grabFraction, bool followsTargetCenter, bool startsAtTop, int pixelsPerStep, bool regrabsAtBorder)
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
        var heldX = grabFraction;
        var heldY = 0.5;
        for (var step = 1; step <= steps; step++)
        {
            var x = startX + (endX - startX) * step / steps;
            var y = startY + (endY - startY) * step / steps;
            Inject(bubble, NativeMethods.WmMouseMove, x, y);
            var rect = PhysicalRect(handle);
            smallestWidth = Math.Min(smallestWidth, rect.Right - rect.Left);
            largestWidth = Math.Max(largestWidth, rect.Right - rect.Left);
            var drift = GrabDrift(rect, x, y, heldX, heldY);
            if (drift > worstDrift)
            {
                worstDrift = drift;
                worstAt = (x, y);
            }

            // Picking it up takes hold of whatever spot is now under the pointer. Waiting at the border
            // walks that spot a little with every regrab, so stop before it reaches the icon's edge.
            var spotX = (x - rect.Left) / (double)(rect.Right - rect.Left);
            var spotY = (y - rect.Top) / (double)(rect.Bottom - rect.Top);
            if (regrabsAtBorder && spotX is > 0.3 and < 0.7 && spotY is > 0.3 and < 0.7
                && rect.Left < targetMonitor.Right && rect.Right > targetMonitor.Left && (rect.Left < targetMonitor.Left || rect.Right > targetMonitor.Right))
            {
                Inject(bubble, NativeMethods.WmLButtonUp, x, y);
                if (Inject(bubble, NativeMethods.WmLButtonDown, x, y) != 1)
                {
                    bubble.Close();
                    throw new InvalidOperationException($"The bubble dropped at {rect.Left},{rect.Top} ({rect.Right - rect.Left} wide) on the monitor border could not be picked up again at {x},{y}.");
                }

                heldX = spotX;
                heldY = spotY;
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

        var route = $"{source.DeviceName} to {target.DeviceName}, grabbed at {grabFraction:P0}, {(followsTargetCenter ? "diagonal" : "level")} from the {(startsAtTop ? "top" : "bottom")}, {pixelsPerStep}px per step{(regrabsAtBorder ? ", regrabbed at the border" : "")}";
        // An off-center grab waits at the border for as long as the two sizes disagree about the monitor.
        var allowedDrift = RoundingPixels + (largestWidth - smallestWidth) * Math.Abs(grabFraction - 0.5);
        if (worstDrift > allowedDrift)
            throw new InvalidOperationException($"The bubble drifted {worstDrift:F0} physical pixels from the pointer at {worstAt.X},{worstAt.Y} dragging {route}; it ended at {endRect.Left},{endRect.Top}.");

        Console.WriteLine($"PASS: The bubble stayed within {worstDrift:F1} physical pixels of the pointer dragging {route}.");
    }

    // How far the spot the user grabbed has moved from the pointer. The window's physical size
    // changes with the monitor it is on, so the spot is a fraction of its current size.
    private static double GrabDrift(NativeMethods.NativeRect window, int cursorX, int cursorY, double heldX, double heldY)
    {
        var grabbedX = window.Left + (window.Right - window.Left) * heldX;
        var grabbedY = window.Top + (window.Bottom - window.Top) * heldY;
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
