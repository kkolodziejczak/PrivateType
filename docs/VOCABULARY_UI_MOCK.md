# Vocabulary and language UI mock

## Status and authority

This is the interaction template for the PrivateType vocabulary and expanded-language feature.

- **Authoritative:** navigation, field meanings, available actions, selection behavior, privacy text, enabled/disabled states, and keyboard/accessibility expectations.
- **Directional:** exact pixel sizes, wrapping, and spacing. The implementation must reuse PrivateType's existing colors, typography, rounded surfaces, and frameless-window treatment, then pass the repository layout probe and UI-quality verification.
- **Sample data only:** `MVVM`, `PrivateType`, `PostgreSQL`, and the example transcript. Do not ship these as built-in vocabulary and do not use real dictated text in fixtures, screenshots, documentation, or diagnostics.
- **Implementation boundary:** this mock authorizes only explicit local import/export of the simple weighted-entry pack format described below. It does not authorize transcript history, correction-pair replacement, automatic rewriting of injected text, network discovery/download, synchronization/updates, bundled packs, or UI localization.

The existing visual references remain [settings.png](images/settings.png) and [recording-bubble.png](images/recording-bubble.png).

## Settings shell

The current Settings window becomes a bounded-height window with two top-level pages. `General` contains the existing microphone, shortcuts, startup, model, diagnostics, and license controls. `Vocabulary` contains the new editor. Do not place the complete vocabulary editor beneath the existing form; that would make the window excessively tall and difficult to verify at larger text/DPI scales.

```text
┌────────────────────────────────────────────────────────────────────────┐
│ PrivateType — settings                                             ×  │
├────────────────────────────────────────────────────────────────────────┤
│                                                                        │
│  Settings                                                              │
│  Configure local dictation.                                            │
│                                                                        │
│  [ General ]  [ Vocabulary ]                                           │
│  ────────────────────────────────────────────────────────────────────  │
│                                                                        │
│  Selected page content scrolls here; footer remains visible.           │
│                                                                        │
│  ────────────────────────────────────────────────────────────────────  │
│  [Open-source licenses…] [View diagnostics…]      [Cancel] [Save]     │
└────────────────────────────────────────────────────────────────────────┘
```

Annotations:

1. The active page is visually distinct and exposed as selected to UI Automation.
2. `Ctrl+Tab` switches pages; the tab controls are reachable in normal tab order.
3. Opening Settings normally selects `General`.
4. Choosing `Vocabulary…` from the bubble opens Settings directly on `Vocabulary`.
5. Cancelling discards edits from both pages. Saving validates and atomically persists the complete settings object.
6. The footer remains visible when content scrolls.

## Expanded shortcut language selector

The shortcut row keeps the existing language-plus-hotkey structure. The language selector uses exact recognition locales, not base-language vocabulary scopes.

