# Contributing to OpenOverlay

Thanks for considering contributing — this project is fully open source and PRs, issues, and
ideas from anyone are welcome.

## Getting set up

1. Fork the repo and clone your fork.
2. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
3. Build and run the tests:
   ```powershell
   dotnet build
   dotnet test
   ```
   The test suite doesn't need iRacing installed — the SDK tests build a synthetic copy of
   iRacing's shared-memory layout in-memory to verify parsing logic.
4. To try the app itself against live data, you do need iRacing installed and a session loaded
   (practice/qualify/race/replay — the main menu alone has no live telemetry to read). See the
   [README](README.md) for the borderless/windowed display-mode setup, which is required for any
   overlay to render on top of the sim.

## Making a change

- Keep pull requests focused — one feature or fix per PR is much easier to review than a bundle of
  unrelated changes.
- Add or update unit tests for any change to `IRacingOverlay.Sdk` or the `ViewModels`/`Builder`
  classes in `IRacingOverlay.App` — that's where nearly all the actual logic lives, and it's the
  part that's testable without a live sim.
- Run `dotnet test` before opening a PR and make sure everything passes.
- If you're changing telemetry parsing, note where you got the variable name/behavior from (the
  [iRacing SDK docs](https://sajax.github.io/irsdkdocs/), a live capture, or another open-source
  reader) — iRacing's own documentation is incomplete in places, and some variable types/behaviors
  in this codebase were confirmed empirically against a running session rather than from official
  docs. Flag anything you're not 100% sure about so it's easy to verify later.

## Reporting bugs / requesting features

Open an issue with:
- What you expected vs. what happened.
- Whether it's single-class or multiclass, oval or road course, if relevant (a few things — like
  proximity/spotter data — come from iRacing's own telemetry and behave differently in different
  session types).
- Your iRacing session type (practice/qualify/race) and car, if the issue is telemetry-related.

## Code style

- No enforced style tool yet — just match the surrounding code (nullable-enabled, minimal
  comments, existing naming conventions).
- Comments should explain *why*, not *what* — especially for anything non-obvious about iRacing's
  telemetry quirks, which make up most of the tricky bits in this codebase.

## License

By contributing, you agree your contributions are licensed under the project's [MIT
License](LICENSE).
