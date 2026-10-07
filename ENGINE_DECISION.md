# Speech engine decision

**Status: selected for the Windows portable release.**

PrivateType uses NVIDIA NeMo-Speech.cpp with the `nvidia/nemotron-3.5-asr-streaming-0.6b` Q8_0 model. The native process is loopback-only (`127.0.0.1`) and receives 16 kHz PCM16 audio from the app over its realtime WebSocket endpoint.

## Runtime

Since the release after 1.4.0, PrivateType ships NVIDIA's own prebuilt NeMo-Speech.cpp 0.2.0 Windows release, `nemo-speech-0.2.0-windows-x86_64-vulkan.zip` (SHA-256 `edf15a04ba98740aa0ab75ae4681ac7ad0a488b22b35ce2cbebd10590bb1c1e1`), unmodified. [Get-NemoSpeechRuntime.ps1](Get-NemoSpeechRuntime.ps1) downloads it, checks the hash, and unpacks it; the release workflow runs it instead of building from source. The portable ZIP contains that runtime but never the speech model.

Up to 1.4.0, the workflow built revision `1118951337094db3b362fbf1b27e871696f10590` from source for the CPU only, with a Windows SentencePiece linker patch. That build used four compute threads and had no graphics backend.

## Device choice

The 0.2.0 build includes CPU and Vulkan backends. Its own automatic choice takes the first Vulkan device, which on laptops is usually the slower integrated graphics, so PrivateType reads `nemo-speech --json doctor` and passes `--device` itself: a discrete graphics card, then integrated graphics, each with at least 1 GiB free, then the processor (up to eight threads). After the engine is ready, a one-second silent request warms it up; if that fails on a graphics device, the engine restarts on the processor for the rest of the session. The user can keep the model on the processor in **Settings → Model**.

Measured on 2026-10-07 on an Intel Core i7-11850H laptop (8 cores, 16 threads) with Intel UHD graphics and an NVIDIA GeForce MX450 (2 GiB), Parakeet Q8_0, 70 s of synthetic English speech, warm engine:

| Device | 70 s clip | Ready after start |
| --- | --- | --- |
| Pinned 1.4.0 build, CPU, 4 threads | 14.6–15.5 s | about 1.3 s |
| 0.2.0, CPU, 8 threads | 12.5–15.5 s | about 2 s |
| 0.2.0, Intel UHD (Vulkan) | 6.3–6.8 s | about 1.1 s |
| 0.2.0, NVIDIA MX450 (Vulkan) | 1.85–1.9 s | about 1.6 s |

On the processor, a preview still running at release used to hold up the final pass. The engine cannot abandon a request, so when it runs on the processor and the preview has not finished within one second, PrivateType restarts the engine instead of waiting: releasing three seconds into a long preview took 16.9 s with a restart and 26.7 s without.

## Evidence

- The pinned model passed its expected 742,090,464-byte and SHA-256 verification.
- The runtime exposes Polish, English, and automatic language prompts and runs on CPU only.
- English and Polish fixtures produced provisional and finalized realtime events through the same protocol used by the app.
- The ready local runtime used about 935 MiB of working set on the recorded test machine (model revision `ea30d66`, 2026-09-28).
- A manual Polish microphone test was accepted for continuous phrases. Isolated words and pauses remain a known recognition-quality limitation.

## Second model: Parakeet TDT 0.6B v3

The same runtime and `serve` command also load `nvidia/parakeet-tdt-0.6b-v3` Q8_0 (see [MODEL_ARTIFACT.md](MODEL_ARTIFACT.md)). It is offline-only, so the app records the whole hold and transcribes it once through `/v1/audio/transcriptions`. Measured on the recorded test machine (2026-10-01): a 13 s FLEURS Polish clip was transcribed exactly, with punctuation, in about 0.9 s after release; a 61 s clip took 5.1 s; the ready runtime used about 790 MiB of working set, peaking near 1.14 GiB on the 61 s clip.

## Scope

This is the selected v1 engine. It is not a general engine benchmark or a cross-platform commitment. Future decoder or engine experiments are listed in [TODO.md](TODO.md).

See [MODEL_ARTIFACT.md](MODEL_ARTIFACT.md) for the model identity, checksum, and separate model terms.