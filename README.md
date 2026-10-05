# PrivateType

**PrivateType is local, hold-to-dictate speech input for Windows.** Hold a
shortcut, speak, and release: the text is typed into the window you were
using.

Everything runs on your computer. There is no account, no cloud, and no saved
transcript history.

![The PrivateType bubble while dictating](docs/images/recording-bubble.png)

## Get started

1. Download `PrivateType-<version>-win-x64.zip` from
   [Releases](https://github.com/kkolodziejczak/privatetype/releases).
2. Unpack it into a folder you can write to (not `Program Files`).
3. Start `PrivateType.exe`.
4. Choose a speech model and approve its download (about 700 MiB, once).
5. Open **Settings** from the tray icon and pick your microphone.

To update, close PrivateType, unpack the new ZIP, and start the new
`PrivateType.exe`.

<details>
<summary>Verify the download (optional)</summary>

Each release has a `.zip.sha256` file next to the ZIP. The hash must match
exactly:

```powershell
Get-FileHash .\PrivateType-1.3.0-win-x64.zip -Algorithm SHA256
```

Releases are unsigned, so download them only from this repository.
</details>

## Use it

| Shortcut | Language |
| --- | --- |
| `Ctrl+Shift+R` | Polish |
| `Ctrl+Shift+E` | English |

Hold the shortcut while you speak, then release it to insert the text. A short
ping means PrivateType is listening. In **Settings → Shortcuts** you can:

- add more shortcuts, each with one of 32 languages or Automatic,
- switch to **Press to start and stop** if you'd rather not hold the keys,
- insert text by **Typing characters** instead of pasting, for apps like Vim
  or PuTTY where Ctrl+V doesn't work.

When pasting, your clipboard is put back afterwards.

**Missed a dictation?** If you switch windows before the text arrives, nothing
is lost. Press **Win+Shift+V** to see recent dictations and paste one again.
They are kept in memory only, until PrivateType exits by default.

![Settings](docs/images/settings.png)

### Speech models

Pick a model on first launch. You can download the other one, switch, or delete
one later in **Settings → Model**.

| | Nemotron 3.5 Streaming (default) | Parakeet TDT v3 |
| --- | --- | --- |
| While you speak | Words appear live | Preview about once a second |
| Language | From the shortcut | Detected automatically (25 European languages) |
| Vocabulary boosting | Yes | No |
| Download | About 708 MiB | About 681 MiB |

### Vocabulary

If PrivateType keeps getting a name or term wrong, teach it the right spelling.
Choose **Teach from last dictation…** in the bubble menu, click the words it
got wrong, and type the correct spelling. From then on it recognizes the term
and fixes those wrong words.

![Teach from last dictation](docs/images/teach-vocabulary.png)

You can also add phrases yourself in **Settings → Vocabulary**, for one
language or for all of them. You can import and export phrase lists as packs.

## Requirements

- Windows 10 or 11, 64-bit, with an x86-64 CPU. No GPU is needed.
- 8 GB RAM recommended. The loaded model uses about 0.8–1.1 GiB.
- About 1.2 GB of free disk space for the app, one model, and working room.
- The [Microsoft Visual C++ x64 Redistributable](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist),
  if it isn't already installed.

## Privacy

- Audio is never saved. Recognition runs locally.
- Recent dictations stay in memory only and are cleared when PrivateType exits.
  In Settings you can keep them for less time, or not at all.
- Settings and your vocabulary are stored in `app/data/settings.json` in the
  release folder.
- Models are downloaded from NVIDIA's public releases. See
  [MODEL_ARTIFACT.md](MODEL_ARTIFACT.md) for their sources and checksums.

## Troubleshooting

| Problem | What to do |
| --- | --- |
| First dictation shows **Loading local model…** | Keep holding the shortcut until it is ready. |
| Text was not inserted | Password fields, apps running as administrator, remote desktops, and games are not supported. Paste the text from **Win+Shift+V** instead. |
| Recognition is weak | Choose the right microphone in Settings and speak close to it. Add tricky words to Vocabulary. |
| App won't start the speech engine | Install the Microsoft Visual C++ x64 Redistributable and start PrivateType again. |

Still stuck? Open **Settings → Diagnostics…** and
[open an issue](https://github.com/kkolodziejczak/privatetype/issues). Please
don't include dictated text, audio, or settings files.

## License

PrivateType is [MIT licensed](LICENSE). Third-party notices are in
`app/licenses` and in **Settings → Licenses…**.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), and the
roadmap in [TODO.md](TODO.md). Agents should follow [AGENTS.md](AGENTS.md).

```powershell
dotnet test .\tests\PrivateType.Core.Tests\PrivateType.Core.Tests.csproj
dotnet test .\tests\PrivateType.App.Tests\PrivateType.App.Tests.csproj
dotnet run --project .\tests\PrivateType.App.LayoutProbe\PrivateType.App.LayoutProbe.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-PortableRelease.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Test-PortableRelease.ps1
```

The README screenshots come from the layout probe (output in
`%TEMP%\live-dictation-layout-probe`): `status-panel-recording.png`,
`settings.png`, and `teach-one-fix.png`. Pass
`-p:Version=<release>` so the window title shows the release version.
