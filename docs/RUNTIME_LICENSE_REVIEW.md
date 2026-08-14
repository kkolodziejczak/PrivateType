# Runtime license review for a public portable release

Research date: 2026-08-14
Scope: the `win-x64` portable ZIP produced by the current builder, the native
NeMo-Speech.cpp engine it is designed to copy, the managed application
dependencies, and the self-contained .NET runtime. No final release ZIP was
present in the workspace, so final-artifact verification remains outstanding.
Status: engineering and license-text review, **not legal advice**.

## Executive decision

No KenLM or flashlight-text material was identified in the exact audited CPU
build. The local CMake cache sets `NEMO_SPEECH_WITH_FLASHLIGHT=OFF`; the
generated link graph for `nemo_speech_asr.dll` contains SentencePiece,
Protobuf, ggml, Abseil, and utf8-range inputs, but no KenLM or
flashlight-text library or object. The compiled `flashlight_decoder.cpp`
translation unit is NVIDIA Apache-2.0 code whose KenLM-dependent body is
excluded by `#ifdef NEMO_SPEECH_WITH_FLASHLIGHT` in this configuration. This
evidence removes KenLM from the engineering inventory for this exact build;
the legal conclusion should remain tied to the final-artifact audit.

That does **not** make a ZIP from the current builder ready to publish. The
release script does not explicitly copy license or notice material. It also
verifies only the presence of the local engine files, not the audited upstream
revision, CMake configuration, or binary hashes. A later local rebuild could silently change
the legal footprint.

The PrivateType source may be licensed under MIT while it uses these
dependencies. MIT does not replace the dependencies' own licenses: the source
repository and every binary release must clearly preserve the third-party
terms and notices. Giving the application away for free does not waive those
redistribution conditions.

## What was audited

The portable builder's explicit native payload is the following eight files
from `.engine/build-cpu-realtime-manual/bin`:

- `nemo-speech.exe`
- `nemo_speech_asr.dll`
- `nemo_speech_asr_c.dll`
- `ggml.dll`
- `ggml-base.dll`
- `ggml-cpu.dll`
- `abseil_dll.dll`
- `libprotobuf.dll`

This list comes from the tracked
[`Build-PortableRelease.ps1`](../Build-PortableRelease.ps1), which also runs a
self-contained `win-x64` .NET publish and deliberately excludes the model.
The managed application directly references NAudio 2.2.1 in
[`PrivateType.App.csproj`](../src/PrivateType.App/PrivateType.App.csproj).

