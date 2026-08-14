# PrivateType roadmap

## Future improvements

- Evaluate Flashlight decoding only with a compatible CTC speech model. It enables lexicon, language-model, beam-search, and phrase-boosting controls, but does not apply to the current RNN-T Nemotron model. Any enabled build must receive a fresh dependency and license audit.
- Run a bounded whisper.cpp Polish/English streaming-quality spike before considering an engine/model replacement.
- Investigate platform-native macOS and explicitly named Linux hosts after Windows v1.0.0; .NET MAUI does not provide Linux desktop parity for this app.
- Explore a one-file extractor after v1.0.0. The current transparent folder ZIP remains the release format because the engine needs real files and the model remains a separate download.
- Design a manual update window and workflow; automatic updates are out of scope for v1.0.0.
