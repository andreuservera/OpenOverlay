---
name: changelog
description: 'Add, edit or release entries in OpenOverlay''s CHANGELOG.md, the only source of the app version, What''s New, the Changelog page, the update notice and GitHub release notes. Use when asked to add something to the changelog, write or fix release notes, bump or tag a version ("tag it as 0.9.0", "release 0.9.0"), prepare a release, or check the changelog format.'
argument-hint: 'What changed, or the version to release (e.g. "release 0.9.0")'
---

# Changelog

`CHANGELOG.md` (repo root) is written by hand and nothing else feeds these:

- **Build version**: the first `### [X.Y.Z] - YYYY-MM-DD` heading (read by `IRacingOverlay.App.csproj`). Adding a version entry changes the version of every build from then on.
- **What's New**: that first entry. **Changelog page**: every entry. **Update notice**: the first four top-level bullets of the entry's first non-breaking section.
- **GitHub release notes**: the first entry's body. `release.yml` fails when the tag isn't `v` + that version, and its tests fail on missing dates, wrong order or duplicate versions.

## Format

```markdown
### [Unreleased]

#### Added
- Collected here until a version is chosen; the build and the app ignore it.

---

### [0.9.0] - 2026-10-15

#### Added
- One change per bullet, written for a driver, ending with a period.
- A change with parts:
  - Part one
  - Part two

#### Fixed
- ...

---

### [0.8.0] - 2026-09-30
```

- Newest first. One entry per version, dated with the release day.
- Section headings are `####`, each at most once per entry, in this order: Breaking Changes, Added, Improved, Changed, Fixed, Reliability, Refactored, Removed. Add to an existing heading instead of repeating it. "Breaking Changes" shows in red and is flagged in the update notice.
- Top-level bullets use `- `; nested bullets use exactly two spaces and only one level.
- Put a `---` line between entries, with blank lines around it.
- The app shows plain text: links keep only their label, and backticks and emphasis are stripped.

## Procedure

1. **Read** the first two entries of `CHANGELOG.md` for the current version and tone.
2. **Choose the entry.**
   - A version was given ("tag it as 0.9.0"): use `### [0.9.0] - <today>`. If it doesn't exist yet, create it at the top, moving any `[Unreleased]` bullets into it.
   - No version was given: find out whether the top entry is released with `git ls-remote --tags origin refs/tags/vX.Y.Z` (use `git tag -l vX.Y.Z` if offline; local tags can be stale).
     - Untagged means it's still being prepared, so add to it.
     - Tagged means it's released and frozen, so add under `### [Unreleased]` and create that section at the top if needed.
   - "Release" without a number: bump the minor version for anything Added, Changed or Breaking, and the patch version for fixes only. State the choice in the reply.
3. **Find what changed.** Git only shows where to look; the wording comes from the code.
   - If the user described the change, that is the scope.
   - Uncommitted work: `git status --short`, `git diff --stat`, then `git diff -- <path>`.
   - Since the top entry was committed: `git log -1 --format=%h -S'### [X.Y.Z]' -- CHANGELOG.md`, then `git log --oneline <hash>..HEAD` and `git diff --stat <hash>..HEAD`.
4. **Confirm in the code** what the change does and what the driver sees it called. Take the names from widget titles in `ControlPanel/WidgetCatalog.cs` (`WidgetCatalog.All`), page titles in `NavItem.ForPage(...)` calls, and setting labels in `ControlPanel/ControlPanelSchema.cs` and the XAML.
5. **Write the bullets.**
   - Describe the effect for a driver ("Fuel calculator rounds up to whole laps."), not the implementation.
   - Put the most important bullet first in each section. The first four of the first section become the update notice, so keep a "Parent:" bullet with nested parts out of those four.
   - Developer-only changes go briefly under Changed or Refactored. Skip test-only and doc-only churn.
   - Never paste commit subjects or PR titles. Never add a bullet the code doesn't back up.
6. **Edit only what the task needs.** Released entries are history: don't reword, reorder or merge them unless asked. When a prepared entry is released on a later day, update its date.
7. **Check** from the repo root:
   - `bash .github/skills/changelog/scripts/check-changelog.sh` must report 0 errors. Fix any warnings you introduced; mention warnings that were already there.
   - `dotnet msbuild IRacingOverlay.App/IRacingOverlay.App.csproj -getProperty:Version` must print the expected version. From WSL, run `~/.dotnet/dotnet` and add `-p:EnableWindowsTargeting=true`.
8. **Don't commit, tag or push** unless asked. After adding a version, remind the user that a release starts from a tag on a commit that contains the entry. Once the entry is committed on `main`, they run `git tag vX.Y.Z && git push origin main vX.Y.Z`.

## Reply

Keep the reply short. Give:
- the entry and version you edited, and whether the build version changed;
- the bullets you added;
- any checker warnings;
- the release commands, when a version was added.
