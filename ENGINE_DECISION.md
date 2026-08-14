# Speech engine decision

**Status: selected for the Windows portable release.**

PrivateType uses NVIDIA NeMo-Speech.cpp with the `nvidia/nemotron-3.5-asr-streaming-0.6b` Q8_0 model. The native process is loopback-only (`127.0.0.1`) and receives 16 kHz PCM16 audio from the app over its realtime WebSocket endpoint.

## Reproducible build

The GitHub Actions release workflow checks out NeMo-Speech.cpp at revision `1118951337094db3b362fbf1b27e871696f10590`, builds its CPU runtime through [Build-NemoSpeechCpu.ps1](Build-NemoSpeechCpu.ps1), and applies the checked Windows SentencePiece linker patch. The portable ZIP contains that runtime but never the speech model.

## Evidence

- The pinned model passed its expected 741,548,352-byte and SHA-256 verification.
- The runtime exposes Polish, English, and automatic language prompts and runs on CPU only.
- English and Polish fixtures produced provisional and finalized realtime events through the same protocol used by the app.
- The ready local runtime used about 932 MiB of working set on the recorded test machine.
- A manual Polish microphone test was accepted for continuous phrases. Isolated words and pauses remain a known recognition-quality limitation.

## Scope

This is the selected v1 engine. It is not a general engine benchmark or a cross-platform commitment. Future decoder or engine experiments are listed in [TODO.md](TODO.md).

See [MODEL_ARTIFACT.md](MODEL_ARTIFACT.md) for the model identity, checksum, and separate model terms.