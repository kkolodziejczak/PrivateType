# PrivateType roadmap

## Future improvements

- Evaluate Flashlight decoding only with a compatible CTC speech model. It enables lexicon, language-model, beam-search, and phrase-boosting controls, but does not apply to the current RNN-T Nemotron model. Any enabled build must receive a fresh dependency and license audit.
- Run a bounded whisper.cpp Polish/English streaming-quality spike before considering an engine/model replacement.
- Investigate platform-native macOS and explicitly named Linux hosts after Windows v1.0.0; .NET MAUI does not provide Linux desktop parity for this app.
- Explore a one-file extractor after v1.0.0. The current transparent folder ZIP remains the release format because the engine needs real files and the model remains a separate download.
- Design a manual update window and workflow; automatic updates are out of scope for v1.0.0.
- Add a private note-taker mode: when dictation starts without an eligible target window, let the user explicitly save the transcript as a local note instead of inserting it into another app. Define how notes are opened, organized, and deleted without weakening PrivateType's privacy guarantees.
- Build custom vocabulary (plan Stages 3–6 in [VOCABULARY_AND_LANGUAGE_IMPLEMENTATION_PLAN.md](VOCABULARY_AND_LANGUAGE_IMPLEMENTATION_PLAN.md)); boosting is calibrated on model revision `ea30d66`.
- Verify the Settings language selector at 125%, 150%, and 200% display scaling.
- Allow modifiers other than Ctrl+Shift for dictation shortcuts.
- ASR compute threads are fixed at 4 inside the pinned engine; making them configurable requires an engine patch and a latency benchmark.
- Sign release executables so Windows SmartScreen does not warn on first launch.
