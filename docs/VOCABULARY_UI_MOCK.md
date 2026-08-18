# Vocabulary and language UI mock

## Status and authority

This is the interaction template for the PrivateType vocabulary and expanded-language feature.

- **Authoritative:** navigation, field meanings, available actions, selection behavior, privacy text, enabled/disabled states, and keyboard/accessibility expectations.
- **Directional:** exact pixel sizes, wrapping, and spacing. The implementation must reuse PrivateType's existing colors, typography, rounded surfaces, and frameless-window treatment, then pass the repository layout probe and UI-quality verification.
- **Sample data only:** `MVVM`, `PrivateType`, `PostgreSQL`, and the example transcript. Do not ship these as built-in vocabulary and do not use real dictated text in fixtures, screenshots, documentation, or diagnostics.
- **Implementation boundary:** this mock does not authorize transcript history, correction-pair replacement, automatic rewriting of injected text, named profiles, import/export, or UI localization.

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

## Vocabulary page

<!--
Sample rows below communicate density and row behavior only. They are not a built-in software profile.
The real view binds to the user's locally stored vocabulary.
-->

```text
┌────────────────────────────────────────────────────────────────────────┐
│ Vocabulary                                                             │
│ Help the local model recognize names, acronyms, and specialist terms.  │
│                                                                        │
│ Vocabulary scope                                                       │
│ [ Shared across languages                                         ▾ ]  │
│ Used with every recognition language.                                  │
│                                                                        │
│ Phrase                                      Influence                   │
│ ┌──────────────────────────────────────┐   ┌───────────────────────┐   │
│ │ MVVM                                 │   │ Normal              ▾ │ — │
│ └──────────────────────────────────────┘   └───────────────────────┘   │
│ ┌──────────────────────────────────────┐   ┌───────────────────────┐   │
│ │ PrivateType                          │   │ Strong              ▾ │ — │
│ └──────────────────────────────────────┘   └───────────────────────┘   │
│                                                                        │
│ + Add phrase                                                           │
│                                                                        │
│ Terms remain on this computer in settings.json.                        │
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

Privacy and failure behavior:

- Opening the dialog suspends dictation hotkeys, matching the existing Settings behavior.
- Successful save atomically updates settings, clears the ephemeral transcript, closes the dialog, and restores hotkeys.
- Cancel, window close, or explicit dismissal clears the ephemeral transcript and restores hotkeys.
- A persistence failure keeps the dialog and ephemeral transcript available for retry, shows a local error without transcript contents, and does not partially update in-memory settings.
- Transcript text, selected heard text, and desired terms never enter diagnostics, exception messages, telemetry, screenshots, or committed test fixtures.

## Required rendered states

The implementation must extend `tests/PrivateType.App.LayoutProbe` and visually inspect all affected states:

1. General Settings with the 32-locale selector closed and open.
2. Vocabulary page: empty Shared scope.
3. Vocabulary page: populated Shared scope.
4. Vocabulary page: populated base-language scope with long Unicode phrases.
5. Vocabulary page: inline validation and maximum-content scrolling.
6. Bubble menu with Teach disabled.
7. Bubble menu with Teach enabled.
8. Teach dialog with no selection.
9. Teach dialog with one selected word.
10. Teach dialog with a selected multi-word range and edited desired phrase.
11. Teach dialog persistence-error state with no sensitive text in the error.

Every state must be checked at the repository-supported DPI/text scales and with keyboard-only navigation, visible focus, readable contrast, no clipped controls, and no debug/sample data left in production.
