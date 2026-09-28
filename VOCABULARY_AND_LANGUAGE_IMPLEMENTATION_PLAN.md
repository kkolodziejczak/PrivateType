# Vocabulary and Expanded Languages Implementation Plan

## Purpose

Add a private, user-owned vocabulary that biases the pinned local Nemotron recognizer toward desired words and phrases, expand explicit recognition from Polish/English to every locale the pinned model can transcribe without fine-tuning, support simple offline vocabulary-pack sharing, and provide an optional quick-teach workflow for the immediately preceding dictation.

The feature must preserve PrivateType's defining behavior: hold-to-dictate, local processing, no cloud fallback, no retained transcript history, no transcript rewriting, and no model load unless recognition proof specifically requires it.

## Agreed decisions

> **Revised 2026-09-28 (Stage 3):** Stage 1 proved the pinned engine applies one boost per request (the maximum across contexts). By the owner's decision, influence is now a single vocabulary-wide **strength** (Low/Normal/Strong, default Normal) instead of a per-entry value. Entries store only phrase and scope. Items below that mention per-entry influence or pack `weight` are superseded for Stage 3 and must be redesigned before Stage 4.

- [x] Maintain one personal vocabulary plus separately named, enableable installed packs; do not turn personal vocabulary into named profiles.
- [x] Vocabulary has `Shared` and base-language scopes.
- [x] A base-language scope applies to every regional locale for that language.
- [x] Explicit recognition uses Shared plus the selected locale's base-language vocabulary.
- [x] Automatic recognition uses Shared vocabulary only.
- [x] Expose Automatic plus the model's 32 out-of-box locales; hide the eight adaptation-only locales.
- [x] A vocabulary entry stores only the desired spelling or phrase, never a misheard-to-corrected replacement pair.
- [x] Each entry has user-selected `Low`, `Normal`, or `Strong` influence; new entries default to Normal.
- [x] Vocabulary is editable in Settings and reachable directly from the bubble menu.
- [x] `Teach from last dictation…` is an on-demand bubble action, not an automatic post-dictation interruption.
- [x] The teach view displays the one ephemeral sentence as selectable word chips and supports one word or a contiguous range.
- [x] The teach dialog preselects the dictation's base language, or Shared after Automatic, while allowing the user to change the scope.
- [x] Teaching changes future recognition only; it never rewrites or copies over already injected text.
- [x] The last transcript exists only in memory until the next dictation begins, the teach dialog is saved/dismissed, or the app exits.
- [x] Personal vocabulary and quick teach persist only the desired entry, its scope, and influence; never persist the misheard wording.
- [x] Import and export packs manually; PrivateType never downloads, discovers, or synchronizes packs.
- [x] A share file is a top-level JSON array of `{ "phrase", "weight" }` entries. `weight` is optional and defaults to `normal`.
- [x] Share files contain no pack ID, version, author, attribution, license, provenance, locale, or update metadata.
- [x] The filename supplies the proposed pack name; the user chooses one Shared/base-language scope during import.
- [x] Imported packs are separately enabled, disabled, edited, exported, renamed, and removed. Editing changes only the installed local copy.
- [x] Export from personal vocabulary includes only entries explicitly selected by the user from the currently visible scope and shows a final preview.
- [x] Personal vocabulary wins over an identical phrase from packs; otherwise the strongest enabled-pack influence wins.
- [x] Curated example pack files may live in a dedicated repository directory with CI validation, but they are downloads only and are not bundled into releases.
- [x] Preserve Polish and English default shortcuts and migrate existing settings without loss.
- [x] Keep the application UI in English; UI localization is separate work.
- [x] Prove and calibrate word boosting before building the production UI.

## External capability facts

The implementation is pinned to the repository's current artifacts:

