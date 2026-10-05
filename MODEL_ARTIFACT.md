# Multilingual CPU model artifacts

PrivateType offers two models. Each is downloaded separately after the user accepts its terms, and neither is included in the portable ZIP. The identities below are pinned in `src/PrivateType.Core/SpeechModels.cs`.

## Nemotron 3.5 ASR Streaming 0.6B

| Field | Value |
|---|---|
| Model | `nvidia/nemotron-3.5-asr-streaming-0.6b` |
| Runtime format | GGUF Q8_0 |
| Pinned repository revision | `ea30d66debe3740a08b573244286791d423d6b3e` |
| File | `nemotron-3.5-asr-streaming-0.6b.q8_0.gguf` |
| Expected bytes | `742090464` |
| Expected SHA-256 | `3fc991d3badad7277c11030a7519832cddaf2057aafed6d4b25147e953a070b1` |
| Destination | `models/` beside the executable |
| License | OpenMDW-1.1; it is accepted separately before download. |

PrivateType verifies both the byte count and SHA-256 before activating the downloaded file.

Revision `ea30d66` (2026-09-10) re-converted the GGUF with the model's SentencePiece tokenizer embedded (`asr.tokenizer.spm_model`), which the engine needs for RNNT word boosting. The previous pin, `1c8deae` (741,548,352 bytes, SHA-256 `a5c435f2…`), recognizes identically but cannot boost vocabulary.

## Parakeet TDT 0.6B v3 (recommended for new installs)

| Field | Value |
|---|---|
| Model | `nvidia/parakeet-tdt-0.6b-v3` |
| Runtime format | GGUF Q8_0, published by NVIDIA in the model repository |
| Pinned repository revision | `541d1f99c6b0c3cd0b11a95167540bb8edefd82b` |
| File | `parakeet-tdt-0.6b-v3.q8_0.gguf` |
| Expected bytes | `713975456` |
| Expected SHA-256 | `e3880d0aaaaf2c308ea2c35016b2b895c423eb3fda924c1b463d1c19b7f4d32e` |
| License | CC-BY-4.0 (attribution: NVIDIA); it is accepted separately before download. |

The pinned engine loads this GGUF as a TDT transducer (`head=tdt`, durations 0–4) but serves it offline only: `/v1/realtime` rejects it with "this transducer encoder is offline-only and cannot serve StreamingRecognize". PrivateType therefore keeps the held audio in memory and posts it once, as a 16 kHz PCM16 WAV, to `/v1/audio/transcriptions` on release. The engine ignores the language field (the model detects it) and ignores `speech_contexts` for this head ("boosting requires the Flashlight decoder").

