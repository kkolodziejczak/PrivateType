namespace PrivateType.Core;

public sealed class CommitCoordinator
{
    private string committedText = string.Empty;
    private readonly HashSet<string> appliedCommitIds = new(StringComparer.Ordinal);

    public string ProvisionalText { get; private set; } = string.Empty;

    public void Apply(TranscriptUpdate update)
    {
        if (!update.IsCommitted)
        {
            ProvisionalText = update.Text;
            return;
        }

        ApplyCommittedUpdate(update);
        ProvisionalText = string.Empty;
    }

    public string TakeFinalText()
    {
        var result = committedText;
        committedText = string.Empty;
        appliedCommitIds.Clear();
        ProvisionalText = string.Empty;
        return result;
    }

    private void ApplyCommittedUpdate(TranscriptUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.CommitId))
            throw new InvalidOperationException("Committed transcript updates require a stable commit identifier.");

        if (!appliedCommitIds.Add(update.CommitId))
            return;

        if (update.BoundaryOverlap < 0 || update.BoundaryOverlap > update.Text.Length || update.BoundaryOverlap > committedText.Length)
            throw new InvalidOperationException("Committed transcript overlap is outside the available text boundary.");

        if (update.BoundaryOverlap > 0
            && !committedText.EndsWith(update.Text[..update.BoundaryOverlap], StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Committed transcript overlap does not match the existing text boundary.");
        }

        committedText += update.Text[update.BoundaryOverlap..];
    }
}
