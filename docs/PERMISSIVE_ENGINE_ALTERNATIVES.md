# Permissive local-ASR alternatives

Research date: 2026-08-14. This is an engineering assessment, not legal
advice. All external claims below use first-party project documentation,
licenses, source, or model cards.

## Bottom line

PrivateType can license its own source under MIT with any of the candidates
below. A dependency does not become MIT merely because the application is MIT;
the release must still preserve that dependency's license and any required
notices. Free distribution does not remove redistribution conditions.

For the smallest and clearest license footprint, the best alternative is a
CPU-only **whisper.cpp + original OpenAI multilingual Whisper** stack. Both the
runtime and the original code/model weights are MIT-licensed. The important
product tradeoff is that whisper.cpp documents its microphone stream as a
"naive" rolling transcription example, not a native cache-aware streaming
model. A spike must prove latency, stable partial-text reconciliation, and
Polish quality before replacing the working Nemotron path.

If true streaming and minimal resource use matter more than recognition
quality, **Vosk with the specifically listed Apache-2.0 English and Polish
models** is the strongest fallback. It has a streaming API and C# bindings, but
requires two language models, has no verified automatic Polish/English model
selection path, and does not provide comparable built-in punctuation for both
languages.

Do not migrate to sherpa-onnx solely for license simplification. Its runtime is
Apache-2.0 and its C# integration is attractive, but its official online-model
catalog currently has no Polish streaming model; its Whisper path is explicitly
non-streaming. Each downloaded model artifact would also need its own license
verification.

## Current integration seam

Verified in this repository:

- `DictationSession` sends 16 kHz mono PCM frames through
  `IStreamingRecognizer`, consumes replaceable provisional text, and commits
  immutable final text.
- `RealtimeRecognizer` translates that interface to the current loopback
  WebSocket protocol (`session.update`, binary PCM, delta, completed).
- `EngineHost` owns one local engine process and expects readiness and explicit
  unload/reload behavior.

This is a useful isolation boundary. Vosk or sherpa-onnx can implement
`IStreamingRecognizer` in process. whisper.cpp can be integrated through its C
API or a small app-owned host, but needs a transcript-stability layer because
its rolling-window output is not the current server's delta/commit protocol.
Those compatibility statements are repository-based engineering inferences,
not upstream guarantees.

## Comparison

