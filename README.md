# PrivateType

**PrivateType is a local, hold-to-dictate speech-input app for Windows.** It
listens only while you hold a shortcut, shows what it is hearing, and inserts
the final text into the field that was active when dictation began.

Audio and recognition stay on the computer. There is no account, cloud
recognition fallback, or retained transcript history.

![Live spectrum while dictating](docs/images/recording-bubble.png)

## Download and start

1. Download the versioned `PrivateType-<version>-win-x64.zip` and its
   `.sha256` checksum from
   [Releases](https://github.com/kkolodziejczak/privatetype/releases).
2. Verify the checksum, then unpack the ZIP into a writable folder. Do not run
   it from `Program Files`, a read-only archive, or a protected network path.
3. Open the extracted versioned folder and start the clearly visible
   `PrivateType.exe`. Technical runtime files are kept inside `app`.
4. On the first launch, approve the separate local-model download and wait for
   its verification to finish.
5. Open **Settings** from the tray icon or ready bubble, choose a microphone,
   and confirm your shortcuts.

By default, the verified model is stored once in the current Windows user's
`%LOCALAPPDATA%\PrivateType\models\<sha256>` cache and is reused by
cache-aware PrivateType versions. To keep a release self-contained, create an
empty `app\models` directory in the extracted folder before its first launch;
that deliberate directory selects portable-local mode. Existing v1.0.2 or other
release-folder models are never scanned, moved, linked, overwritten, or deleted.
After closing every PrivateType version, model directories under that shared
cache can be deleted to reclaim space; a cache-aware version will download its
pinned model again the next time it needs it.

The first public releases use a direct, unsigned ZIP with manual updates. Use
the SHA-256 file published beside each release and download only from this
repository's Releases page.

To update, close PrivateType, unpack the new ZIP, and start the new
`PrivateType.exe` once. If Windows startup is already enabled, the same or a
newer version automatically becomes the startup copy. Starting an older or
unversioned copy remains supported: PrivateType asks before changing the
registered startup version, so you can deliberately roll back. Disabling
Windows startup removes both current PrivateType and obsolete LiveDictation
entries without touching other startup applications.

```powershell
$version = '1.1.0'
Get-FileHash ".\PrivateType-$version-win-x64.zip" -Algorithm SHA256
```

The reported hash must exactly match the accompanying `.zip.sha256` file.

## Use it

| Language | Default shortcut |
| --- | --- |
| Polish (Poland) | `Ctrl+Shift+R` |
| English (United States) | `Ctrl+Shift+E` |

In **Settings → Shortcuts**, each shortcut can use Automatic or any of 32
languages and regions the local model recognizes, including German, French,
Spanish, Italian, Portuguese, Dutch, Ukrainian, Russian, Czech, Swedish,
Japanese, Korean, and Chinese (Simplified). Add as many shortcuts as you need.
The app's own interface stays in English.

Hold a shortcut while speaking, then release it to insert the final text. The
model loads when PrivateType starts; after the selected idle timeout unloads
it, the next held shortcut loads it again. Keep
holding the shortcut while **Loading local model…** is shown. A short ping
signals that loading has finished and the microphone is ready: you can speak.
The ping plays only when that hold waited for the model to load.

Prefer not to hold? Set **Settings → Shortcuts → Shortcut behavior** to
**Press to start and stop**: press the shortcut once to start listening and
again to insert the text. If the model is still loading, listening starts by
itself when it is ready.

**Insert text by** chooses how text reaches the target app. **Pasting**
(default) inserts the whole text in one step, so pressing Enter right away
cannot send half of it. PrivateType puts your previous clipboard back
afterwards (unless you copied something new meanwhile) and marks the dictated
text so Windows clipboard history and cloud clipboard skip it. Choose **Typing
characters** for apps where Ctrl+V does not paste, such as Vim or PuTTY; it
never touches the clipboard.

### Recent dictations

Long dictations take a moment to finish after you release the shortcut. If you
switch windows in the meantime, the text is not inserted, and the bubble says
so. Nothing is lost: press **Win+Shift+V** (or choose **Recent dictations…** in
the bubble menu) to see your recent dictations, newest first. Dictations that
could not be inserted are marked **Not inserted**. Press Enter or click one to
paste it into the window you were in. **Delete** or **×** removes one entry, and
**Clear all** removes everything. If that window can't take the text, it is
copied instead, so you can paste it with Ctrl+V.

In **Settings → Recent dictations**, choose to keep dictations until
PrivateType exits (default), for 1 hour, for 15 minutes, or not at all. They are
kept in memory only and never saved to disk. With **Don't keep**, Win+Shift+V
goes back to Windows. Tick **Show pasted dictations in Win+V clipboard history**
if you also want them in Windows clipboard history. It is off by default because
Windows keeps those entries until you clear them or restart, and clipboard tools
can read them. Cloud clipboard skips dictated text either way.

### Vocabulary

If the model keeps misspelling a name, acronym, or technical term, add it in
**Settings → Vocabulary** (or **Vocabulary…** in the bubble or tray menu).
Type each word or phrase exactly as it should be written. Phrases under
**Shared across languages** are used with every shortcut; phrases under a
language, such as Polish, are used only with that language's shortcuts.
Automatic uses shared phrases only.

**Strength** applies to all phrases. Normal suits most people; Strong helps
stubborn words more but can also insert them where they were not said. On
PrivateType's recognition tests, vocabulary raised correctly spelled technical
terms from about 1 in 5 to about half. Vocabulary stays in the settings file on
this computer and is only given to the local speech engine.

**Teach from last dictation…** in the bubble menu is the quickest way to add a
phrase. It shows the sentence you just dictated as words: click the words it
got wrong (each one in turn, or just the first and last of a longer phrase),
type the correct spelling, and choose **Add fix**.
Repeat for every other mistake in the sentence, each with its own language if
needed, then save them all at once. **Open vocabulary** then shows the saved
terms in Settings; the language picker there shows how many phrases each
language holds. Each fix saves your typed spelling and the few words it
replaced, so those words are corrected in future dictations. The rest of the
sentence lives only in memory, and is discarded when that window closes, when
you start the next dictation, or when PrivateType exits. The text already typed
into the other app is not changed.

**Fix phrases after dictation** (on by default, under **After dictation** in the
same page) corrects the finished sentence when you release the shortcut, before
it is inserted. The model tends to write what it hears, such as "three D
printing" or "fusion three sixty", and vocabulary alone can't turn those into
"3D printing" or "Fusion 360". This step rewrites them, whether the numbers and
letters are said in English or Polish. It also rewrites every **Heard as → Write
as** pair listed there, which Teach adds for you and you can edit or remove.
Only whole words are replaced, and never across a full stop or comma. With it
on, Normal strength is usually enough.

**Packs** are named phrase lists you can share. **Import pack…** reads a
`.privatetype-vocabulary.json` file (a JSON list of phrases), shows every
phrase, and asks which language it is for before copying it in. Turn packs on
or off, edit, export, or remove them in the same section; **Export…** writes
only the phrases you leave ticked. Each dictation uses at most 200 phrases from
your own list and switched-on packs for its language, and up to 1,000 phrases
can be stored. PrivateType never downloads packs; curated examples may appear in
[vocabulary-packs](vocabulary-packs/) for manual download.

In **Settings → Ready sound**, choose Ping, Chime, Bell, or your own WAV/MP3,
and adjust the volume from 0% (muted) to 100%. **Preview** plays your pending
choice immediately, without loading the model. **Save changes** keeps your
preferences; **Cancel** discards them. Custom files are copied into app storage
when saved, and playback uses at most the first three seconds. If a saved custom
file becomes unreadable, PrivateType uses Ping instead. Settings scrolls when
needed, with Save and Cancel always accessible.

![Settings](docs/images/settings.png)

### What the app does

- When dictation starts, the bubble moves to the monitor under the mouse while
  keeping the same relative screen position.
- The ready bubble is semi-transparent (more faded while the model is unloaded)
  and click-through, so you can see what is behind it. Only the icon is
  draggable; the bubble expands when the model is loading or you are speaking.
- A 44-band voice spectrum and icon react to microphone input; visual gain
  adapts to quieter speech without changing the audio passed to recognition.
- The live transcript keeps three visible lines and follows the newest text.
- **Start PrivateType with Windows** creates a current-user Startup entry.
- **Unload model after** frees model memory after 5, 10, 15, or 30 idle
  minutes.
- Diagnostics retain only safe warnings and errors in memory until the app
  closes. They never include audio or dictated text.

## Requirements

PrivateType is a CPU-only `win-x64` app.

| Resource | Minimum practical guidance | Recommended |
| --- | --- | --- |
| Operating system | Windows 10 or 11, 64-bit | Current Windows 11 build |
| CPU | 64-bit x86 CPU | Modern multi-core CPU |
| RAM | 4 GB total, at least 2 GB free while dictating | 8 GB total or more |
| Free disk space | 1.2 GB for app, runtime, model, and working room | 2 GB or more |
| Microphone | Any Windows recording device | Headset or close microphone in a quiet room |

The pinned Nemotron Q8_0 model is about 708 MiB. The self-contained app/runtime
folder is about 200 MB before the model download. The current native runtime
requires the [Microsoft Visual C++ x64 Redistributable](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)
if it is not already installed.

## Privacy, limits, and troubleshooting

- PrivateType does not retain raw audio. Recent dictations stay in memory only,
  for **Recent dictations** (until the app exits, or for the time you choose in
  Settings, up to 50 entries) and for **Teach from last dictation…**. They are
  never written to disk or diagnostics. Choose **Don't keep** in Settings to
  turn the list off.
- `app/data/settings.json` stores only the selected microphone, shortcuts and
  their languages, shortcut behavior, insertion mode, how long recent
  dictations are kept, the Win+V clipboard history choice, vocabulary (including the
  heard-as wordings you taught or typed), bubble location,
  Windows-startup preference, model idle timeout, and ready-sound choice.
- The model is downloaded separately from NVIDIA; it is not included in the
  app ZIP. See [MODEL_ARTIFACT.md](MODEL_ARTIFACT.md) for its source and
  checksum.
- The shared cache contains only the verified public model and its coordination
  files. Settings remain in the release's `app/data`; audio, transcripts, and
  diagnostics are never copied into the shared cache.
- Removing an old release folder is a manual cleanup choice. New cache-aware
  versions do not migrate or deduplicate its local model automatically, so an
  older v1.0.2 installation can continue to roll back independently.
- Normal desktop text fields are supported. Password/secure fields, elevated
  apps, remote desktops, games, and apps that reject synthetic input are not.
  If the foreground app changes while dictating, insertion is cancelled; paste
  the text from **Recent dictations** (Win+Shift+V) instead.

| Symptom | What to do |
| --- | --- |
| First dictation pauses at loading | Keep holding the shortcut until the local model is ready. |
| Text was not inserted | Check the original target is a normal, non-elevated text field, then open **Settings → View diagnostics…**. |
| Bubble is on another monitor | Drag the ready bubble where you want it on that monitor. |
| Recognition is weak | Select the right microphone and speak close to it. The spectrum is not an audio-gain control. |
| Engine will not start | Install the current Microsoft Visual C++ x64 Redistributable, then relaunch the app. |

## Licenses

PrivateType's own source and maintainer-owned assets are available under the
[MIT License](LICENSE). The portable release includes complete notices in
`app/licenses`, including `THIRD-PARTY-NOTICES.txt` for NeMo-Speech.cpp, ggml, cpp-httplib,
SentencePiece, Protobuf, Abseil, utf8-range, NAudio, and the self-contained
.NET runtime. Open the same notices from **Settings → Open-source licenses…**.

## Report a problem or contribute

Please use [GitHub Issues](https://github.com/kkolodziejczak/privatetype/issues)
for reproducible bugs and feature ideas. Do not include dictated text, raw
audio, settings files, or diagnostic reports without first reviewing them.

Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting a change and
[SECURITY.md](SECURITY.md) for vulnerability reporting. The future technical
roadmap is in [TODO.md](TODO.md).

## Build from source

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

Agents working in this repository should follow [AGENTS.md](AGENTS.md).
