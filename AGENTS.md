# PrivateType agent guide

## Relaunch before requesting user input

When a change affects PrivateType's executable behavior or rendered UI and you need the user to test, review, or answer a question about that change, always relaunch the application first. The user must never be asked to test stale binaries.

Use this sequence after the relevant build and tests pass:

1. Resolve the exact `PrivateType` process with `Get-Process`; do not use a broad process name, wildcard, or `taskkill`.
2. Stop only that resolved process. If it is not running, continue without error.
3. Start the current built executable:
   `src\PrivateType.App\bin\Debug\net8.0-windows\PrivateType.exe`.
   For a release-validation task, start the executable in the release folder instead.
4. Confirm the new `PrivateType` process exists before asking for user input.
5. Tell the user the app was relaunched and that the requested behavior is ready to test.

Do not stop the app merely to provide a code-only explanation. Do stop it whenever the next user input is intended to validate a change.

## Verification

- Run the affected core and Windows test projects.
- For rendered UI changes, run `tests\PrivateType.App.LayoutProbe` and inspect every affected state.
- Keep the model unloaded unless the specific test needs live recognition. The first held shortcut intentionally loads it on demand.
- Treat microphone audio, dictated text, settings, and diagnostic logs as private. Do not copy their contents into documentation, test fixtures, or prompts.

## Documentation

Keep [README.md](README.md) user-first. Update its measured model/runtime figures whenever the pinned model or package changes. Keep implementation and contributor guidance below the user instructions, rather than replacing them.
