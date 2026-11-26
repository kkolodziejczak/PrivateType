# PrivateType agent guide

## Relaunch before requesting user input

When a change affects PrivateType's executable behavior or rendered UI and you need the user to test, review, or answer a question about that change, always relaunch the application first. The user must never be asked to test stale binaries.

Use this sequence after the relevant build and tests pass:

1. Resolve the exact `PrivateType` process with `Get-Process`; do not use a broad process name, wildcard, or `taskkill`.
2. Stop only that resolved process. If it is not running, continue without error.
3. Start the current built executable:
   `src\PrivateType.App\bin\Debug\net10.0-windows\PrivateType.exe`.
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

## Starting the approval for the owner

<!-- Shared with every repository that signs through gh-release-signing; keep it identical to the
     copy in that repository's README ("Instructions for agents"). -->
As soon as you start a release run (push the tag or start the workflow), also start the approval
script for the owner; don't wait for the build, and don't ask them to run it. It follows the run
and prompts the owner when signing needs them. Open it in its own window, from this repository's
folder:

```powershell
Start-Process powershell.exe -WorkingDirectory (Get-Location) -ArgumentList '-NoExit', '-Command', 'gh release-signing approve -Tag vX.Y.Z'
```

- Pass `-RunId <id>` after a rerun, and `-Workflow <file>` when the calling workflow is not
  `release.yml`.
- If `gh extension list` does not show `release-signing`, run `Approve-ReleaseSigning.ps1` from a
  local clone of gh-release-signing instead (`-NoExit -ExecutionPolicy Bypass -File <path> …`),
  still with this repository's folder as the working directory.
- Never answer its prompts or type the code yourself. Tell the owner the window is open, then wait
  for the run to finish in the background and report its result.
