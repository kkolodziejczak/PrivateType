namespace PrivateType.App;

// The plain title of each event the app records, and a next step only where the code that records it makes the cause certain.
// An event with no entry shows its name with the dots turned into spaces.
internal static class DiagnosticTitles
{
    private static readonly Dictionary<string, (string Title, string? NextStep)> Titles = new(StringComparer.Ordinal)
    {
        // A dictation's problems: the key adds the detail that decides what happened.
        ["injection.skipped reason=empty-transcript"] = ("No words were recognized, so nothing was typed", null),
        ["injection.skipped reason=target-ineligible"] = ("The text wasn't typed: the window you dictated into changed or can't take text", null),
        ["session.failed phase=created"] = ("The speech engine couldn't start this dictation", null),
        ["session.failed phase=capture.start"] = ("The microphone couldn't start recording", null),
        ["session.failed phase=recording"] = ("Dictation stopped while recording", null),
        ["session.failed phase=capture.stop"] = ("The microphone couldn't stop recording", null),
        ["session.failed phase=pcm.drain"] = ("Dictation failed while it was being transcribed", null),
        ["session.failed phase=recognizer.complete"] = ("Dictation failed while it was being transcribed", null),
        ["session.failed phase=recognizer.updates"] = ("Dictation failed while it was being transcribed", null),
        ["session.failed phase=text.inject"] = ("Dictation failed while the text was being typed", null),
        ["session.failed"] = ("Dictation stopped with an error", null),
        ["text.correction.failed"] = ("Vocabulary corrections failed; the text was kept as recognized", null),

        // The application's problems.
        ["model.load.failed"] = ("The speech model couldn't load", "Holding the shortcut again tries to load it again."),
        ["model.switch.failed"] = ("Couldn't switch to the chosen model", null),
        ["model.choice.save.failed"] = ("The model choice couldn't be saved", null),
        ["engine.devices.failed"] = ("The graphics devices couldn't be listed for the speech engine", null),
        ["engine.warm-up.failed"] = ("The speech engine didn't answer its first test", null),
        ["engine.fallback.failed"] = ("The speech engine couldn't restart on the processor", "Holding the shortcut again loads it on the processor."),
        ["settings.save.failed"] = ("Settings couldn't be saved", null),
        ["settings.import.failed"] = ("Settings from the earlier copy couldn't be imported", null),
        ["startup.failed"] = ("The Start with Windows setting couldn't be updated", null),
        ["copies.startup.unreadable"] = ("The Windows startup entry couldn't be read", null),
        ["vocabulary.teach.save.failed"] = ("A taught phrase couldn't be saved", null),
        ["history.inject.failed"] = ("A recent dictation couldn't be pasted", null),
        ["history.paste.failed"] = ("A recent dictation couldn't be pasted", null),
        ["cue.failed"] = ("A sound cue couldn't play", null),
        ["bubble.recover.failed"] = ("The dictation bubble couldn't be redrawn", null),
        ["shutdown.step.failed"] = ("A step of closing PrivateType failed", null),
        ["shutdown.sessions.timeout"] = ("A dictation didn't finish before PrivateType closed", null),
        ["diagnostics.legacy-cleanup.failed"] = ("Old diagnostic files couldn't be removed", null),

        // Information.
        ["engine.started"] = ("Speech engine started", null),
        ["engine.warmed"] = ("Speech engine ready", null),
        ["engine.restarted"] = ("Speech engine restarted", null),
        ["engine.fallback"] = ("The graphics device failed, so the speech engine moved to the processor", null),
        ["model.loading"] = ("Loading the speech model", null),
        ["model.loaded"] = ("Speech model loaded", null),
        ["model.standby"] = ("Loading the speech model in the background", null),
        ["model.unload.scheduled"] = ("Speech model unload scheduled", null),
        ["model.unloaded"] = ("Speech model unloaded", null),
        ["model.switch"] = ("Model switched", null),
        ["session.started"] = ("Dictation started", null),
        ["session.state"] = ("Dictation state changed", null),
        ["session.finalizing"] = ("Dictation finishing", null),
        ["session.completed"] = ("Dictation finished", null),
        ["cue.transcribing"] = ("Transcribing cue", null),
        ["transcription.timed"] = ("Transcription timed", null),
        ["transcription.coverage"] = ("Transcription coverage checked", null),
        ["text.corrected"] = ("Vocabulary corrections applied", null),
        ["injection.started"] = ("Typing the text", null),
        ["injection.completed"] = ("Text typed", null),
        ["history.opened"] = ("Recent dictations opened", null),
        ["history.raised"] = ("Recent dictations brought forward", null),
        ["history.pasted"] = ("Recent dictation pasted", null),
        ["history.copied"] = ("Recent dictation copied", null),
        ["history.closed"] = ("Recent dictations closed", null),
        ["shortcut.release.recovered"] = ("A missed shortcut release was caught", null),
        ["bubble.recovered"] = ("Dictation bubble redrawn", null),
        ["vocabulary.taught"] = ("Phrase taught", null),
        ["settings.imported"] = ("Settings imported from the earlier copy", null),
        ["settings.import.declined"] = ("Settings import declined", null),
        ["settings.import.reviewed"] = ("Settings import reviewed", null),
        ["startup.claimed"] = ("Start with Windows moved to this copy", null),
        ["startup.claimed.confirmed"] = ("Start with Windows confirmed for this copy", null),
        ["startup.claim.declined"] = ("Start with Windows left with the other copy", null),
    };

    // The detail that decides a title: the reason a text wasn't typed, or the phase a dictation failed in.
    private static readonly string[] DecidingDetails = ["reason", "phase"];

    internal static (string Title, string? NextStep) Describe(DiagnosticEntry entry)
    {
        foreach (var key in DecidingDetails)
            if (entry.Details.TryGetValue(key, out var value) && Titles.TryGetValue($"{entry.EventName} {key}={value}", out var specific))
                return specific;
        return Titles.TryGetValue(entry.EventName, out var title) ? title : (entry.EventName.Replace('.', ' '), null);
    }
}
