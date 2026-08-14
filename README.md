# PrivateType

PrivateType is a private, local Windows tray app for hold-to-dictate speech input. It shows a draggable bubble while you speak, displays a live 44-band voice spectrum, and inserts finalized text into the app that was active when dictation began.

Audio and recognition remain on the computer. The speech engine listens only on `127.0.0.1`; there is no account, cloud-recognition fallback, or retained transcript history.

## Quick start

1. Download and unpack the portable ZIP into a writable folder—not `Program Files`, a read-only archive, or a protected network location.
2. Run `PrivateType.exe`.
3. On first launch, let the app download and verify the local speech model.
4. Open **Settings** from the tray icon or the ready bubble, choose a microphone, and confirm your shortcuts.
5. Hold a shortcut while speaking. Release it to insert the finalized text into the original eligible text field.

Default shortcuts:

| Language | Shortcut |
| --- | --- |
| Polish | `Ctrl+Shift+R` |
| English | `Ctrl+Shift+E` |

## What to expect

- The ready bubble can be dragged. Its position is retained separately for each monitor.
- At launch, the app begins loading the local model so the first dictation is ready sooner. After the selected idle timeout, a held shortcut shows **Loading local model…** until the model is ready; keep holding and it will move to **Listening**.
- The 44 spectrum bars and icon react to microphone input. Their visual gain adapts to quieter speech; this does not alter the audio sent to recognition.
- The transcript view keeps three visible lines and follows the newest text.
- In Settings, **Start PrivateType with Windows** creates a current-user Startup entry. **Unload model after** releases the model's memory after 5, 10, 15, or 30 minutes without dictation.

## Computer requirements

The app is a `win-x64`, CPU-only application. These are practical recommendations, not a guarantee for every computer:

| Resource | Minimum practical guidance | Recommended |
| --- | --- | --- |
| Operating system | Windows 10 or 11, 64-bit | Current Windows 11 build |
| CPU | 64-bit x86 CPU | Modern multi-core CPU |
| RAM | 4 GB total, with at least 2 GB available while dictating | 8 GB total or more |
| Free disk space | 1.2 GB for the app, runtime, model, and working room | 2 GB or more |
| Microphone | Any Windows-recording device | Headset or close microphone in a quiet room |

Measured package facts:

- The pinned Nemotron Q8_0 model is **741,548,352 bytes** (about 707 MiB).
- The recorded self-contained app/runtime folder is about **190 MB** before the model download.
- On the verified test machine, the ready local engine used about **932 MiB** working set. Windows and other active apps require additional headroom.

See [MODEL_ARTIFACT.md](MODEL_ARTIFACT.md) and [ENGINE_DECISION.md](ENGINE_DECISION.md) for the model checksum, license, benchmark context, and measured runtime evidence.

## Privacy and supported targets

- The app does not retain raw audio or inserted transcripts.
- `data/settings.json` stores the selected microphone, shortcuts, bubble position, Windows startup choice, and model idle timeout.
- Diagnostics are a bounded in-memory timeline of safe operational breadcrumbs, warnings, and errors. They disappear when the app closes. Use **Settings → View diagnostics…** to review, copy, save, or clear a report; nothing is written automatically.
- Ordinary desktop text fields are supported. Password/secure fields, elevated applications, remote desktops, games, and apps that reject synthetic input are intentionally unsupported. If the foreground app changes while dictating, insertion is cancelled rather than redirected.

## Troubleshooting

| Symptom | What to do |
| --- | --- |
| First dictation pauses at loading | Keep the shortcut held until the local model becomes ready. Later holds are immediate until the idle timeout unloads it. |
| Text was not inserted | Check that the original target is a normal, non-elevated text field. Review the local diagnostics log for the error phase. |
| The bubble is on another monitor | Drag the ready bubble where you want it on that monitor; the app saves that position. |
| Recognition is weak | Choose the correct microphone in Settings and speak close to it. The visual spectrum is not an audio gain control. |

## Building and testing

For contributors with the local engine runtime available:

```powershell
dotnet test .\tests\PrivateType.Core.Tests\PrivateType.Core.Tests.csproj
dotnet test .\tests\PrivateType.App.Tests\PrivateType.App.Tests.csproj
dotnet run --project .\tests\PrivateType.App.LayoutProbe\PrivateType.App.LayoutProbe.csproj
```

Create and verify a portable release with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-PortableRelease.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Test-PortableRelease.ps1
```

## Agent handoff

Automation and coding agents should read [AGENTS.md](AGENTS.md) before changing this project. In particular, after any behavior or UI change, rebuild, verify, stop only the old `PrivateType.App` process, launch the current executable, confirm it is running, and only then ask the user to test it.
