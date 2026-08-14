# Contributing to PrivateType

Thanks for helping improve PrivateType. Small, reproducible changes are easiest
to review and safest for a desktop speech-input tool.

## Before opening an issue

- Check existing [issues](https://github.com/kkolodziejczak/privatetype/issues).
- Describe the Windows version, PrivateType version, and a minimal sequence of
  actions that reproduces the problem.
- Never paste dictated text, raw audio, microphone names, settings files, or
  unreviewed diagnostic reports. Redact screenshots carefully.

## Before opening a pull request

1. Discuss substantial changes in an issue first.
2. Keep the change focused and preserve the local-only privacy model.
3. Run the relevant tests. UI changes also require the layout probe and visual
   inspection of each affected state.
4. Update user-facing documentation and the release checks when behavior,
   packaging, or requirements change.
5. Do not add speech models, generated recordings, credentials, or private
   diagnostic data to the repository.

The portable release workflow builds the pinned native engine, tests the .NET
projects, creates the ZIP, and verifies its required payload. Changes to native
dependencies, build flags, model handling, or notices require a fresh release
license audit.
