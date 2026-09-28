# Multilingual CPU model artifact

This model is downloaded separately by PrivateType after the user accepts its terms. It is not included in the portable ZIP.

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