```text
┌──────────────────────────────────────────────────────────────────────┐
│ [ Polish (Poland)                         ▾ ] [ Ctrl+Shift+R ] [—]  │
│ [ English (United States)                 ▾ ] [ Ctrl+Shift+E ] [—]  │
│ + Add shortcut                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

Selector rules:

- `Automatic` appears first.
- The 32 usable explicit locales follow alphabetically by display language and region.
- Transcription-ready and broad-coverage locales may be grouped or described with secondary text, but neither tier is presented as unsupported.
- The eight adaptation-only locales are absent.
- Type-ahead search matches display name and locale code (`Spanish`, `es-ES`).
- The saved value is the stable locale code, never the display label or list index.

## Vocabulary page — Personal

<!--
Sample rows below communicate density and row behavior only. They are not a built-in software profile.
The real view binds to the user's locally stored vocabulary.
-->

The drawing shows the final Stage 4 surface. Stage 3 first ships the personal phrase/scope/influence editor without the Personal/Installed packs navigation, Import action, export checkboxes, or Export action. Stage 4 adds those controls without changing Stage 3 editing semantics.

```text
┌────────────────────────────────────────────────────────────────────────┐
│ Vocabulary                                                             │
│ Help the local model recognize names, acronyms, and specialist terms.  │
│                                                                        │
│ [ Personal ]  [ Installed packs ]              [ Import pack… ]       │
│                                                                        │
│ Vocabulary scope                                                       │
│ [ Shared across languages                                         ▾ ]  │
│ Used with every recognition language.                                  │
│                                                                        │
│   Phrase                                    Influence                   │
│ ┌─┬────────────────────────────────────┐   ┌───────────────────────┐   │
│ │☐│ MVVM                               │   │ Normal              ▾ │ — │
│ └─┴────────────────────────────────────┘   └───────────────────────┘   │
│ ┌─┬────────────────────────────────────┐   ┌───────────────────────┐   │
│ │☑│ PrivateType                        │   │ Strong              ▾ │ — │
│ └─┴────────────────────────────────────┘   └───────────────────────┘   │
│                                                                        │
│ + Add phrase                                [ Export selected… ]       │
│                                                                        │
│ Terms remain local unless you explicitly export selected entries.      │
└────────────────────────────────────────────────────────────────────────┘
```

Vocabulary-scope behavior:

- Available scopes are `Shared across languages` plus the 28 base languages represented by the 32 explicit locales.
- A base-language section applies to all its regional locales: English applies to `en-US` and `en-GB`; Spanish applies to `es-US` and `es-ES`; French applies to `fr-FR` and `fr-CA`; Portuguese applies to `pt-BR` and `pt-PT`.
- Selecting a scope filters the visible rows but does not delete or disable other scopes.
- Empty scopes show the empty state below rather than a blank table.
- Phrases retain their exact Unicode spelling and case.
- Influence is selected per entry as `Low`, `Normal`, or `Strong`; new rows default to `Normal`.
- Saving an exact existing phrase in the same scope updates its influence instead of creating a duplicate.
- The editor never asks for or persists the incorrectly recognized wording.
- Selection checkboxes affect export only; they do not enable/disable recognition entries.
- `Export selected…` is disabled until at least one row in the visible scope is selected. Changing scope clears the selection so hidden entries cannot be exported accidentally.

Empty state:

```text
┌───────────────────────────────────────────────────────────────┐
│ No Polish vocabulary yet.                                    │
│ Add a word or phrase only when the model repeatedly gets it   │
│ wrong. Shared terms are already applied to Polish dictation.  │
│                                                               │
│ [ Add phrase ]                                                │
└───────────────────────────────────────────────────────────────┘
```

Validation behavior:

- Validation is inline and keeps focus near the invalid row.
- Blank phrases, line breaks/control characters, over-limit values, and exact duplicates in one scope cannot be saved.
- Hitting a vocabulary count or payload budget explains the limit without truncating or silently discarding entries.
- Removing a row is reversible until `Save changes` is pressed.

## Vocabulary page — Installed packs

```text
┌────────────────────────────────────────────────────────────────────────┐
│ Vocabulary                                                             │
│ Help the local model recognize names, acronyms, and specialist terms.  │
│                                                                        │
│ [ Personal ]  [ Installed packs ]              [ Import pack… ]       │
│                                                                        │
│ ┌────────────────────────────────────────────────────────────────────┐ │
│ │ ☑ Software development                                             │ │
│ │   English · 24 entries                                             │ │
│ │                             [Edit] [Export…] [Remove…]              │ │
│ └────────────────────────────────────────────────────────────────────┘ │
│ ┌────────────────────────────────────────────────────────────────────┐ │
│ │ ☐ Product names                                                    │ │
│ │   Shared across languages · 8 entries                              │ │
│ │                             [Edit] [Export…] [Remove…]              │ │
│ └────────────────────────────────────────────────────────────────────┘ │
│                                                                        │
│ Enabled packs can influence future recognition.                        │
└────────────────────────────────────────────────────────────────────────┘
```

Installed-pack behavior:

- The leading checkbox enables or disables the complete pack. Disabled packs remain editable and exportable but never contribute recognition contexts.
- Each pack has one editable local name and one Shared/base-language scope applying to all its entries.
- `Edit` opens the same phrase/influence row editor used for personal vocabulary, plus editable Name and Scope fields. Saving changes the installed local collection directly; there is no upstream link, version, update, or fork.
- Installed names are ordinally unique. Rename validation remains inline and never exposes phrase contents in an error.
- `Remove…` requires confirmation naming the pack and removes only the installed collection after Settings is successfully saved. It never deletes or modifies the file originally imported.
- Empty state explains that packs are manually downloaded or received, then imported from a local file. It contains no online gallery or download action.

## Import pack preview

```text
┌────────────────────────────────────────────────────────────────────────┐
│ Import vocabulary pack                                             ×  │
├────────────────────────────────────────────────────────────────────────┤
│ File: software-development.privatetype-vocabulary.json                 │
│                                                                        │
│ Local pack name                                                        │
│ [ Software development                                               ] │
│ Scope                            Enabled after import                    │
│ [ English                    ▾ ] [✓]                                   │
│                                                                        │
│ 24 valid entries · omitted weights use Normal                          │
│ ┌────────────────────────────────────────────────────────────────────┐ │
│ │ MVVM                                                     Strong    │ │
│ │ dependency injection                                     Normal    │ │
│ │ array                                                    Low       │ │
│ │ …                                                                  │ │
│ └────────────────────────────────────────────────────────────────────┘ │
│                                                                        │
│ This copies the reviewed entries locally. It does not link or update.  │
│                                                [Cancel] [Import pack]  │
└────────────────────────────────────────────────────────────────────────┘
```

Import behavior:

- The file picker accepts `.privatetype-vocabulary.json`; choosing a file performs a bounded read and validation before this preview opens.
- The preview lists every normalized phrase and effective Low/Normal/Strong weight. It never silently skips, repairs, or truncates entries.
- The filename supplies the proposed editable local name. If that name is installed already, suggest a unique suffix such as `Software development (2)` and keep confirmation disabled until the name is valid.
- Scope must be explicitly selected; no scope exists inside the file. Enabled defaults on but remains user-selectable.
- Cancel and window close make no settings changes. `Import pack` commits one complete candidate through the normal atomic Settings save.
- Invalid JSON, encoding, fields, weights, duplicates, or limits show a content-free error with no partial preview or installation.

## Export selection and preview

Personal export begins with the checked rows from the currently visible scope. Installed-pack export includes the complete selected pack. Both routes then show the exact outgoing array before opening the destination picker:

```text
┌────────────────────────────────────────────────────────────────────────┐
│ Export vocabulary pack                                             ×  │
├────────────────────────────────────────────────────────────────────────┤
│ 2 entries will be written. Scope and pack settings are not included.   │
│                                                                        │
│ ┌────────────────────────────────────────────────────────────────────┐ │
│ │ [                                                                ]│ │
│ │ Exact formatted JSON array appears in this read-only preview.      │ │
│ │ [                                                                ]│ │
│ └────────────────────────────────────────────────────────────────────┘ │
│                                                                        │
│                                       [Cancel] [Choose destination…]   │
└────────────────────────────────────────────────────────────────────────┘
```

Export behavior:

- The preview contains only `phrase` and symbolic `weight`; no scope, enabled state, name, settings, transcript, IDs, versions, provenance, attribution, or license metadata.
- `weight` may be omitted only when its effective value is Normal; Low and Strong are always explicit. Serialization is deterministic.
- Cancel/window close writes nothing. Destination failure leaves an existing file intact and keeps the preview available for retry.
- The default filename ends with `.privatetype-vocabulary.json`; the user controls the destination and may rename it.

## Bubble menu

The bubble context menu adds two actions above Settings.

```text
┌──────────────────────────────┐
│ Vocabulary…                  │
│ Teach from last dictation…   │  disabled when no ephemeral result exists
├──────────────────────────────┤
│ Settings…                    │
│ Quit                         │
└──────────────────────────────┘
```

Behavior:

- `Vocabulary…` opens Settings on the Vocabulary page.
- `Teach from last dictation…` is enabled only after a non-empty dictation has finalized.
- The item becomes disabled as soon as the next dictation begins, after the teach dialog is saved or dismissed, and when PrivateType shuts down.
- Opening the menu must not reveal transcript text.
- Both actions have automation names and keyboard access.

## Teach-from-last-dictation window

This is a separate focused dialog. It uses only the one ephemeral transcript already held in memory; it does not query a history store because no history store exists.

```text
┌────────────────────────────────────────────────────────────────────────┐
│ PrivateType — teach vocabulary                                     ×  │
├────────────────────────────────────────────────────────────────────────┤
│ Teach from last dictation                                               │
│ Select the first and last words of the mistake.                         │
│ This sentence is discarded when you close this window.                  │
│                                                                         │
│ [The] [app] [uses] [model] [view] [view] [model] [for] [the] [screen.] │
│                  ╰──────── selected contiguous range ────────╯          │
│                                                                         │
│ Desired word or phrase                                                  │
│ [ MVVM                                                               ] │
│                                                                         │
│ Save for                         Influence                               │
│ [ English                       ▾ ] [ Normal                          ▾ ] │
│                                                                         │
│ The already typed sentence will not be changed.                         │
│                                                    [Cancel] [Save term] │
└────────────────────────────────────────────────────────────────────────┘
```

Selection contract:

1. Each Unicode word span is a keyboard-focusable toggle/chip; punctuation remains visually attached to its word where practical.
2. The first click sets the selection anchor and selects one word.
3. Clicking another word selects the complete contiguous range between the anchor and that word.
4. Clicking a new word after a completed range starts a new one-word selection; `Escape` clears the selection.
5. Keyboard users can set the anchor with `Space` and extend the range with `Shift+Left` / `Shift+Right` or an equivalent documented, tested interaction.
6. The selected heard text may prefill the desired-term field, but the heard text is never saved.
7. The scope is preselected from the dictation's base language; Automatic preselects Shared. The user can always choose another scope.
8. Influence defaults to Normal and remains user-selectable.
9. `Save term` is disabled until a range and valid desired term exist.
10. Saving affects future dictations only. It does not copy text to the clipboard or modify text already injected into another application.
11. Saving always adds or updates personal vocabulary in the chosen scope; quick teach never edits an installed pack.

Privacy and failure behavior:

- Opening the dialog suspends dictation hotkeys, matching the existing Settings behavior.
- Successful save atomically updates settings, clears the ephemeral transcript, closes the dialog, and restores hotkeys.
- Cancel, window close, or explicit dismissal clears the ephemeral transcript and restores hotkeys.
- A persistence failure keeps the dialog and ephemeral transcript available for retry, shows a local error without transcript contents, and does not partially update in-memory settings.
- Transcript text, selected heard text, and desired terms never enter diagnostics, exception messages, telemetry, screenshots, or committed test fixtures.

## Required rendered states

The implementation must extend `tests/PrivateType.App.LayoutProbe` and visually inspect all affected states by their owning plan stage:

Stage 2:

1. General Settings with the 32-locale selector closed and open.

Stage 3:

1. Personal vocabulary: empty Shared scope.
2. Personal vocabulary: populated base-language scope with long Unicode phrases.
3. Personal vocabulary: inline validation and maximum-content scrolling.

Stage 4:

1. Final Personal view: populated scope with no export selection.
2. Final Personal view: selected long Unicode phrases with Export enabled.
3. Installed packs: empty state.
4. Installed packs: mixed enabled/disabled cards and maximum-content scrolling.
5. Installed pack editor: renamed/scoped content and inline validation.
6. Import preview: valid file with omitted/default and explicit weights.
7. Import preview: name collision and content-free validation-error states.
8. Export preview: selected personal entries and complete installed-pack variants.
9. Pack removal confirmation and injected persistence-error state.

Stage 5:

1. Bubble menu with Teach disabled.
2. Bubble menu with Teach enabled.
3. Teach dialog with no selection.
4. Teach dialog with one selected word.
5. Teach dialog with a selected multi-word range and edited desired phrase.
6. Teach dialog persistence-error state with no sensitive text in the error.

Every state must be checked at the repository-supported DPI/text scales and with keyboard-only navigation, visible focus, readable contrast, no clipped controls, and no debug/sample data left in production.