- Model: `nvidia/nemotron-3.5-asr-streaming-0.6b`, revision and checksum in [MODEL_ARTIFACT.md](MODEL_ARTIFACT.md).
- Runtime: NeMo-Speech.cpp commit `1118951337094db3b362fbf1b27e871696f10590`, recorded in [ENGINE_DECISION.md](ENGINE_DECISION.md).
- The pinned realtime protocol accepts `speech_contexts: [{ phrases: [...], boost: N }]` and a `prompt` compatibility field. Use `speech_contexts`; do not use `prompt` for this feature. See NVIDIA's [pinned HTTP API reference](https://github.com/NVIDIA/NeMo-Speech.cpp/blob/1118951337094db3b362fbf1b27e871696f10590/docs/api.md#websocket-v1realtime).
- NVIDIA documents 19 transcription-ready and 13 broad-coverage locales that transcribe out of the box, plus eight adaptation-only locales that require fine-tuning. See the [model language table](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b#supported-languages).
- NVIDIA does not document a universally safe boost range for this exact local runtime/model pair. Stage 1 is therefore a required calibration spike, not optional research.

## Current state

| Concern | Current owner | Current behavior / constraint |
|---|---|---|
| Recognition language | [DictationContracts.cs](src/PrivateType.Core/DictationContracts.cs) | Closed `RecognitionLanguage` enum with Polish, English, and Auto. |
| Shortcut persistence | [PortableSettings.cs](src/PrivateType.Core/PortableSettings.cs) | `ShortcutBinding` serializes the enum numerically; validation rejects all other values. |
| Language UI | [SettingsWindow.xaml.cs](src/PrivateType.App/SettingsWindow.xaml.cs) | Hard-coded list of three choices. |
| Runtime mapping | [RealtimeRecognizer.cs](src/PrivateType.App/RealtimeRecognizer.cs) | Hard-coded enum-to-`pl-PL`/`en-US`/`auto`; sends sample rate, language, and punctuation only. |
| Session lifecycle | [DictationSession.cs](src/PrivateType.Core/DictationSession.cs) | Final text is taken once, injected, and discarded; there is no completion event carrying the final transcript. |
| Composition root | [DictationApplication.cs](src/PrivateType.App/DictationApplication.cs) | Creates sessions, owns Settings and bubble events, and suspends hotkeys around modal Settings. |
| Bubble actions | [DictationBubble.xaml](src/PrivateType.App/DictationBubble.xaml) | Settings and Quit only. |
| Settings layout | [SettingsWindow.xaml](src/PrivateType.App/SettingsWindow.xaml) | Fixed-width, auto-height, single-page form; unsuitable for an unbounded vocabulary list. |
| Vocabulary sharing | None | No pack parser, installed-pack state, file picker flow, export writer, repository directory, or validation command exists. |
| Privacy statement | [README.md](README.md) | No retained transcript history; settings currently described as containing no vocabulary. |
| UI proof | [PrivateType.App.LayoutProbe](tests/PrivateType.App.LayoutProbe) | Renders current setup, bubble, and Settings states; must be extended for every new state. |

The repository is clean at planning time. This plan and [the annotated UI mock](docs/VOCABULARY_UI_MOCK.md) are the only planning changes.

## Intent model

### Actors

- **Dictating user:** selects an exact recognition locale through a shortcut and optionally maintains vocabulary.
- **Settings editor:** validates and atomically persists shortcuts and vocabulary.
- **Vocabulary composer:** chooses the entries applicable to one recognition request and resolves duplicates.
- **Vocabulary-pack importer/exporter:** previews and validates bounded local JSON arrays without network access.
- **Realtime recognizer:** serializes calibrated phrase groups into the pinned WebSocket protocol.
- **Dictation session:** recognizes, injects, and announces one finalized transcript without retaining history.
- **Ephemeral transcript buffer:** owns the single last teachable transcript and its destruction rules.
- **Teach dialog:** converts a selected word range into a desired vocabulary entry; it never edits the target application.

### Inputs

- Exact locale code selected by a shortcut, such as `pl-PL`, `en-US`, `es-ES`, or `auto`.
- Locally stored personal and enabled-pack entries: desired phrase, `shared` or base-language scope, and influence.
- A manually selected local `.privatetype-vocabulary.json` file containing only a bounded entry array.
- PCM16 microphone audio for the active held shortcut.
- One finalized transcript held temporarily for quick teaching.

### Outputs

- A realtime `session.update` containing the exact locale and zero to three calibrated `speech_contexts` groups.
- Final text injected into the originally captured eligible target.
- Versioned local settings containing stable locale codes and vocabulary entries.
- Installed pack collections stored locally, plus explicitly exported JSON arrays containing only reviewed phrases and weights.
- An optional new vocabulary entry derived from a transient selection.

### State and ownership

| State | Canonical owner | Lifetime |
|---|---|---|
| Supported locale catalog | Core language catalog | Static for the pinned model/release. |
| Shortcut locale codes | Portable settings | Persistent, local, atomic save. |
| Vocabulary entries | Portable settings | Persistent, local, atomic save. |
| Installed pack name, scope, enabled state, and entries | Portable settings | Persistent, local, atomic save; no link to the source file. |
| Calibrated influence mapping | One recognizer configuration owner | Static for the pinned runtime/model; established by Stage 1. |
| Active recognition request | Dictation session / recognizer | One held shortcut. |
| Last teachable transcript | Ephemeral transcript buffer | At most one result; never serialized or logged. |
| Teach selection | Teach dialog view model | Dialog lifetime only. |

### Side effects

- Register or re-register configured global hotkeys.
- Start and communicate with the loopback-only local engine.
- Inject Unicode text into the captured foreground target.
- Atomically replace `data/settings.json` after successful validation.
- Read a user-selected local pack file and write an explicitly selected export destination.
- Display local modal Settings/Teach windows.

### Failure behavior

- Unknown locale or vocabulary scope: reject settings with a local validation message; never silently substitute Auto.
- Legacy settings migration failure: preserve the existing safe fallback and warning behavior; never partially rewrite the file.
- Invalid or oversized vocabulary: keep the editor open and do not truncate.
- Invalid/oversized pack import or duplicate installed name: show a content-free validation result and do not mutate settings; offer a unique local name rather than inferring an update.
- Pack export failure: leave settings unchanged and keep the reviewed selection available for retry.
- Engine rejection of `speech_contexts`: fail the calibration stage; do not ship a non-functional UI.
- Settings write failure from Teach: keep the dialog and ephemeral transcript for retry; do not update in-memory settings.
- Empty or failed dictation: no teachable result; Teach remains disabled.
- Next dictation: clear the previous transcript synchronously before any model loading or capture work.
- App shutdown: clear references to the ephemeral transcript; no persistence or diagnostics.

## Common ground diagrams

### Recognition sequence

```mermaid
sequenceDiagram
    actor User
    participant Hotkey
    participant App as DictationApplication
    participant Vocab as VocabularyComposer
    participant Session as DictationSession
    participant ASR as RealtimeRecognizer
    participant Engine as Local NeMo-Speech
    participant Target as Foreground target
    participant Last as EphemeralTranscriptBuffer

    User->>Hotkey: Hold configured shortcut
    Hotkey->>App: Begin exact locale
    App->>Last: Clear previous transcript
    App->>Vocab: Compose(locale, saved entries)
    alt explicit locale
        Vocab-->>App: Shared + matching base-language entries
    else Automatic
        Vocab-->>App: Shared entries only
    end
    App->>Session: Start(locale, phrase biases)
    Session->>ASR: Start request
    ASR->>Engine: session.update(language, grouped speech_contexts)
    User->>Hotkey: Release shortcut
    Session->>Engine: Commit audio
    Engine-->>Session: Final transcript
    Session->>Target: Inject final text if still eligible
    Session-->>App: Finalized transcript event
    App->>Last: Replace(text, locale)
```

### Quick-teach sequence

```mermaid
sequenceDiagram
    actor User
    participant Bubble
    participant App as DictationApplication
    participant Last as EphemeralTranscriptBuffer
    participant Teach as TeachVocabularyWindow
    participant Store as PortableSettingsStore

    User->>Bubble: Teach from last dictation…
    Bubble->>App: Teach requested
    App->>App: Suspend hotkeys
    App->>Last: Read one snapshot
    App->>Teach: Show transient sentence + suggested scope
    User->>Teach: Select contiguous words
    User->>Teach: Enter desired phrase, scope, influence
    Teach->>Store: Atomically save vocabulary upsert
    alt save succeeds
        Store-->>Teach: Saved
        Teach->>Last: Clear
        Teach-->>App: Close success
    else save fails
        Store-->>Teach: Non-sensitive error
        Teach-->>User: Keep dialog and allow retry
    end
    App->>App: Restore hotkeys
```

### Vocabulary-pack import/export sequence

```mermaid
sequenceDiagram
    actor User
    participant UI as Vocabulary settings
    participant File as Local JSON file
    participant Codec as Pack codec/validator
    participant Store as PortableSettingsStore

    alt import
        User->>UI: Choose local pack file
        UI->>File: Read bounded content
        File-->>Codec: JSON entry array
        Codec-->>UI: Validated preview or safe error
        User->>UI: Choose scope, name, and enabled state
        UI->>Store: Atomically install local collection
    else export
        User->>UI: Select reviewed entries
        UI->>Codec: Serialize phrase + symbolic weight only
        UI->>User: Show final export preview
        User->>UI: Choose destination
        UI->>File: Write one JSON array atomically
    end
```

## UI mock contract

Use [docs/VOCABULARY_UI_MOCK.md](docs/VOCABULARY_UI_MOCK.md) as the interaction template.

- Preserve its navigation, actions, privacy behavior, selection semantics, and enabled states.
- Reuse the current app theme and visual references rather than copying generic controls.
- Treat dimensions and sample content as illustrative.
- The implementation is not complete until every required mock state is rendered by `PrivateType.App.LayoutProbe`, visually inspected, and passed through `$verify-ui-quality` as required by [AGENTS.md](AGENTS.md).

## Target architecture

### 1. Static recognition locale catalog

Replace enum branching with one catalog entry per explicit locale plus Automatic:

```csharp
public sealed record RecognitionLocaleDefinition(
    string Code,
    string? BaseLanguageCode,
    string DisplayName,
    RecognitionSupportTier Tier);
```

Contract:

- `Code` is the exact engine value and persistent identity.
- `BaseLanguageCode` is null for `auto`; otherwise use a stable lowercase language code (`en`, `pl`, `es`).
- `DisplayName` is English UI text with region when multiple locales exist.
- `Tier` is `Automatic`, `TranscriptionReady`, or `BroadCoverage`.
- Catalog lookup, validation, sorting, base-language projection, and display labels have one canonical owner in `PrivateType.Core`.
- Do not derive the catalog remotely at runtime; PrivateType remains offline and the model artifact is pinned.

Required catalog:

| Tier | Locale codes |
|---|---|
| Automatic | `auto` |
| Transcription-ready | `en-US`, `en-GB`, `es-US`, `es-ES`, `fr-FR`, `fr-CA`, `it-IT`, `pt-BR`, `pt-PT`, `nl-NL`, `de-DE`, `tr-TR`, `ru-RU`, `ar-AR`, `hi-IN`, `ja-JP`, `ko-KR`, `vi-VN`, `uk-UA` |
| Broad coverage | `pl-PL`, `sv-SE`, `cs-CZ`, `nb-NO`, `da-DK`, `bg-BG`, `fi-FI`, `hr-HR`, `sk-SK`, `zh-CN`, `hu-HU`, `ro-RO`, `et-EE` |

The following adaptation-only locales are explicit non-options until the pinned model is replaced by a proven fine-tune: `el-GR`, `lt-LT`, `lv-LV`, `mt-MT`, `sl-SI`, `he-IL`, `th-TH`, `nn-NO`.

### 2. Versioned settings and migration

Change persisted shortcut identity from numeric enum values to exact locale-code strings and add vocabulary:

```csharp
public sealed record ShortcutBinding(string LocaleCode, int VirtualKey);

public enum VocabularyInfluence
{
    Low,
    Normal,
    Strong
}

public sealed record VocabularyEntry(
    string Phrase,
    string Scope,
    VocabularyInfluence Influence);

public sealed record InstalledVocabularyPack(
    string Name,
    string Scope,
    bool IsEnabled,
    IReadOnlyList<VocabularyPackEntry> Entries);

public sealed record VocabularyPackEntry(
    string Phrase,
    VocabularyInfluence Influence);
```

Persistence contract:

- Add an explicit schema version; the target schema is version 2.
- Serialize locale codes, scopes, and influence names as strings.
- `Scope` is `shared` or a base-language code present in the catalog.
- Legacy files without a schema version are parsed as schema 1. Map numeric `Language` values `0 -> pl-PL`, `1 -> en-US`, and `2 -> auto` while preserving microphone, shortcut keys, panel position, startup choice, and idle timeout.
- Perform migration in memory on load. Write version 2 only on the next successful normal save; do not mutate a readable legacy file merely by starting the app.
- Missing vocabulary and installed packs migrate to empty lists.
- Unknown legacy enum values and malformed files retain the current safe-default warning behavior.
- Keep the existing temporary-file plus replace transaction for all saves.
- Split catalog, settings domain/validation, and serialization/migration into focused files; do not turn `PortableSettings.cs` into a catch-all.

Illustrative version 2 JSON:

```json
{
  "SchemaVersion": 2,
  "MicrophoneId": "default",
  "Shortcuts": [
    { "LocaleCode": "pl-PL", "VirtualKey": 82 },
    { "LocaleCode": "en-US", "VirtualKey": 69 }
  ],
  "Vocabulary": [
    { "Phrase": "MVVM", "Scope": "shared", "Influence": "Normal" },
    { "Phrase": "dependency injection", "Scope": "en", "Influence": "Low" }
  ],
  "InstalledVocabularyPacks": [],
  "ModelIdleTimeoutMinutes": 10
}
```

The values are schema examples, not built-in vocabulary.

### 3. Vocabulary validation and composition

Create one domain service for normalization/validation and one pure composer for a recognition request.

Validation invariants:

- Normalize a phrase to Unicode NFC and trim outer whitespace before comparison/storage.
- Preserve internal whitespace, spelling, punctuation, and case.
- Reject blank text, CR/LF, other control characters, and phrases longer than 120 UTF-16 code units.
- Allow at most 200 entries and at most 16 KiB of normalized phrase UTF-8 payload across settings.
- Reject an exact ordinal duplicate of `(Scope, Phrase)` within persisted settings. Case variants remain distinct because decoder tokenization may be case-sensitive.
- Validate every scope and influence against the closed app catalogs.
- Never put a phrase in a validation exception, diagnostic event, or log message.

Composition invariants:

- Explicit locale: select Shared plus entries whose scope equals the catalog base language.
- Automatic: select Shared only.
- If the same exact phrase exists in Shared and the matching base-language scope, emit it once using the stronger influence.
- Group selected phrases by influence so the engine receives at most three `speech_contexts` objects.
- Stable ordering is influence then ordinal phrase; tests must not depend on user insertion order.
- Empty vocabulary omits `speech_contexts`, preserving the current request shape.
- Use `speech_contexts`; do not add post-recognition replacements and do not use the API's `prompt` field.

### 4. Recognition request contract

Replace `IStreamingRecognizer.StartAsync(RecognitionLanguage, ...)` with a request value that carries an exact locale and already composed phrase biases. `DictationSession` must not know persistence or vocabulary-selection rules.

Illustrative contract:

```csharp
public sealed record RecognitionPhraseGroup(
    VocabularyInfluence Influence,
    IReadOnlyList<string> Phrases);

public sealed record RecognitionRequest(
    string LocaleCode,
    IReadOnlyList<RecognitionPhraseGroup> PhraseGroups);
```

`RealtimeRecognizer` maps Low/Normal/Strong to the Stage 1 calibrated numeric values and serializes:

```json
{
  "type": "session.update",
  "session": {
    "sample_rate": 16000,
    "language": "pl-PL",
    "automatic_punctuation": true,
    "speech_contexts": [
      { "phrases": ["..."], "boost": 0.0 }
    ]
  }
}
```

The `0.0` is deliberately not a proposed production value. Stage 1 must replace the pending mapping with three proven positive values before Stage 3 proceeds.

### 5. Shareable vocabulary-pack contract

> **Redesigned and approved by the owner 2026-09-28** (JSON phrase array; 200-phrase per-dictation and 1,000-phrase stored budgets; single Vocabulary page with Your phrases and Packs sections). Strength is vocabulary-wide (see the revision note under Agreed decisions), so packs carry phrases only. The per-entry `weight` format, pack precedence by influence, and the persisted-entry budget below were replaced.

The share file is a UTF-8 JSON array of phrase strings. It is intentionally not a package manifest:

```json
["MVVM", "dependency injection", "PostgreSQL"]
```

File contract:

- Use the extension `.privatetype-vocabulary.json`; the base filename proposes the installed pack name.
- The top level must be an array of strings. Reject objects, numbers, nulls, nested arrays, malformed JSON, non-UTF-8 content, and phrases that fail the normal phrase validator. Accept UTF-8 with or without its byte-order mark.
- Duplicate phrases inside one file (after normalization) are rejected, not silently merged, so the preview always matches the file.
- A share file contains no scope, strength, name, executable content, path, URL, ID, version, author, attribution, license, provenance, transcript, or settings payload.
- Require a regular file no larger than 64 KiB selected by the user; never follow content-provided paths and never perform network access.
- Show the proposed name, the chosen scope, and every normalized phrase before installation. Do not persist until the user confirms.
- Installed pack names are unique ordinally. A collision does not imply an update; propose a unique local suffix such as `Software development (2)` and let the user edit it.
- Imported data is copied into the settings model with no live relationship to its source file and no automatic update behavior.
- Editing an installed pack edits that local collection. Removing it deletes only the installed collection after confirmation, never the source file.
- Export writes the reviewed phrases as the same canonical array (ordinal order, two-space indented), through a temporary sibling file and atomic replace/create. Failure leaves any existing destination and all application state intact.

Settings model (additive to schema 2; no version bump):

```csharp
public sealed record VocabularyPack(string Name, string Scope, bool IsEnabled, IReadOnlyList<string> Phrases);
// PortableSettings gains: IReadOnlyList<VocabularyPack> VocabularyPacks = []
```

Budgets:

- **Per request (engine-proven):** for every scope combination a dictation can use (Shared alone for Automatic, and Shared plus each base language), the composed set of personal and enabled-pack phrases stays within 200 phrases and 16 KiB normalized UTF-8. Settings validation checks every combination on save, so a dictation can never exceed the budget and nothing is truncated at request time.
- **Stored:** at most 1,000 phrases across personal vocabulary and all installed packs (enabled or not), 120 UTF-16 code units per phrase, and at most 50 installed packs. Disabled packs and packs for other languages do not count toward the per-request budget.
- An import or enable action that would break either budget is rejected with a content-free message naming the language whose budget it would exceed.

Composition contract:

- Compose personal phrases plus phrases from enabled packs whose scope is Shared or matches the explicit locale's base language. Automatic uses Shared personal phrases and Shared enabled packs only.
- Collisions between personal and pack phrases, or between packs, are emitted once. With one vocabulary-wide strength there is no precedence to resolve.
- Output remains distinct and ordinal-sorted, sent as one `speech_contexts` group at the calibrated strength.

Repository contract:

- Curated examples live under `vocabulary-packs/` and use the exact production file format.
- A repository validator calls the same parser/validator as the app. CI checks every curated file for extension, UTF-8, schema, limits, duplicates, and canonical serialization.
- Repository pack files are never copied into application output or the portable release. README may link to the directory and explain manual download and import.
- This plan does not invent an initial domain pack. Add curated content only after its phrases are intentionally reviewed as public repository data.

### 6. Ephemeral transcript ownership

Introduce a small testable owner, not a general history service:

```csharp
public sealed record FinalizedDictation(string Text, string LocaleCode);

public sealed class EphemeralTranscriptBuffer
{
    public bool HasValue { get; }
    public FinalizedDictation? TakeOrPeek(/* explicit semantics */);
    public void Replace(FinalizedDictation value);
    public void Clear();
}
```

Required semantics:

- `DictationSession` emits one finalized result after recognition has produced non-empty final text. Injection eligibility does not alter the recognized result, but a recognition failure emits nothing.
- `DictationApplication` clears the previous buffer synchronously at the beginning of every new dictation, before any await, engine load, or microphone start.
- It replaces the buffer after one successful non-empty recognition result.
- Opening the teach dialog reads the current snapshot but does not serialize it.
- Save success, Cancel, window close, next dictation, and app disposal clear the buffer.
- Save failure leaves the buffer and dialog intact for retry.
- The buffer has no file APIs, diagnostic APIs, history collection, timestamps, or transcript enumeration.
- Bubble menu enabled state derives only from `HasValue`; menu text never includes transcript content.

### 7. Word-range selection

Create a Unicode-aware word-span tokenizer/selector that returns source offsets without persisting heard text.

- A chip represents a word span and retains start/end offsets into the ephemeral sentence.
- First activation sets an anchor and selects one word.
- Activating another word selects the contiguous inclusive range between it and the anchor.
- A completed range can be restarted with a new activation; Escape clears.
- Punctuation may be visually attached, but selecting a range must preserve the original substring boundaries between its first and last word.
- The desired-term field may be prefilled from the selection. Only the edited desired term is eligible for persistence.
- Unit tests use synthetic, non-sensitive Unicode strings created for the test; do not use real dictated content.

## Shared configuration and conventions

- **Defaults:** `pl-PL` at `Ctrl+Shift+R`, `en-US` at `Ctrl+Shift+E`, empty vocabulary, Normal influence for new rows.
- **UI language:** English for all locales. Use catalog display names; do not add 32 switch arms.
- **Recording status:** use English `Listening`; the selected locale may be shown through a catalog label if it fits the approved mock without clipping.
- **Privacy:** vocabulary is private settings data. Transcript and vocabulary contents are forbidden in diagnostics, logs, exceptions, documentation evidence, screenshots, and committed fixtures.
- **Engine lifecycle:** keep the model unloaded in normal unit/layout tests. Only Stage 1 and the final live acceptance check may load it.
- **No network at runtime:** locale and base-language catalogs ship with the app.
- **Manual sharing only:** import/export uses explicit local file pickers; no URL field, discovery catalog, updater, synchronization, or bundled pack is added.
- **No silent fallback:** invalid explicit locale is a settings error, not Auto.
- **No transcript mutation:** phrase biasing changes decoder likelihood only; the final transcript is injected as returned.
- **No application targeting expansion:** secure/elevated/ineligible target rules remain unchanged.

## Stage 1: Prove and calibrate pinned-runtime phrase boosting

**Goal:** Prove that the exact pinned NeMo-Speech.cpp runtime applies per-request `speech_contexts` to the exact pinned multilingual RNN-T model, then establish ordered Low/Normal/Strong mappings with acceptable control behavior.

**Allowed files/modules:** a developer-only project under `tests/PrivateType.EngineProbe`, solution/project references needed to build it, this plan's Stage 1 final-fact table, and no production source.

**Do not change:** Settings UI, production recognizer, portable settings schema, model/runtime pins, release packaging, README promises, or recorded user data.

**Required sequence:**

1. Create failing protocol/parser tests for the probe's baseline and grouped `speech_contexts` requests.
2. Implement the smallest probe that starts or connects to the exact local pinned runtime, accepts an explicitly supplied local PCM16 WAV path, sends a selected locale and phrase groups, and reports results only to the local console.
3. Inspect the probe before executing it, as required by repository instructions.
4. Use only non-sensitive, test-purpose audio in a temporary directory. Never commit audio or transcript output.
5. Run baseline and candidate boosts against explicit English, explicit Polish containing English technical terms, and unrelated control audio.
6. Send a generated non-sensitive boundary payload proving whether 200 phrases and 16 KiB normalized phrase text are accepted; record the lower proven limit if not.
7. Record only aggregate counts, payload limits, and the chosen numeric mappings below; do not record phrases, transcripts, audio paths, or dictated text.
8. Delete temporary audio after the run and verify no probe output entered repository files.
9. Stop if capability or safety criteria fail; do not start Stage 2 under the assumption that UI can compensate.

**Risk Manifest:** Required — external provider behavior and private audio are involved.

### Risk Manifest

#### Risks and Owners

| ID | Risk | Canonical owner | Consumers |
|---|---|---|---|
| R1 | The documented API may not materially bias this pinned RNN-T path. | Engine probe protocol client | Stages 3, 4, and 6 |
| R2 | Influence values may improve target terms while inserting them into unrelated speech. | Calibration matrix and acceptance rule | Realtime recognizer mapping |
| R3 | Probe inputs/results may expose private dictated content. | Probe CLI and execution protocol | Implementer, verification report |
| R4 | The planned combined personal/pack phrase budget may exceed the pinned provider's request limit. | Engine probe boundary matrix | vocabulary validator and composer |

#### States and Variants

| ID | States or variants | Required paths | Failure edges |
|---|---|---|---|
| R1 | no contexts, one context, three grouped contexts; `en-US`, `pl-PL` | baseline and biased requests | server error, ignored context, malformed session update |
| R2 | candidate Low/Normal/Strong values; target and unrelated controls | repeated A/B matrix | non-monotonic influence, false insertion, no measurable gain |
| R3 | temp input, console result, aggregate evidence, cleanup | local-only execution | committed audio/text, diagnostic capture, undeleted temp files |
| R4 | 200 phrases; 16 KiB normalized phrase payload; grouped across three influences | generated boundary request -> pinned engine | rejection, timeout, undocumented lower cap |

#### Proof

| ID | Public seam | Planned red test | Expected observation | Final evidence |
|---|---|---|---|---|
| R1 | pinned `/v1/realtime` | probe request with a known invalid context shape, then valid shape | invalid is rejected; valid completes and changes target-term outcomes versus baseline | **Passed with model revision `ea30d66` (2026-09-28).** Invalid shape rejected; valid requests complete and raise target-term recognition. Revision `1c8deae` failed: no embedded SentencePiece tokenizer, so the engine disabled boosting. |
| R2 | candidate mapping | target/control matrix with no mapping selected | chosen levels are ordered by influence, improve target recognition, and Strong inserts boosted terms in no more than 1 of 20 unrelated controls | Boost vs baseline (en-US targets / pl-PL targets / control insertions): none 5/24, 3/8, 0/21; 0.5 11/24, 5/8, 0/21; 1 12/24, 5/8, 0/21; 2 13/24, 7/8, 1/21; 3 14/24, 8/8, 8/21; 4 12/24, 8/8, 15/21; 5 12/24, 2/8, 19/21. |
| R3 | repo and temp state | pre-run clean status and temp inventory | post-run repository has no audio/transcript artifacts and temp inputs are removed | Synthetic TTS clips and one public FLEURS clip in a temp folder, deleted after the run; probe prints aggregates only. |
| R4 | pinned `/v1/realtime` | generated boundary payload | request completes or lower accepted count/bytes are recorded before Stage 3 limits are frozen | 200 phrases / 16,200 normalized bytes accepted and completed in ~0.8 s with no boundary phrase inserted. |

#### Budget and Environment

| ID | File, module, provider, or tool | Current fact | Planned limit or required proof | Final fact |
|---|---|---|---|---|
| R1 | NeMo-Speech.cpp `1118951…` + pinned Q8_0 model | API documents `speech_contexts`; PrivateType does not send it | exact local artifact proof | Engine `1118951…` with model revision `ea30d66` (742,090,464 bytes, SHA-256 `3fc991d3…`) loads the embedded tokenizer. One request applies a single alpha, the maximum boost across its contexts, so per-entry influence cannot mix within one request. |
| R2 | boost numbers | no safe model-specific range documented | three distinct positive values; Strong control false-insertion <= 1/20 | Low 0.5, Normal 1.0, Strong 2.0. Boost 3 and above inserts boosted terms into 8–19 of 21 controls and is excluded. |
| R3 | evaluation data | repo forbids private audio/transcripts in artifacts | temporary local data only; aggregate counts only | Met. |
| R4 | phrase-context payload | no model-specific limit established | prove 200 phrases/16 KiB or revise every downstream validator/budget consistently | 200 phrases / 16 KiB proven. |

**Tests/proof:** probe unit tests, successful engine completion for baseline and biased requests, aggregate A/B matrix, `git status --short`, and explicit temp cleanup verification.

**Stop conditions:** runtime rejects or ignores valid contexts; no candidate improves target recognition; influence levels cannot be ordered; Strong exceeds the control false-insertion threshold; no safe bounded phrase payload can be established; model/runtime pins differ; private data would need to be committed.

**Implementation prompt:** Implement Stage 1 only. Create the failing probe tests first, use the exact pinned local artifacts, run the privacy-bounded A/B and generated payload-boundary matrices, record only aggregate final facts, proven limits, and calibrated mappings in this plan, clean temporary inputs, and stop on any stop condition.

Stage 1 acceptance:

**Outcome (2026-09-28): passed after re-pinning the model.** Revision `1c8deae` lacked the embedded SentencePiece tokenizer, so the engine disabled boosting. NVIDIA's revision `ea30d66` embeds it; with it, boosting raises target terms from 5/24 to 11–13/24 (English) and from 3/8 to 5–7/8 (Polish path) at no more than 1/21 control insertions. Calibrated mapping: Low 0.5, Normal 1.0, Strong 2.0. Design constraint for Stage 3: the engine applies one strength per request (the maximum), so mixed per-entry influences collapse to the strongest one.

- [x] The exact pinned runtime/model completes realtime recognition with `speech_contexts`.
- [x] Aggregate evidence shows useful target-term improvement over baseline.
- [x] Low/Normal/Strong map to three recorded, distinct, positive values satisfying the control threshold.
- [x] English and Polish-with-English-terms paths are both exercised.
- [x] The 200-phrase/16-KiB context budget is proven or every downstream planned limit is revised to the lower proven boundary.
- [x] No audio, transcript, phrase list, or sensitive path is committed or logged.
- [x] The repository remains working and the probe is excluded from portable release output.

## Stage 2: Expand recognition locales and migrate settings

**Goal:** Deliver Automatic plus all 32 usable explicit locales through existing configurable shortcuts, using stable string identities and lossless schema-1 migration while preserving Polish/English defaults.

**Allowed files/modules:** focused catalog/settings/migration files under `src/PrivateType.Core`; recognition, hotkey, status, Settings, and composition-root files under `src/PrivateType.App`; corresponding Core/App tests; Settings states in `PrivateType.App.LayoutProbe`; no vocabulary UI yet.

**Do not change:** engine/model pins, audio pipeline, text injection eligibility, startup transaction semantics, model loading, vocabulary behavior, quick-teach behavior, or portable release layout.

**Required sequence:**

1. Add failing catalog tests for exact count, codes, tiers, base-language mappings, sort order, and adaptation-only exclusion.
2. Add failing schema-1 migration and schema-2 round-trip tests before changing persistence types.
3. Introduce the static catalog and string locale identity; migrate runtime/session/hotkey contracts away from the enum.
4. Implement in-memory legacy migration and version-2 serialization.
5. Replace all hard-coded language switches/list labels with catalog lookup.
6. Update Settings selector and recording/bubble labels without adding vocabulary UI.
7. Remove the old `RecognitionLanguage` enum and its Polish/English hard-coded catalog constants after every consumer is migrated.
8. Run Core/App tests, LayoutProbe, and inspect all expanded-language states.

**Risk Manifest:** Required — persistent migration and a cross-layer identity change are involved.

### Risk Manifest

#### Risks and Owners

| ID | Risk | Canonical owner | Consumers |
|---|---|---|---|
| R1 | Locale identity diverges among persistence, UI, hotkeys, session, and engine. | `RecognitionLocaleCatalog` | validator, Settings, bubble, session, recognizer |
| R2 | Existing numeric settings are rejected or reset, losing user choices. | versioned settings deserializer/migrator | application startup and Settings save |
| R3 | Large selector clips, becomes inaccessible, or exposes unsupported locales. | language selector view model/catalog ordering | Settings UI and LayoutProbe |

#### States and Variants

| ID | States or variants | Required paths | Failure edges |
|---|---|---|---|
| R1 | Auto, 19 transcription-ready, 13 broad-coverage | load -> bind -> hotkey -> session -> engine | unknown code, wrong base language, wrong display label |
| R2 | missing file, valid schema 1, valid schema 2, malformed, unknown legacy enum | load and next save | silent reset, partial migration, eager rewrite |
| R3 | closed/open selector, type-ahead, keyboard-only, long regional names | Settings default and edited states | clipping, unreachable items, wrong saved code |

#### Persistence

| ID | Invariant | Enforcement | Transaction boundary | Concurrency |
|---|---|---|---|---|
| R2 | schema 1 maps `0/1/2` exactly and preserves all unrelated fields | migration tests and validator | existing atomic settings save; no write on load | Settings/hotkeys suspended during save as today |

#### Proof

| ID | Public seam | Planned red test | Expected observation | Final evidence |
|---|---|---|---|---|
| R1 | catalog and recognizer request | every required code plus invalid code | 33 unique choices; exact engine code; invalid rejected | `RecognitionLocaleTests`, `Sends_the_catalog_locale_code_verbatim_to_the_local_engine`, `Refuses_to_send_an_unsupported_locale_instead_of_falling_back`. |
| R2 | `PortableSettingsStore.Load/Save` | representative schema-1 JSON | migrated settings equal old choices and next save writes schema 2 | `Migrates_schema_1_numeric_languages_without_losing_other_settings`, `Loading_a_legacy_file_does_not_rewrite_it`, `Saves_schema_2_with_locale_codes_and_reloads_it`. |
| R3 | Settings selector | populated layout/automation probe | all choices searchable/reachable with no clipping | `SettingsLanguageProbe`: 33 choices, no clipped labels, open list rendered, type-ahead, focus, saved codes. Type-ahead matches display names only, not locale codes. |

#### Budget and Environment

| ID | File, module, provider, or tool | Current fact | Planned limit or required proof | Final fact |
|---|---|---|---|---|
| R1 | `PortableSettings.cs` and enum consumers | language rules are duplicated | split catalog/migration/validation owners; no 33-arm switches | `RecognitionLocales.cs`, `PortableSettingsMigration.cs`, validator in `PortableSettings.cs`; enum removed. |
| R3 | fixed Settings window | current selector has three items | bounded content and verified supported DPI/text scales | Verified at 96 DPI only; other scales not yet checked. |

**Tests/proof:** Core migration/catalog/validator tests, App hotkey/status/recognizer tests, Settings automation/layout probe, affected Core/App projects, `git diff --check`.

**Stop conditions:** official/pinned locale facts differ; migration cannot distinguish legacy values safely; any old valid settings field is lost; selector cannot be made accessible in the approved shell without a mock revision.

**Implementation prompt:** Implement Stage 2 only. Start with failing catalog and migration tests, replace language identity end to end, preserve existing settings and defaults, remove the old enum/hard-coded labels, run Core/App/LayoutProbe verification, and stop on any stop condition.

Stage 2 acceptance:

- [x] Automatic plus exactly 32 usable explicit locales are selectable and sent verbatim to the engine.
- [x] The eight adaptation-only locales are absent.
- [x] `en-US`/`en-GB`, `es-US`/`es-ES`, `fr-FR`/`fr-CA`, and `pt-BR`/`pt-PT` map to shared base languages correctly.
- [x] Existing Polish/English/Auto settings migrate without losing any unrelated setting.
- [x] New installs retain Polish and English defaults.
- [x] Invalid locale codes fail validation without silently becoming Auto.
- [x] The old enum and duplicated language switch arms are removed.
- [ ] Settings language selection passes layout and keyboard/accessibility checks.

## Stage 3: Persistent vocabulary and decoder biasing

**Goal:** Deliver Settings-based Shared/base-language vocabulary management and apply the composed Low/Normal/Strong phrase groups to every new recognition session.

**Dependencies:** Stage 1 calibrated mappings recorded; Stage 2 locale catalog and schema migration complete.

**Allowed files/modules:** vocabulary domain/validation/composition files in Core; version-2 settings DTO/store; recognizer request and serialization; Settings page/user control/view models; application Settings opening seam; Core/App tests; LayoutProbe; mock and README only where Stage 3 behavior is documented.

**Do not change:** transcript retention, bubble Teach action, target injection, model pins, UI localization, postprocessing/replacement, named profiles, import/export, or Automatic language detection behavior.

**Required sequence:**

1. Reconcile Stage 1 final facts; stop if mappings remain Pending.
2. Add failing vocabulary normalization, limits, duplicate, scope, merge, precedence, grouping, and Auto tests.
3. Add failing version-2 vocabulary round-trip and legacy-empty-vocabulary tests.
4. Add failing recognizer session JSON tests for empty, explicit-locale, Auto, and all three influence groups.
5. Implement vocabulary domain owners and session composition before UI.
6. Extend recognizer request/serialization using the calibrated mapping and `speech_contexts` only.
7. Build the Settings navigation and Stage 3 Personal Vocabulary page from the mock as a focused control/view model, excluding the Stage 4 pack/import/export controls; keep persistence and composition outside code-behind.
8. Open Settings on Vocabulary when requested from the bubble's generic `Vocabulary…` action; do not add Teach yet.
9. Extend LayoutProbe for every Stage 3 mock state and run `$verify-ui-quality` before handoff.

**Risk Manifest:** Required — persistent private settings, provider serialization, and cross-layer UI are involved.

### Risk Manifest

#### Risks and Owners

| ID | Risk | Canonical owner | Consumers |
|---|---|---|---|
| R1 | Shared/base-language selection or duplicate precedence sends the wrong terms. | pure `VocabularyComposer` | session factory and recognizer |
| R2 | Invalid/oversized/private vocabulary is truncated, leaked, or corrupts settings. | vocabulary validator + atomic settings store | Settings and quick teach later |
| R3 | Influence labels drift from calibrated engine values or payload shape. | one recognizer calibration/serializer owner | realtime engine |
| R4 | Vocabulary UI makes Settings oversized or inaccessible and pushes domain rules into code-behind. | Vocabulary view model/control + approved mock | Settings shell and LayoutProbe |

#### States and Variants

| ID | States or variants | Required paths | Failure edges |
|---|---|---|---|
| R1 | Shared, matching base, nonmatching base, duplicate with stronger local/shared, Auto | save -> compose -> request | union-all on Auto, duplicate emission, weaker wins |
| R2 | empty, valid Unicode/case variants, duplicate, 120-char boundary, 200-entry boundary, 16-KiB boundary | edit -> validate -> atomic save/load | silent trim/truncate, sensitive error, partial write |
| R3 | no contexts; Low, Normal, Strong; mixed groups | request -> JSON -> pinned engine | zero/unproven boost, `prompt` used, wrong grouping |
| R4 | empty/populated/error/max-scroll; Shared/base scopes | mouse, keyboard, automation, DPI/text scale | clipped footer, lost edits, inaccessible influence |

#### Persistence

| ID | Invariant | Enforcement | Transaction boundary | Concurrency |
|---|---|---|---|---|
| R2 | complete settings validate before one atomic replacement; vocabulary content never enters errors/logs | validator and red tests | existing temp-file replacement | hotkeys suspended while modal Settings saves |

#### Proof

| ID | Public seam | Planned red test | Expected observation | Final evidence |
|---|---|---|---|---|
| R1 | `Compose(entries, locale)` | explicit and Auto matrices | exact selected set, strongest duplicate wins, stable groups | `VocabularyTests` compose matrix: Shared + base language for explicit locales, Shared only for Auto, distinct ordinal order. With one strength, duplicate precedence reduces to de-duplication. |
| R2 | Settings store/validator | boundary and injected save-failure tests | no truncation/partial update/sensitive error | Length, count, payload, duplicate, scope, and control-character tests; messages never quote phrases; save path unchanged (atomic temp-file replace); damaged files keep valid entries. |
| R3 | serialized `session.update` | snapshot/JSON semantic assertions | calibrated values grouped under `speech_contexts`; empty omitted | One `speech_contexts` object with boost 0.5/1.0/2.0; omitted when empty; `prompt` never sent (`WindowsBoundaryTests`). |
| R4 | Settings + LayoutProbe | every Stage 3 mock state | flow matches mock; PASS from UI-quality gate | `SettingsVocabularyProbe`: tabs, direct open, empty/populated/long-Unicode/scroll states, validation keeps page open, saved entries. Ctrl+Tab and non-96-DPI scales not probed. |

#### Budget and Environment

| ID | File, module, provider, or tool | Current fact | Planned limit or required proof | Final fact |
|---|---|---|---|---|
| R2 | vocabulary payload | no vocabulary today | 200 entries, 120 chars each, 16 KiB normalized UTF-8 total | Enforced by `VocabularyRules`; engine accepted 200 phrases / 16 KiB in Stage 1. |
| R3 | boost calibration | established only by Stage 1 | one mapping owner; exact three recorded values | Pending |
| R4 | `SettingsWindow.xaml(.cs)` | already owns general form interactions | new focused Vocabulary control/view model; no vocabulary domain rules in window code-behind | Pending |

**Tests/proof:** Core vocabulary/settings/composer tests, App recognizer/Settings/menu tests, semantic JSON assertions, LayoutProbe images and assertions, full affected Core/App projects, `$verify-ui-quality`, `git diff --check`.

**Stop conditions:** Stage 1 mapping is missing or invalid; provider payload limits are lower than planned caps; sensitive values appear in errors/diagnostics; UI cannot meet the mock/accessibility states without revising the approved interaction.

**Implementation prompt:** Implement Stage 3 only. Reconcile Stage 1 calibration, write failing domain/persistence/protocol tests first, implement Settings vocabulary end to end, send only applicable grouped contexts, run all UI and code gates, and stop on any stop condition.

Stage 3 acceptance:

- [x] Users can add, edit, remove, and scope desired phrases in Settings, with one vocabulary-wide strength (revised decision).
- [x] Shared and base-language inheritance follows the agreed matrix; Auto uses Shared only.
- [x] Exact cross-scope duplicates emit once (strength is vocabulary-wide).
- [x] Empty vocabulary preserves the previous recognizer payload behavior.
- [x] Low/Normal/Strong use the one calibrated mapping and `prompt` is not sent.
- [x] Invalid and oversized data is rejected without truncation, partial save, or sensitive error text.
- [x] `Vocabulary…` opens the approved Settings page.
- [ ] Every Stage 3 mock state passes LayoutProbe and UI-quality verification. (LayoutProbe passes; no `$verify-ui-quality` tool exists in this repository, and non-96-DPI scales are unchecked.)

## Stage 4: Simple offline vocabulary-pack sharing

> **Redesigned and approved by the owner 2026-09-28** (JSON phrase array; 200-phrase per-dictation and 1,000-phrase stored budgets; single Vocabulary page with Your phrases and Packs sections). Packs hold phrases only; strength stays vocabulary-wide. The Personal/Installed sub-tabs and per-row export checkboxes of the original mock are replaced by one Vocabulary page with two sections and a reviewed export dialog.

**Goal:** Let users import, manage, and export named local phrase collections through a phrase-only JSON array, with no network, identity, versioning, or hidden metadata.

**Dependencies:** Stage 3 vocabulary validation, composition, settings persistence, and Vocabulary page complete (done).

**Allowed files/modules:** pack domain types and codec; atomic exporter; vocabulary validation/composition extensions; settings model; Vocabulary page controls/view models, import/export dialogs, and local file pickers; repository `vocabulary-packs/` directory, validator, and targeted CI; Core/App tests; LayoutProbe; mock and README.

**Do not change:** model/runtime pins, calibrated strength mapping, transcript retention, target injection, network behavior, settings schema version, UI localization, or update behavior.

**Required sequence:**

1. Add failing codec tests: string-array schema, UTF-8 with/without BOM, normalization, rejected non-string items, in-file duplicates, malformed/oversized input, and content-free errors.
2. Add failing settings tests: pack round-trip, legacy files load with no packs, damaged-pack repair, and both budgets (per-request per scope combination; stored totals).
3. Add failing composer tests: disabled packs, Shared/matching/non-matching scopes, Automatic, personal/pack and pack/pack de-duplication, deterministic order.
4. Add failing import tests: preview before confirmation, explicit scope, unique-name suggestion, cancel without mutation, budget rejection naming the language, source-file independence.
5. Add failing export tests: reviewed personal phrases from one scope, whole installed pack, canonical output, cancel, and atomic destination failure.
6. Implement codec and validation, then persistence and composition, then import/export orchestration. Keep file dialogs and code-behind free of domain rules.
7. Add `vocabulary-packs/` contributor guidance and a validator reusing the production codec; run it in CI when pack files, the codec, or the validator change; assert the directory is absent from release output.
8. Build the UI from the redesigned mock: "Your phrases" and "Packs" sections, import preview, export review, edit, rename, enable/disable, and confirmed removal.
9. Extend LayoutProbe for every Stage 4 mock state.
10. Update README with the format, manual workflow, offline guarantee, scope-at-import rule, and budgets.

**Stop conditions:** the parser cannot enforce a bounded deterministic schema; an import/export error reveals phrase content; file replacement is not atomic on Windows; removal could touch the source file; the UI would need download, update, or hidden metadata to work.

Stage 4 acceptance:

Evidence (2026-09-28): `VocabularyPackTests`, `CuratedVocabularyPackTests`, `VocabularyEditorTests`, and `SettingsPackProbe` (section, filters, toggles, removal confirmation, content-free bad-file rejection, import preview/collision, editor error, export review and atomic canonical write). The repository has no curated packs yet; `Test-PortableRelease.ps1` now fails if packs or the engine probe reach release output. Settings-save I/O failure is handled by the existing save path and not separately probed.

- [x] A user can import a validated `.privatetype-vocabulary.json` string array after reviewing every phrase and choosing one scope, a unique local name, and the enabled state.
- [x] Files with objects, weights, or other non-string items are rejected with a content-free message.
- [x] Installed packs can be enabled, disabled, renamed, re-scoped, edited, exported, and removed with confirmation.
- [x] No network access, discovery, synchronization, ID, version, provenance, attribution, license, or update behavior exists.
- [x] Each dictation sends personal plus applicable enabled-pack phrases once each, within 200 phrases / 16 KiB for every scope combination, validated on save.
- [x] Personal export writes only phrases reviewed in the export dialog from the visible scope; both export paths show the exact array before an atomic write.
- [x] Invalid or over-budget imports and failed settings/export writes cause no truncation, partial change, sensitive error, or source-file change.
- [x] Repository example files use the production format, pass the shared validator in targeted CI, and are absent from release output.
- [x] Every Stage 4 mock state passes LayoutProbe.

## Stage 5: Ephemeral quick teaching from the bubble

**Goal:** Let the user teach a desired word or contiguous phrase from the immediately previous dictation without persistent transcript history or modification of already injected text.

**Dependencies:** Stage 4 complete; Stage 3 personal-vocabulary upsert/persistence seam remains the save destination.

**Allowed files/modules:** finalized-result contract/session event; ephemeral buffer; Unicode word-span selection domain; `DictationApplication` orchestration; bubble menu and events; separate Teach window/view model; Core/App tests; LayoutProbe; README privacy text.

**Do not change:** final text injection, target eligibility, clipboard, transcript rewriting, persistent history, diagnostics payload, automatic teach display, installed packs/import/export, or recognition engine behavior.

**Required sequence:**

1. Add failing ephemeral-buffer lifecycle tests for replace, next-start clear, save/cancel clear, save-failure retain, empty/fault no value, and disposal.
2. Add failing session result-event tests without changing injection assertions.
3. Add failing Unicode word-span and contiguous-selection tests.
4. Add failing bubble Teach enabled-state and modal hotkey suspension/restoration tests.
5. Implement the finalized-result event and the single-value buffer; clear synchronously before new dictation begins.
6. Implement bubble actions and the separate Teach dialog from the mock.
7. Route save through the Stage 3 personal-vocabulary upsert and atomic settings store. Quick teach never edits or targets an installed pack. Update in-memory settings only after persistence succeeds.
8. Ensure Cancel/window-close consumes the transcript; save failure retains it for retry.
9. Extend LayoutProbe for every Teach/bubble state and run `$verify-ui-quality`.
10. Update README privacy/settings/use text without including sample transcripts.

**Risk Manifest:** Required — this stage intentionally extends sensitive transcript lifetime and crosses session/UI/persistence boundaries.

### Risk Manifest

#### Risks and Owners

| ID | Risk | Canonical owner | Consumers |
|---|---|---|---|
| R1 | A transcript survives longer than agreed or leaks into persistence/diagnostics. | `EphemeralTranscriptBuffer` lifecycle + composition root | bubble state and Teach dialog |
| R2 | The wrong word range is selected for Unicode, punctuation, or keyboard interaction. | word-span tokenizer and selection model | Teach view |
| R3 | Teach saves partially, consumes the transcript on failure, or races dictation/hotkeys. | Teach save transaction orchestrator | settings, hotkeys, buffer |
| R4 | Quick teach implies current-document correction or exposes text in the bubble/menu. | Teach UI contract and bubble presentation | user-facing workflow |

#### States and Variants

| ID | States or variants | Required paths | Failure edges |
|---|---|---|---|
| R1 | none, one result, dialog open, consumed, next dictation, shutdown | session -> app -> buffer -> dialog | stale previous result, multiple-history growth, logging |
| R2 | one word, forward range, reverse range, punctuation, Unicode, clear/restart, keyboard | transcript -> spans -> desired prefill | broken offsets, noncontiguous range, inaccessible chips |
| R3 | save success, validation failure, I/O failure, cancel, close | suspend -> edit -> save/clear -> restore | in-memory/file divergence, lost retry, hotkeys remain suspended |
| R4 | Teach disabled/enabled; on-demand only | bubble menu -> modal dialog | automatic popup, menu transcript preview, target rewrite |

#### Persistence

| ID | Invariant | Enforcement | Transaction boundary | Concurrency |
|---|---|---|---|---|
| R1 | only desired vocabulary entry persists; transcript object has no serializer/store path | architecture test/review and diagnostics assertions | none for transcript | one UI dispatcher owner; clear before async begin |
| R3 | in-memory settings change only after atomic file save | injected failing store tests | one settings replacement | hotkeys suspended for modal Teach window |

#### Proof

| ID | Public seam | Planned red test | Expected observation | Final evidence |
|---|---|---|---|---|
| R1 | buffer and diagnostics/store fakes | complete then begin/cancel/dispose | exact lifecycle clears; no transcript in captured writes/logs | Pending |
| R2 | tokenizer/selection model | synthetic Unicode/punctuation cases | correct contiguous offsets and keyboard state | Pending |
| R3 | Teach transaction with failing store | save throws | dialog/buffer remain, settings unchanged, retry succeeds, hotkeys restored on exit | Pending |
| R4 | bubble/Teach LayoutProbe | every approved state | on-demand, no rewrite/copy action, PASS UI gate | Pending |

#### Budget and Environment

| ID | File, module, provider, or tool | Current fact | Planned limit or required proof | Final fact |
|---|---|---|---|---|
| R1 | transcript retention | current final text is discarded immediately after injection | exactly one in-memory immutable string; no history collection/timestamp/file API | Pending |
| R2 | transcript display | current bubble displays live plain text only | separate focused dialog, wrapping chips, bounded scroll, accessible selection | Pending |
| R3 | `DictationApplication.cs` | already coordinates many concerns | delegate buffer/selection/save rules to focused types; composition root only | Pending |

**Tests/proof:** Core session/buffer/selection tests, App bubble/Teach/settings-failure tests, privacy assertions, LayoutProbe, affected Core/App projects, `$verify-ui-quality`, manual on-demand workflow with non-sensitive text, `git diff --check`.

**Stop conditions:** transcript content reaches diagnostics/files except the desired term; next dictation can begin without clearing previous content; save failure loses retry state or diverges settings; contiguous selection cannot be made keyboard accessible; workflow requires editing the target application.

**Implementation prompt:** Implement Stage 5 only. Start with failing privacy/lifecycle/selection tests, add the one-value buffer and finalized event, implement the on-demand bubble/Teach flow exactly as mocked, prove failure recovery and hotkey restoration, run UI-quality verification, and stop on any stop condition.

Stage 5 acceptance:

Evidence (2026-09-28): `QuickTeachTests` (buffer lifecycle, word spans, range selection, session result event including skipped insertion), `TeachProbe` (menu disabled/enabled without transcript text, no-selection/one-word/range/edited states, keyboard extension, Escape, save failure keeps dialog and selection, retry saves only the typed phrase). `DictationApplication` clears the buffer synchronously at dictation start, drops results from superseded dictations, clears on every dialog exit and on disposal, and updates settings only after the atomic save succeeds. Diagnostics record only event names and error types.

- [x] Teach is enabled only for one non-empty last result and appears only on demand.
- [x] The previous result is cleared before the next dictation starts and on every agreed terminal path.
- [x] One word or a contiguous range can be selected with mouse and keyboard.
- [x] Scope is suggested but editable; the taught phrase uses the vocabulary-wide strength (no per-entry influence, per the 2026-09-28 revision).
- [x] Only the desired entry persists; heard text and transcript never enter settings or diagnostics.
- [x] Save failure supports retry without partial in-memory update.
- [x] Already injected text and clipboard remain untouched.
- [ ] Every Stage 5 mock state passes LayoutProbe and UI-quality verification. (LayoutProbe passes; no UI-quality tool exists in this repository.)

## Stage 6: Full-system verification and user documentation

**Goal:** Verify the complete affected surface, update user-first documentation, and prove the portable build remains private and functional.

**Dependencies:** Stages 1–5 complete with reconciled evidence.

**Allowed files/modules:** tests/probes and documentation; production repairs must return to and satisfy the owning earlier stage's manifest rather than being hidden in this stage.

**Do not change:** feature scope, model/runtime pins, calibration without rerunning Stage 1, release topology, privacy promises, or deferred features.

**Required sequence:**

1. Re-run affected Core and Windows test projects.
2. Run the repository pack validator over every curated share file and verify deterministic output.
3. Run the complete LayoutProbe and inspect every existing and new rendered state.
4. Run `$verify-ui-quality` against the mock, existing design references, realistic non-sensitive content, keyboard access, supported DPI/text scales, and complete state transitions.
5. Run the live local-model acceptance matrix with model loading limited to the relevant checks; record only aggregate non-sensitive evidence.
6. Update README usage, language availability, vocabulary behavior, manual pack workflow, settings storage, troubleshooting, and exact ephemeral-transcript privacy statement.
7. Build and verify the portable release using repository scripts, after inspecting them as required; assert `vocabulary-packs/` is absent from the output.
8. Verify no audio/transcript artifacts, debug controls, unintended sample vocabulary in production/output, stale enum references, or broken paths remain.
9. Relaunch the exact built PrivateType executable before requesting user acceptance, following [AGENTS.md](AGENTS.md).

**Risk Manifest:** Not required — this stage adds no new production behavior; defects must be repaired and re-proved under the earlier stage that owns the risk.

**Tests/proof:** all affected `dotnet test` projects, repository pack validator, `PrivateType.App.LayoutProbe`, UI-quality PASS, `git diff --check`, path/link validation, portable build/test scripts and pack-exclusion assertion, exact-process relaunch, manual end-to-end acceptance.

**Stop conditions:** any prior risk evidence is Pending; UI gate is FAIL/BLOCKED; portable verification fails; README contradicts runtime behavior; sensitive artifacts exist; the exact built app cannot be relaunched.

**Implementation prompt:** Implement Stage 6 only. Reconcile all prior evidence, run full code/UI/live/portable verification, update user-first documentation, route defects back to their owning stage, relaunch the exact verified build, and stop on any stop condition.

Stage 6 acceptance:

Evidence (2026-09-28): Core 137, App 118, and probe 10 tests pass; LayoutProbe reports 20 PASS lines covering Stages 2–5 and every state was inspected. Live matrix on engine `1118951…` and model `ea30d66` (synthetic TTS clips plus the public FLEURS Polish clip, deleted afterwards): baseline en-US 5/24, Automatic 5/24, Polish path 3/8; Low 11/24, 11/24, 5/8; Normal 12/24, 12/24, 5/8; Strong 13/24, 13/24, 7/8; control insertions 0/20 except 1/20 at Strong; 200 phrases / 16 KiB accepted. Portable 1.1.0 build: 201,699,891-byte folder, 79,066,624-byte ZIP; `Test-PortableRelease.ps1` passes, including the new repository-only exclusion check. No obsolete enum, sample vocabulary in `src`, debug output, broken relative links, or whitespace errors. There are no curated packs yet, so the pack validator passes over an empty set. Display scaling: WPF lays out in device-independent units, so 125–200% scaling changes only the available height, which the 520-DIP Settings probe covers; Windows' separate text-size setting is not verified. No `$verify-ui-quality` tool exists in this repository.

- [x] All affected Core and App tests pass.
- [x] LayoutProbe covers and passes all existing and new states.
- [ ] `$verify-ui-quality` reports PASS with evidence. (Not available in this repository; LayoutProbe and manual inspection used instead.)
- [x] Live aggregate acceptance confirms calibrated vocabulary for explicit English, explicit Polish with English technical terms, and Automatic Shared-only behavior.
- [x] README accurately explains 32 locales, defaults, vocabulary persistence, simple manual pack sharing, and ephemeral quick teaching.
- [x] Every curated repository share file passes the production validator.
- [x] Portable release verification passes and contains no curated packs, test probe, or private artifacts.
- [x] No obsolete enum/hard-coded language path, unintended production sample vocabulary, debug UI, stale link, or private evidence remains.
- [x] The verified executable is relaunched before user testing.

## Test strategy

| Layer | Primary risks covered | Required examples |
|---|---|---|
| Pure Core unit tests | catalog identity, migration, vocabulary/pack validation and composition, pack codec/export, buffer lifetime, word ranges | exact locale sets; schema-1 mapping; Shared/base/Auto matrix; pack schema/precedence; Unicode/case; lifecycle terminal paths |
| App boundary tests | hotkey/request mapping, JSON session payload, modal save/hotkey behavior, import/export transactions, menu enabled state | empty and three-group contexts; invalid locale; pack preview/cancel/failure; Teach unavailable/available |
| LayoutProbe | visual hierarchy, bounded layout, complete states, automation hooks | every personal/pack/teach state enumerated in the mock at supported DPI/text scales |
| Engine probe | pinned provider capability and calibration | baseline/Low/Normal/Strong target and unrelated controls for English and Polish code-switching |
| Repository pack validation | share-file contract and release exclusion | every curated JSON file parses canonically; pack directory absent from app output |
| Manual app acceptance | complete hold/release/inject/settings/import/export/teach flow | explicit locale, Auto, vocabulary save, local pack round-trip, next-dictation clear, no target rewrite |
| Portable verification | packaging and privacy footprint | no curated pack/probe/audio/transcript fixture; app starts from relocated release; model remains separate |

No test or evidence artifact may contain captured microphone audio or real dictated text. Synthetic strings used solely to exercise tokenization must be clearly non-sensitive and invented for the test.

## Flow traceability

| Pipeline step | Planned owner/code seam | Planned proof |
|---|---|---|
| Select exact locale | locale catalog + Settings shortcut editor | catalog tests + selector LayoutProbe |
| Load old settings | versioned settings migrator | schema-1 preservation tests |
| Persist vocabulary | validator + atomic settings store | round-trip, boundary, and injected-failure tests |
| Import a pack | bounded pack codec + import transaction | schema/size/preview/cancel/name-collision tests |
| Manage installed packs | settings store + pack view model | enable/edit/rename/remove atomicity tests + LayoutProbe |
| Choose applicable terms | `VocabularyComposer` | explicit/base/Auto/duplicate matrix |
| Export selected terms | selection model + pack exporter | exact reviewed array + destination-failure tests |
| Map influence | one calibrated mapping owner | Stage 1 aggregate matrix + serializer tests |
| Start ASR | recognition request + `RealtimeRecognizer` | semantic JSON tests + pinned engine probe |
| Finalize/inject | existing `DictationSession` path plus result event | existing injection tests + new event tests |
| Retain one teachable result | `EphemeralTranscriptBuffer` | lifecycle/privacy tests |
| Open quick teach | bubble event + app orchestration | enabled-state/modal tests + LayoutProbe |
| Select heard phrase | word-span tokenizer/selection model | Unicode, punctuation, forward/reverse range tests |
| Save desired term | vocabulary upsert + atomic store | success/duplicate/failure retry tests |
| Clear sensitive state | app begin/save/cancel/dispose paths | terminal-path tests and no-persistence assertions |

## Refactor removal list

Remove or replace the whole obsolete footprint as its replacement lands:

- `RecognitionLanguage` enum and every switch over Polish/English/Auto.
- `RealtimeRecognizer.ToEngineLanguage` enum mapping.
- `SettingsWindow` hard-coded three-language list.
- `PortableSettingsValidator` enum-specific language predicate.
- `HotkeyCatalog.Polish`, `HotkeyCatalog.English`, and `HotkeyCatalog.All` if no longer required by tests/runtime.
- `DictationStatusText` fallback that treats every non-English locale as Polish.
- `DictationBubble.LanguageLabel` hard-coded labels.
- Tests asserting enum identities rather than stable locale codes/catalog behavior.
- The TODO statement that broadly implies phrase boosting is unavailable for the current RNN-T model; retain the accurate Flashlight/CTC limitation while documenting the proven native `speech_contexts` path.

Do not leave compatibility aliases or dead enum adapters after schema migration tests prove the new path.

## Explicit non-goals

- Built-in or automatically enabled domain packs such as Software, Medical, or Legal.
- Network pack discovery/download, synchronization, automatic updates, repository browsing inside the app, or release-bundled packs.
- Pack IDs, versions, provenance/fork tracking, attribution/license fields, per-entry scope, or raw numeric weights in the share format.
- Persistent transcript history or more than one in-memory last result.
- Saving misrecognized text or correction pairs.
- Rewriting prior injected text, controlling another app's caret, or using the clipboard.
- Automatic display of the Teach window after every dictation.
- Per-locale vocabulary below the base-language layer.
- Raw numeric boost controls.
- Adaptation-only languages without a pinned, evaluated fine-tuned model.
- App-interface translation.
- Cloud recognition, accounts, telemetry, or network-fetched catalogs.
- N-gram language models, Flashlight/CTC decoding, model fine-tuning, diarization, or a second local LLM.

## Implementation order

1. Stage 1 must prove capability and produce calibrated values.
2. Stage 2 introduces stable locale identity and migration independently of vocabulary.
3. Stage 3 adds persistent vocabulary and decoder biasing using Stage 1/2 contracts.
4. Stage 4 adds simple offline pack import/export and installed-pack composition on top of Stage 3.
5. Stage 5 adds the privacy-sensitive quick-teach workflow, saving only into personal vocabulary.
6. Stage 6 performs full-system documentation, release verification, relaunch, and user acceptance.

Do not parallelize production stages: each later stage consumes contracts and evidence from the previous stage. Within a stage, independent test, catalog, or layout work may run in parallel only after its canonical owners are fixed.

## Definition of done

The feature is done only when:

- all agreed decisions are observable in the built app;
- every stage acceptance item is checked with evidence;
- every Risk Manifest final-evidence/final-fact cell is reconciled;
- Automatic plus exactly 32 explicit locales are usable through shortcuts;
- old valid settings migrate without loss;
- vocabulary composition and calibrated phrase biasing work end to end;
- simple weighted-entry packs import/export only through explicit local actions, remain separately manageable, obey precedence/scope rules, and never add network/update behavior;
- curated repository packs pass the production validator and are not bundled into application releases;
- quick teach retains at most one transcript for the agreed lifetime and persists only the desired entry;
- no transcript rewriting, persistent history, private diagnostics, or cloud behavior exists;
- all affected code, UI, live engine, path, and portable-release gates pass;
- README and TODO accurately describe the shipped behavior;
- the exact verified executable is relaunched before requesting user acceptance;
- completed milestones are committed with clear messages and the current branch is pushed only when the user requests or the active implementation workflow requires it.
