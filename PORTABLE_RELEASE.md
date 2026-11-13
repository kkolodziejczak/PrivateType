# PrivateType portable release

## What the ZIP contains

`Build-PortableRelease.ps1` creates a versioned `win-x64` application folder and ZIP. The extracted folder keeps a prominent native `PrivateType.exe` launcher and a short start-here document at the top level. The self-contained .NET application, NVIDIA's NeMo-Speech runtime (CPU and Vulkan), portable data, and notices live under `app`. The ZIP deliberately excludes the speech model.

By default, the model is downloaded on first run into the current Windows user's
`%LOCALAPPDATA%\PrivateType\models\<lowercase-sha256>` shared cache. Once
verified, cache-aware PrivateType versions reuse that artifact without another
download. The application does not have a cloud-recognition fallback.

## Install and use

1. Unpack the ZIP into a writable folder. Do not run it from a read-only archive, Program Files, or a protected network location.
2. For a self-contained release, create an empty `app\models` directory before
   first launch. This explicit directory selects portable-local mode; otherwise
   the shared per-user cache is used. Launch `PrivateType.exe` and allow the
   initial local-model download to finish. The application verifies the file
   before activating it.
3. Open **Settings** from the tray or ready bubble to select a microphone and shortcuts.
4. Hold a configured shortcut to dictate, then release it to insert only finalized text into the original eligible target.

In portable-local mode, the complete versioned folder is portable: after the
initial verified model download, move the whole folder, including `app/models/`
and `app/data/`, to retain the downloaded model and settings. In the default
shared mode, settings live in `%LOCALAPPDATA%\PrivateType\settings.json` and
the model in the per-user cache, so every copy shares them and the folder can
be moved freely. A new copy with no settings offers to import them from an
earlier copy it finds, only with your consent. Earlier folders stay in place as
independent rollback copies until you remove them manually.

After closing every PrivateType version, shared model directories can be removed
from `%LOCALAPPDATA%\PrivateType\models` to reclaim space. A cache-aware version
downloads its pinned model again when needed.

## Privacy and supported targets

- Audio and recognition stay on the computer. The recognizer is a child process bound to `127.0.0.1`; there is no account, telemetry, or cloud fallback.
- The application never writes audio or dictated text to disk. Recent dictations stay in memory only and can be turned off in Settings. Its settings file (`%LOCALAPPDATA%\PrivateType\settings.json`, or `app/data/settings.json` for a portable copy) stores preferences and vocabulary, never audio or dictated text.
- Unicode insertion is supported for ordinary desktop text fields. Elevated applications, secure/password fields, remote desktops, games, and applications that reject synthetic input are intentionally unsupported. The application cancels instead of redirecting text after a foreground-target change.

## Model and runtime notices

- Models: `nvidia/parakeet-tdt-0.6b-v3` (recommended for new installs), Q8_0 GGUF, 713,975,456 bytes, and `nvidia/nemotron-3.5-asr-streaming-0.6b`, Q8_0 GGUF, 742,090,464 bytes; see [MODEL_ARTIFACT.md](MODEL_ARTIFACT.md).
- Model licenses: OpenMDW-1.1 (Nemotron) and CC-BY-4.0 (Parakeet). Models are downloaded by the user and are not redistributed in the ZIP.
- Runtime: NVIDIA's unmodified NeMo-Speech.cpp 0.2.0 Windows release, Apache-2.0, with the licenses NVIDIA ships for its bundled components. See [ENGINE_DECISION.md](ENGINE_DECISION.md) for the pinned runtime, device choice, and measurements.
- Vulkan loader: LunarG's unmodified `vulkan-1.dll` 1.4.328.1, MIT and Apache-2.0, beside the engine. The engine cannot start without it, even on the processor, and NVIDIA's release leaves it out.

## Release verification

Download and verify the pinned runtime, then build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Get-NemoSpeechRuntime.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-PortableRelease.ps1 -Version 1.0.6
```

Verify a freshly created archive without overwriting an existing directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Test-PortableRelease.ps1 -ExpectedVersion 1.0.6
```

The smoke script validates the top-level launcher, embedded application version, clean unpack, required engine files, absence of a bundled model, whole-folder relocation, and that the packaged engine starts and lists the processor. It does not download the model or launch the tray application. Before a release handoff, manually complete first-run download, offline second launch, relocation launch, Notepad insertion, browser-textarea insertion, focus-change cancellation, and hotkey cleanup using a disposable writable folder.

Pushing a version tag such as `v1.0.6` requires a matching `.github/release-notes/v1.0.6.md`, builds `PrivateType-1.0.6-win-x64.zip` and its checksum, and creates a GitHub draft release from those polished notes. Download and test those exact draft assets on a clean supported Windows installation before publishing the draft. Do not rebuild or replace an accepted ZIP between acceptance and publication.

## Signed releases

`.github/workflows/release.yml` builds the folder unzipped, signs both `PrivateType.exe` files (the launcher and `app\PrivateType.exe`) through the shared [gh-release-signing](https://github.com/kkolodziejczak/gh-release-signing) workflow, and only then zips, hashes and drafts the release. `Test-PortableRelease.ps1 -ExpectedSignerThumbprint` fails unless both carry a valid, timestamped signature from that certificate. NVIDIA's engine files, the Vulkan loader and the .NET runtime keep their publishers' signatures, or none, so they stay unmodified.

After pushing the tag, run this in the repository folder and keep the window open:

```powershell
gh release-signing approve -Tag v1.0.6
```

It asks you to approve the `code-signing` environment, then for the SimplySign phone code. The environment only accepts `v*.*.*` tags. A manual run of the workflow from a branch skips signing and builds an unsigned test ZIP without a release. If the code expires, run `gh run rerun <run-id> --failed`, then `gh release-signing approve -RunId <run-id> -Tag v1.0.6`.

Release notes for a signed version should say, under **Download and start**, that `PrivateType.exe` is signed by **Open Source Developer Krzysztof Kołodziejczak** (Certum), which users can check under the file's **Properties → Digital Signatures**. Windows SmartScreen may still show "Windows protected your PC" while the new certificate builds reputation; **More info** then shows that publisher, and **Run anyway** starts the app.

## Recorded package evidence

On 2026-11-02, a manual run of the release workflow produced a 242,530,596-byte application folder and a 96,739,709-byte ZIP from the 1.5.2 sources (.NET 10). The excluded models are 742,090,464 bytes (Nemotron) and 713,975,456 bytes (Parakeet). Re-record these values whenever the runtime or publish output changes.