The audited local NeMo-Speech.cpp checkout was commit
[`1118951337094db3b362fbf1b27e871696f10590`](https://github.com/NVIDIA/NeMo-Speech.cpp/tree/1118951337094db3b362fbf1b27e871696f10590).
The local cache recorded this relevant configuration:

```text
NEMO_SPEECH_BUILD_ASR=ON
NEMO_SPEECH_BUILD_HTTP=ON
NEMO_SPEECH_BUILD_DIAR=OFF
NEMO_SPEECH_BUILD_GRPC=OFF
NEMO_SPEECH_BUILD_NMT=OFF
NEMO_SPEECH_BUILD_TTS=OFF
NEMO_SPEECH_HTTP_TLS=OFF
NEMO_SPEECH_WITH_FLASHLIGHT=OFF
NEMO_SPEECH_WITH_NORM=OFF
GGML_CUDA=OFF
GGML_OPENMP=ON
```

The local vcpkg packages used by that native build were SentencePiece 0.2.1,
Protobuf 6.33.4, Abseil 20260107.1, and utf8-range 6.33.4. The PE import table
was also inspected with `dumpbin /dependents` for all eight native files.

## Decision table

| Component or concern | Proven in current payload? | Governing terms and concrete action |
|---|---:|---|
| PrivateType original source and assets | Yes | The maintainer may place code and assets they own under MIT. Add an application `LICENSE`; do not imply that bundled third-party files are relicensed. Confirm ownership of icons, screenshots, and GIFs separately. |
| NeMo-Speech.cpp CLI and ASR libraries | Yes | NVIDIA-authored code is Apache-2.0. Apache section 4 requires a copy of the license; preservation of relevant notices; readable NOTICE attribution when the work has a NOTICE; and prominent notices on modified source files. Preserve NVIDIA's [`LICENSE`](https://github.com/NVIDIA/NeMo-Speech.cpp/blob/1118951337094db3b362fbf1b27e871696f10590/LICENSE), [`NOTICE`](https://github.com/NVIDIA/NeMo-Speech.cpp/blob/1118951337094db3b362fbf1b27e871696f10590/NOTICE), and the tracked Windows patch/revision disclosure. |
| ggml DLLs and linked ggml code | Yes | MIT. Include the ggml copyright and complete MIT permission/disclaimer text. The audited upstream revision is [`c03b4e2bcece5134827881af90242086daf75be5`](https://github.com/ggml-org/ggml/tree/c03b4e2bcece5134827881af90242086daf75be5). |
| cpp-httplib | Yes, compiled into the HTTP-enabled CLI | MIT. Include yhirose's complete MIT notice. TLS is off, and the import graph contains no OpenSSL DLL. Audited revision: [`62d899feac3cf9215a55f2b43da250fdd98d2156`](https://github.com/yhirose/cpp-httplib/tree/62d899feac3cf9215a55f2b43da250fdd98d2156). |
| SentencePiece 0.2.1 | Yes, statically linked into `nemo_speech_asr.dll` | Apache-2.0. Include its Apache license and retain relevant notices. Primary source: [SentencePiece v0.2.1](https://github.com/google/sentencepiece/tree/v0.2.1). |
| Protobuf 6.33.4 | Yes, `libprotobuf.dll` plus an import library at native link time | BSD 3-Clause. Binary redistribution must reproduce the copyright, conditions, and disclaimer in documentation or other release materials. Primary source: [Protocol Buffers](https://github.com/protocolbuffers/protobuf). Use the exact license file from the locked vcpkg package in the generated release bundle. |
| Abseil 20260107.1 | Yes, `abseil_dll.dll` plus statically linked Abseil libraries | Apache-2.0. Include its license and relevant notices. Primary source: [Abseil tag 20260107.1](https://github.com/abseil/abseil-cpp/tree/20260107.1). |
| utf8-range 6.33.4 | Yes, `utf8_validity.lib` is statically linked | MIT. Include its Yibo Cai/Google copyright and complete MIT notice. Primary source: [protobuf/utf8_range](https://github.com/protocolbuffers/utf8_range). |
| KenLM | **Not identified** | The current cache has Flashlight off and the ASR link line has no KenLM input or import. This exact build provides no engineering evidence of distributed KenLM material. Keep a release assertion that fails if Flashlight is later enabled or KenLM appears in the link graph/imports, and tie the final legal conclusion to that exact release artifact. |
| flashlight-text | **Not identified** | It is added only inside NeMo's `if(NEMO_SPEECH_WITH_FLASHLIGHT)` block, and no flashlight-text link input was found. Re-audit if that flag or the final link graph changes. |
| OpenSSL | **No** | HTTP TLS and cpp-httplib OpenSSL use are off; no OpenSSL import was found. Re-audit if TLS is enabled. |
| NAudio 2.2.1 and its split assemblies | Yes | MIT, Copyright 2020 Mark Heath. The app may remain MIT, including for commercial use, but the ZIP must retain the full NAudio MIT notice. There is no source-disclosure or copyleft duty. Primary sources: [v2.2.1 license](https://raw.githubusercontent.com/naudio/NAudio/v2.2.1/license.txt) and [NuGet package metadata](https://www.nuget.org/packages/NAudio/2.2.1). The restored closure contains `NAudio`, `.Asio`, `.Core`, `.Midi`, `.Wasapi`, `.WinForms`, and `.WinMM`; inventory the final publish rather than relying only on the direct package reference. |
| Other restored managed packages | Conditional on final publish output | `project.assets.json` also resolves Microsoft.Win32.Registry 4.7.0, System.Security.AccessControl 4.7.0, and System.Security.Principal.Windows 4.7.0 through the NAudio graph. They were not proven as separate runtime files in a final self-contained ZIP. Inventory the actual publish output and include any package-specific terms that remain present. |
| Self-contained Windows .NET runtime | Yes under the current publish command | This is not accurately described as only MIT. Microsoft's [.NET license information](https://github.com/dotnet/core/blob/main/license-information.md) says Windows runtime/product distributions use the [.NET Library License](https://dotnet.microsoft.com/en-us/dotnet_library_license.htm), while source and packages have their stated licenses. That license permits object-code distribution as part of an application but contains distribution requirements, including end-user terms and third-party notices. Bundle the authoritative license and third-party notices for the exact runtime pack used. A qualified reviewer should determine whether and what mechanism a direct ZIP needs to make the required end-user terms effective. |
| Microsoft C/C++ and OpenMP runtime | Required externally by current PE imports; not copied by the script | `ggml-cpu.dll` imports `VCOMP140.dll`; the native files also import `MSVCP140.dll`, `VCRUNTIME140.dll`, and `VCRUNTIME140_1.dll`. A clean machine therefore needs the matching/newer VC++ v14 runtime unless those allowed redistributables are included app-local. Microsoft recommends the current central VC Redistributable and says redistribution is limited to licensed Visual Studio users under its terms: [deployment guidance](https://learn.microsoft.com/en-us/cpp/windows/deployment-in-visual-cpp) and [supported downloads](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). Resolve this before calling the ZIP portable. |
| Nemotron model | Not in the ZIP | The application downloads it from NVIDIA/Hugging Face after launch. That avoids distributing the model file in the GitHub release, but it does not relicense the model or erase the user's separate OpenMDW terms. Model licensing is tracked separately in [`MODEL_ARTIFACT.md`](../MODEL_ARTIFACT.md) and [`PUBLIC_RELEASE_OBLIGATIONS.md`](PUBLIC_RELEASE_OBLIGATIONS.md). Re-audit before bundling or mirroring it. |

## Why KenLM is not in this build

NeMo's top-level CMake declares
`NEMO_SPEECH_WITH_FLASHLIGHT` with a default of `OFF`. Its KenLM runtime and
flashlight-text subdirectory are added only inside the corresponding
conditional; the ASR target defines the macro and links those libraries only
inside another matching conditional. See the pinned upstream
[`CMakeLists.txt`](https://github.com/NVIDIA/NeMo-Speech.cpp/blob/1118951337094db3b362fbf1b27e871696f10590/CMakeLists.txt)
and
[`src/asr/CMakeLists.txt`](https://github.com/NVIDIA/NeMo-Speech.cpp/blob/1118951337094db3b362fbf1b27e871696f10590/src/asr/CMakeLists.txt).

The current tracked [`Build-NemoSpeechCpu.ps1`](../Build-NemoSpeechCpu.ps1)
does not enable Flashlight. The generated cache confirms it is off. The
generated `nemo_speech_asr.dll` link line contains no `kenlm.lib`, no KenLM
object, and no flashlight-text library. Its direct PE dependencies likewise
contain no KenLM DLL.

If a future build enables Flashlight, KenLM becomes a different release
decision. KenLM's own pinned
[`LICENSE`](https://github.com/kpu/kenlm/blob/4cb443e60b7bf2c0ddf3c745378f76cb59e254e5/LICENSE)
says most code is LGPL-2.1-or-later, with specified file-level exceptions.
NeMo's conditional build deliberately creates KenLM as a shared library to
keep it replaceable. At that point the release would need a fresh exact-source
audit and legal review; do not reuse this `No` decision.

## Can the repository be MIT?

Yes, as a licensing architecture:

1. Put the maintainer-owned PrivateType code and assets under an MIT
   `LICENSE`.
2. State clearly that third-party software remains under its own terms.
3. Keep `THIRD-PARTY-NOTICES.txt` and a `licenses/` tree in both the public
   source repository and each release ZIP.
4. Do not paste the app's MIT header into third-party files or describe the
   entire binary bundle as MIT-only.
5. Keep source availability separate from binary terms: MIT covers the app;
   Apache, BSD, third-party MIT notices, the .NET Library License, and any
   Microsoft redistributable terms continue to cover their respective files.

Charging versus giving the ZIP away does not change these conditions. The
licenses grant commercial and noncommercial redistribution on their stated
terms; the trigger is distribution, not payment.

## Concrete packaging options

### Option A -- self-contained portable ZIP, current engine configuration

This best matches the requested product, and it does **not** require replacing
the engine to avoid KenLM.

Before release:

- pin and verify the NeMo commit, CMake feature values, vcpkg versions, NuGet
  closure, .NET SDK/runtime packs, and final binary hashes;
- fail the release if Flashlight/KenLM, OpenSSL, CUDA, NMT, TTS, gRPC, or an
  unreviewed native dependency appears;
- add the app MIT license;
- generate a complete `THIRD-PARTY-NOTICES.txt` plus exact component license
  files for NeMo, ggml, cpp-httplib, SentencePiece, Protobuf, Abseil,
  utf8-range, NAudio, and the exact .NET runtime;
- include NVIDIA's NOTICE and disclose the Windows SentencePiece linker patch;
- decide how to satisfy the Windows .NET Library License's end-user-terms
  requirement for a no-installer ZIP;
- either deploy permitted VC++/OpenMP redistributables app-local or document
  and test the official VC++ x64 Redistributable prerequisite; and
- run the final dependency audit against the exact ZIP, not the development
  build directory.

### Option B -- framework-dependent ZIP

Publish without the .NET runtime and require a supported .NET Desktop Runtime
plus the VC++ x64 Redistributable. This reduces the binary notice and Microsoft
redistribution surface, but it is no longer a self-contained portable
experience. NeMo, ggml, cpp-httplib, native vcpkg, and NAudio notices still
remain mandatory.

### Option C -- replace the native engine

This is **not justified by the current KenLM concern**, because no KenLM
material was identified in the audited build. A replacement may
still be appropriate for model-license, maintenance, performance, or support
reasons, but any MIT/Apache alternative will still require attribution and its
model weights require an independent license review. Replacing a working
engine only to remove the current LGPL concern would solve a dependency the
audited build does not contain.

## Required release controls

The current `Build-PortableRelease.ps1` checks only whether the eight engine
filenames exist. Add automated release checks that produce and validate an
SBOM-like manifest with at least:

- source repository URL and exact commit for every source-built native
  component;
- CMake feature flags, vcpkg and NuGet package versions;
- filename, size, SHA-256, PE imports, and signature status for every EXE/DLL;
- a denylist assertion for KenLM/flashlight, OpenSSL, CUDA, TTS, NMT, and gRPC
  unless separately approved;
- an allowlist assertion matching the expected native link/import graph;
- the exact .NET runtime pack and authoritative license/third-party notice
  files used by that publish; and
- a test that every inventory row maps to a readable file in the release's
  `licenses/` directory.

Also remove any unused engine binary after a runtime test. In the current PE
import graph, `nemo-speech.exe` imports `nemo_speech_asr.dll` but not
`nemo_speech_asr_c.dll`; the latter appears unnecessary for the app's CLI
execution path. Treat removal as a tested packaging optimization, not as an
assumption.

## Items still requiring qualified legal review

- The exact end-user terms/mechanism needed when redistributing the Windows
  self-contained .NET runtime in a direct ZIP.
- The maintainer's entitlement and chosen method for redistributing Visual
  C++/OpenMP runtime files, if they are bundled rather than made prerequisites.
- Rights in all original/non-code assets when the standalone repository is
  placed under MIT.
- OpenMDW implications for the application's automatic model download and
  presentation of model terms, even though the model is not in the ZIP.
- Any future release where the audited source revision, build flags, package
  closure, or binary hashes differ.

Those are narrow questions. The engineering evidence for the current audited
binary set consistently identifies no KenLM or flashlight-text material; the
same checks must be repeated against the final ZIP before publication.
