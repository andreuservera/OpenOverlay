# Security

## Reporting a vulnerability

Please report security problems privately, not in a public issue:

- On GitHub, open the repository's **Security** tab and choose **Report a vulnerability**
  (private vulnerability reporting), or
- open a minimal public issue asking for a private contact, without any details of the problem.

Say what you found, which version (Help › About, or the status bar), and how to reproduce it. You
should hear back within a week. Fixes ship in the next release, and the changelog credits the
report unless you'd rather it didn't.

## Supported versions

Only the latest release gets fixes. The app updates itself from GitHub Releases, so most people are
on it within a day of a release.

## What the app does and doesn't do

These are the boundaries a report would cross:

- **iRacing**: OpenOverlay only reads iRacing's shared memory, read-only. It never writes to it and
  never sends anything to iRacing. The one exception is **Replay a session** (Diagnostics), which
  writes an .ibt recording into that shared memory, and only while iRacing is closed.
- **Network**: the only connection the app makes is the update check and download from this
  repository's GitHub Releases (Velopack). Telemetry, laps, coaching reports, logs and settings
  never leave the PC. Spoken coaching uses the offline Windows voices.
- **Files**: everything is kept under `%LOCALAPPDATA%\IRacingOverlay` (settings, layouts, logs,
  coaching data, learned track maps). Exported layouts and diagnostics reports are written only
  where you choose. A diagnostics report contains logs and settings, which name your iRacing
  sessions and drivers; nothing in it is a password or token.
- **Imported files**: layouts (.json), telemetry (.ibt) and lap files (.oolap) are read as data,
  never run. A file that crashes or hangs the app when imported is a bug worth
  reporting.
- **Installer and updates**: releases are built by GitHub Actions from a tag on this repository
  (see `.github/workflows/release.yml`). When a code-signing certificate is configured for the
  repository, the installer and the app are signed with it; until then Windows SmartScreen may
  warn on first install.
