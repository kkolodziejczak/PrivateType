# PrivateType portable release

## What the ZIP contains

`Build-PortableRelease.ps1` creates a versioned `win-x64` application folder and ZIP. The extracted folder keeps a prominent native `PrivateType.exe` launcher and a short start-here document at the top level. The self-contained .NET application, local NeMo-Speech CPU runtime, data, models, and notices live under `app`. The ZIP deliberately excludes the speech model.

The model is downloaded on first run into `app/models/` beside the real application executable. Once that download is verified, later launches use that local file; the application does not have a cloud-recognition fallback.

## Install and use

1. Unpack the ZIP into a writable folder. Do not run it from a read-only archive, Program Files, or a protected network location.
2. Launch `PrivateType.exe` and allow the initial local-model download to finish. The application verifies the file before activating it.
3. Open **Settings** from the tray or ready bubble to select a microphone and shortcuts.
4. Hold a configured shortcut to dictate, then release it to insert only finalized text into the original eligible target.

The complete versioned folder is portable. After the initial verified model download, move the whole folder, including `app/models/` and `app/data/`, to retain the downloaded model and settings.

## Privacy and supported targets

- Audio and recognition stay on the computer. The recognizer is a child process bound to `127.0.0.1`; there is no account, telemetry, transcript history, or cloud fallback.
- The application does not retain audio or inserted transcripts. Its portable `app/data/settings.json` stores only the selected microphone, shortcut bindings, and bubble position.
- Unicode insertion is supported for ordinary desktop text fields. Elevated applications, secure/password fields, remote desktops, games, and applications that reject synthetic input are intentionally unsupported. The application cancels instead of redirecting text after a foreground-target change.

## Model and runtime notices

- Model: `nvidia/nemotron-3.5-asr-streaming-0.6b`, Q8_0 GGUF, 741,548,352 bytes; see [MODEL_ARTIFACT.md](MODEL_ARTIFACT.md).
- Model license: OpenMDW-1.1. The model is downloaded by the user and is not redistributed in the ZIP.
- Runtime: NeMo-Speech.cpp, Apache-2.0. See [ENGINE_DECISION.md](ENGINE_DECISION.md) for the pinned engine/model decision and measurements.

## Release verification

Build from a checkout containing the verified local engine runtime:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-PortableRelease.ps1 -Version 1.0.2
```

Verify a freshly created archive without overwriting an existing directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Test-PortableRelease.ps1 -ExpectedVersion 1.0.2
```

The smoke script validates the top-level launcher, embedded application version, clean unpack, required engine files, absence of a bundled model, and whole-folder relocation. It does not download the model or launch the tray application. Before a release handoff, manually complete first-run download, offline second launch, relocation launch, Notepad insertion, browser-textarea insertion, focus-change cancellation, and hotkey cleanup using a disposable writable folder.

Pushing a version tag such as `v1.0.2` requires a matching `.github/release-notes/v1.0.2.md`, builds `PrivateType-1.0.2-win-x64.zip` and its checksum, and creates a GitHub draft release from those polished notes. Download and test those exact draft assets on a clean supported Windows installation before publishing the draft. Do not rebuild or replace an accepted ZIP between acceptance and publication.

## Recorded package evidence

On 2026-08-13, the release script produced a 189,893,362-byte application folder and a 76,258,595-byte ZIP. The excluded model is 741,548,352 bytes. Re-record these values whenever the runtime or publish output changes.
