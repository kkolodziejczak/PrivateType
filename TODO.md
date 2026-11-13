# PrivateType roadmap

## Future improvements

- Evaluate Flashlight decoding only with a compatible CTC speech model. It enables lexicon, language-model, beam-search, and phrase-boosting controls, but does not apply to the current RNN-T Nemotron model. Any enabled build must receive a fresh dependency and license audit.
- Run a bounded whisper.cpp Polish/English streaming-quality spike before considering an engine/model replacement.
- Investigate platform-native macOS and explicitly named Linux hosts after Windows v1.0.0; .NET MAUI does not provide Linux desktop parity for this app.
- Ship a single self-unpacking `.exe` next to the ZIP, built on the existing launcher: double-click once, it unpacks per user and starts. The engine needs real files, and the model remains a separate download.
- Design a manual update window and workflow; automatic updates are out of scope for v1.0.0.
- Add a private note-taker mode: when dictation starts without an eligible target window, let the user explicitly save the transcript as a local note instead of inserting it into another app. Define how notes are opened, organized, and deleted without weakening PrivateType's privacy guarantees.
- Add reviewed, public curated packs to [vocabulary-packs](vocabulary-packs/) (none ship yet).
- Verify Settings and dialogs with Windows' "Make text bigger" accessibility setting.
- Allow modifiers other than Ctrl+Shift for dictation shortcuts.
- Fall back to the processor when the engine exits or times out starting on a graphics card. Today only a failed warm-up falls back, so every retry picks the same card again.
