namespace PrivateType.App;

// Decides when the recent-dictations list closes itself because the user moved on. Windows can
// refuse the focus handover when the list opens, or hand focus back to the previous app straight
// away; closing on that first loss made the list vanish before it was seen. So the list closes
// once it has held the foreground and lost it, or when a third window takes the foreground.
internal sealed class HistoryDismissal(nint openedOver)
{
    private nint openedOver = openedOver;

    public bool HeldForeground { get; private set; }

    public bool ShouldClose(nint foreground, nint self)
    {
        if (foreground == self)
        {
            HeldForeground = true;
            return false;
        }

        // No foreground window: a handover is still in progress.
        if (foreground == nint.Zero)
            return false;

        // Opened mid-handover: the first app to take the foreground is the one it opened over.
        if (openedOver == nint.Zero && !HeldForeground)
            openedOver = foreground;

        return HeldForeground || foreground != openedOver;
    }
}