| Candidate | Verified license facts | Verified platform/language facts | Fit to PrivateType (inference) | Main risk |
|---|---|---|---|---|
| **whisper.cpp + OpenAI Whisper** | whisper.cpp is [MIT](https://github.com/ggml-org/whisper.cpp/blob/master/LICENSE). OpenAI states that Whisper code **and model weights** are [MIT](https://github.com/openai/whisper#license), and the official converted GGML model repository is also [marked MIT](https://huggingface.co/ggerganov/whisper.cpp). | whisper.cpp documents Windows/MSVC, CPU-only inference, a C API, quantization, and models from 75 MiB/~273 MB memory (`tiny`) through 466 MiB/~852 MB (`small`) in its [official README](https://github.com/ggml-org/whisper.cpp#memory-usage). OpenAI's tokenizer lists both [English and Polish](https://github.com/openai/whisper/blob/main/whisper/tokenizer.py#L8-L20), and the multilingual model supports language selection/detection. | One multilingual model can preserve Polish, English, and Automatic. PCM input is compatible. A custom rolling decoder must convert successive hypotheses into provisional/committed updates. Avoid optional FFmpeg/SDL components unless their licenses are separately audited; neither is needed for the app's existing PCM capture. | Upstream calls its realtime tool a ["naive" example](https://github.com/ggml-org/whisper.cpp#real-time-audio-input-example) that samples every 500 ms and retranscribes a rolling window. Latency, CPU cost, repeated/rewritten text, and Polish accuracy are unverified on the target machine. |
| **Vosk + `small-en-us-0.15` + `small-pl-0.22`** | Vosk API is [Apache-2.0](https://github.com/alphacep/vosk-api/blob/master/COPYING). The official model list labels the selected [English model Apache-2.0](https://alphacephei.com/vosk/models) and the selected [Polish model Apache-2.0](https://alphacephei.com/vosk/models). | Vosk officially lists Polish and English, C# bindings, continuous streaming, and small models in its [README](https://github.com/alphacep/vosk-api). The model page gives 40 MB for English and 50 MB for Polish; it reports Polish WER of 18.36/16.88/11.55 on its named test sets. Its C API consumes PCM16 chunks and exposes mutable partial plus final JSON results in [`vosk_api.h`](https://github.com/alphacep/vosk-api/blob/master/src/vosk_api.h). Windows x86/x64 and NuGet are documented by the project's [installation guide](https://github.com/alphacep/vosk-space/blob/master/install.md). | Closest semantic match: map `PartialResult` to provisional updates and endpoint/final results to commits. Load the model chosen by shortcut. The existing idle unload policy can dispose models. | Two models replace one multilingual model. `Automatic` would require a separate language-ID strategy or running recognizers in parallel; neither is verified. Official model data suggests a likely quality regression, and the official page recommends a separate 1.6 GB recasing/punctuation model for English while listing none for Polish. |
| **sherpa-onnx + Whisper ONNX** | sherpa-onnx is [Apache-2.0](https://github.com/k2-fsa/sherpa-onnx/blob/master/LICENSE). Its converted multilingual `small` publisher marks that artifact [Apache-2.0](https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small/commit/421db27fb3bb1850cbaaa1e9c8a5bc500ec6ecb7), while the underlying OpenAI Whisper weights are MIT. Every selected conversion must still be pinned and checked. | The official [C# API](https://k2-fsa.github.io/sherpa/onnx/csharp-api/) supports streaming and non-streaming recognition, provides prebuilt C# libraries, and documents Windows through .NET. Its Whisper implementation is explicitly [non-streaming](https://k2-fsa.github.io/sherpa/onnx/pretrained_models/whisper/). The official online-model index lists English, Chinese, French, Korean and Bengali variants, but [no Polish online model](https://k2-fsa.github.io/sherpa/onnx/pretrained_models/online-transducer/). | Direct C# embedding is attractive and avoids a loopback process. Whisper still needs end-of-utterance or rolling-window adaptation, so it does not remove the hardest product risk versus whisper.cpp. | More native/runtime dependencies and model-artifact provenance to audit, without a verified Polish streaming model. ONNX Runtime is [MIT](https://github.com/microsoft/onnxruntime/blob/main/LICENSE) but ships its own extensive [`ThirdPartyNotices.txt`](https://github.com/microsoft/onnxruntime/blob/main/ThirdPartyNotices.txt). This is not a clear compliance simplification. |

## What MIT licensing would mean in practice

### whisper.cpp route

Verified license condition: both MIT licenses require retaining their copyright
and permission notices in copies or substantial portions. The application
repository can carry its own MIT license, and the ZIP should include a
third-party notices file containing the whisper.cpp and OpenAI Whisper MIT
texts. If the app downloads rather than bundles the model, show the model name,
source, pinned revision/hash, and license before or alongside download.

This is the lowest-complexity option assessed here. It is not "no notices";
it is a short, permissive two-license notice path.

### Vosk route

Verified license condition: Apache-2.0 section 4 permits source/object
redistribution and allows different terms for the distributor's own
modifications or derivative whole, provided the Apache conditions are met. In
practice, keep the PrivateType source MIT and ship the Vosk Apache-2.0 text,
preserve relevant copyright/attribution notices, identify modifications, and
include any upstream NOTICE applicable to the exact binaries. Do the same for
each selected model. The app does not need to be relicensed to Apache-2.0.

### sherpa-onnx route

The same MIT-application/Apache-dependency structure is possible, but the exact
NuGet/native runtime and chosen converted model package still need a binary and
notice audit. Do not assume the runtime repository license automatically
licenses every model published beside it.

## Recommendation and next proof

1. **Do not replace the current engine on license concern alone until the exact
   current NeMo binary footprint review is complete.** A third-party notice that
   mentions a project does not by itself prove how the shipped executable is
   linked or which obligations apply to this exact package.
2. Run one bounded **whisper.cpp tracer spike** using an upstream tagged release,
   a pinned multilingual `base` and `small` quantized model, the existing Polish
   and English fixtures, and live microphone input. Measure first provisional
   text, stop-to-final latency, RTF, peak working set, transcript duplication,
   and WER/text comparison. Exercise explicit `pl`, explicit `en`, and automatic
   detection.
3. Accept whisper.cpp only if it preserves the current hold-to-dictate feel and
   Polish quality. Otherwise keep Nemotron with a corrected notices bundle, or
   test Vosk as a deliberately lighter/lower-quality mode rather than a silent
   replacement.
4. Before release under any route, audit the **exact built binaries and copied
   files**, pin all source/model revisions and hashes, and generate the notice
   bundle from those pinned artifacts.

## Decision matrix

| Goal | Best candidate |
|---|---|
| Simplest permissive license story | whisper.cpp + original OpenAI Whisper |
| Closest true-streaming API and smallest models | Vosk with the two named Apache-2.0 models |
| Best direct .NET API | sherpa-onnx, but not for current Polish streaming requirements |
| Lowest product risk today | Keep the already-validated Nemotron engine until an alternative spike passes |
