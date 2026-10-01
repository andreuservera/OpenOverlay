#!/usr/bin/env bash
# Checks CHANGELOG.md against what the build, the app and the release workflow read from it.
# Usage: check-changelog.sh [path/to/CHANGELOG.md]. Exits 1 on errors; warnings are advice.
set -euo pipefail

file="${1:-$(cd "$(dirname "$0")/../../../.." && pwd)/CHANGELOG.md}"
[[ -f "$file" ]] || { echo "check-changelog: $file not found" >&2; exit 2; }

exec awk -v today="$(date +%F)" '
BEGIN {
    split("Breaking Changes|Added|Improved|Changed|Fixed|Reliability|Refactored|Removed", names, "|")
    for (i in names) known[names[i]] = 1
    usual = "Breaking Changes, Added, Improved, Changed, Fixed, Reliability, Refactored, Removed"
}

function problem(at, kind, msg) {
    printf "%s:%d: %s: %s\n", FILENAME, at, kind, msg
    if (kind == "error") errors++; else warnings++
}

function trim(s) { sub(/^[ \t]+/, "", s); sub(/[ \t]+$/, "", s); return s }

function cmpnum(a, b) { return (a + 0 > b + 0) - (a + 0 < b + 0) }

# SemVer precedence of X.Y.Z[-label]: negative, zero or positive.
function cmpver(a, b,    la, lb, x, y, i, n, m, d) {
    la = index(a, "-") ? substr(a, index(a, "-") + 1) : ""
    lb = index(b, "-") ? substr(b, index(b, "-") + 1) : ""
    split(la == "" ? a : substr(a, 1, index(a, "-") - 1), x, ".")
    split(lb == "" ? b : substr(b, 1, index(b, "-") - 1), y, ".")
    for (i = 1; i <= 3; i++) if ((d = cmpnum(x[i], y[i])) != 0) return d
    if (la == lb) return 0
    if (la == "") return 1
    if (lb == "") return -1
    n = split(la, x, "."); m = split(lb, y, ".")
    for (i = 1; i <= n && i <= m; i++) {
        if (x[i] == y[i]) continue
        if (x[i] ~ /^[0-9]+$/ && y[i] ~ /^[0-9]+$/) return cmpnum(x[i], y[i])
        if (x[i] ~ /^[0-9]+$/) return -1
        if (y[i] ~ /^[0-9]+$/) return 1
        return x[i] < y[i] ? -1 : 1
    }
    return (n > m) - (n < m)
}

function open_entry(name) {
    in_entry = 1; entry = name; entry_line = FNR; entry_items = 0; summary = 0; rule_seen = 0
    section_open = 0; has_parent = 0
    split("", seen)
}

function close_section() {
    if (section_open && section_items == 0) problem(section_line, "warning", "\"" section "\" has no bullets; the app hides it")
    section_open = 0
}

function close_entry() {
    close_section()
    if (!in_entry) return
    if (entry == "[Unreleased]") unreleased_items = entry_items
    else if (entry_items == 0 && !summary) problem(entry_line, "error", entry " has no changes")
    in_entry = 0
}

function separate(next_name) {
    if (in_entry && !rule_seen) problem(FNR, "warning", "put a --- line between " entry " and " next_name)
}

function finish_bullet() {
    if (open_line && open_text !~ /[.:!?]$/) problem(open_line, "warning", "end the bullet with a period (a colon if items follow)")
    open_line = 0; open_text = ""
}

function version_heading(    s, v, d, part, c) {
    if (line !~ /^#+[ \t]/) {
        problem(FNR, "error", "put a space after the #s; without it neither the build nor the app sees this version")
        return
    }
    s = line; sub(/^#+[ \t]+\[?v?/, "", s)
    match(s, /^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?/)
    v = substr(s, 1, RLENGTH)
    # The build takes its version from the first ## or ### version heading.
    if (build_version == "" && line ~ /^###?[ \t]/) build_version = v
    if (line !~ /^### \[[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?\] - [0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9][ \t]*$/)
        problem(FNR, "error", "write version headings exactly as \"### [X.Y.Z] - YYYY-MM-DD\"")
    d = match(line, /[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]/) ? substr(line, RSTART, RLENGTH) : ""
    close_section()
    separate(v)
    close_entry()
    if (d != "") {
        split(d, part, "-")
        if (part[2] + 0 < 1 || part[2] + 0 > 12 || part[3] + 0 < 1 || part[3] + 0 > 31) problem(FNR, "error", d " is not a date")
        else if (d > today) problem(FNR, "warning", v " is dated in the future")
    }
    if (last_version != "") {
        c = cmpver(v, last_version)
        if (c == 0) problem(FNR, "error", v " is listed twice")
        else if (c > 0) problem(FNR, "error", v " is newer than " last_version " above it; newest goes first")
        else if (d != "" && last_date != "" && d > last_date) problem(FNR, "warning", v " is dated after " last_version " above it")
    }
    last_version = v; last_date = d
    open_entry(v)
}

function unreleased_heading() {
    if (line !~ /^### \[Unreleased\][ \t]*$/) problem(FNR, "warning", "write it as \"### [Unreleased]\"")
    if (last_version != "") problem(FNR, "error", "[Unreleased] must be above every version; below one it hides what follows")
    close_section()
    separate("[Unreleased]")
    close_entry()
    open_entry("[Unreleased]")
}

function section_heading(    title) {
    title = line; sub(/^####[ \t]+/, "", title); sub(/[ \t#]+$/, "", title)
    if (!in_entry) { problem(FNR, "warning", "section outside any version; the app ignores it"); return }
    close_section()
    if (!(title in known)) problem(FNR, "warning", "unusual section \"" title "\"; the usual ones are " usual)
    if (title in seen) problem(FNR, "warning", "\"" title "\" appears twice in " entry "; merge them, the app shows both")
    seen[title] = 1
    section = title; section_open = 1; section_items = 0; section_line = FNR; has_parent = 0
}

function other_heading() {
    if (line ~ /^#####/) problem(FNR, "warning", "use #### for sections")
    else if (in_entry) {
        problem(FNR, "warning", "this heading ends " entry " in the app; what follows is hidden until the next version")
        close_entry()
    }
}

function rule() {
    if (prev != "blank") problem(FNR, "warning", "leave a blank line above ---")
    if (in_entry) rule_seen = 1
}

function bullet(    indent, text) {
    match(line, /^[ \t]*/); indent = RLENGTH
    text = trim(substr(line, indent + 2))
    if (!in_entry) { problem(FNR, "warning", "bullet outside any version; the app ignores it"); return }
    if (index(substr(line, 1, indent), "\t")) problem(FNR, "warning", "indent with spaces, not tabs")
    if (substr(line, indent + 1, 1) != "-") problem(FNR, "warning", "use \"-\" for bullets")
    if (text == "") { problem(FNR, "error", "empty bullet"); return }
    if (indent < 2) {
        if (indent == 1) problem(FNR, "warning", "one space reads as a top-level bullet; nest with exactly two")
        if (!section_open) problem(FNR, "warning", "bullet outside a #### section; the app lists it under CHANGES")
        entry_items++; section_items++; has_parent = 1
        open_line = FNR; open_text = text; nested = 0
    } else {
        if (!has_parent) problem(FNR, "warning", "nested bullet without a bullet above it")
        if (indent != 2) problem(FNR, "warning", "nest with exactly two spaces; the app shows a single level")
        nested = 1
    }
}

function text_line() {
    if (!in_entry) return
    if (!section_open) { summary = 1; return }
    problem(FNR, "warning", "text inside a section; write it as a bullet")
}

{
    sub(/\r$/, "")
    line = $0
    if (line ~ /^[ \t]*$/)                                   kind = "blank"
    else if (line ~ /^[ \t]*---+[ \t]*$/)                     kind = "rule"
    else if (line ~ /^[ \t]*[-*+]([ \t]|$)/)                  kind = "bullet"
    else if (line ~ /^#+[ \t]*\[?v?[0-9]+\.[0-9]+\.[0-9]+/)   kind = "version"
    else if (line ~ /^#+[ \t]*\[[Uu]nreleased\]/)             kind = "unreleased"
    else if (line ~ /^####[ \t]/)                             kind = "section"
    else if (line ~ /^#+[ \t]/)                               kind = "heading"
    else if (prev == "bullet" || prev == "continuation")      kind = "continuation"
    else                                                      kind = "text"

    if (kind == "continuation") { if (!nested && open_line) open_text = open_text " " trim(line) }
    else finish_bullet()

    if (kind == "rule") rule()
    else if (kind == "bullet") bullet()
    else if (kind == "version") version_heading()
    else if (kind == "unreleased") unreleased_heading()
    else if (kind == "section") section_heading()
    else if (kind == "heading") other_heading()
    else if (kind == "text") text_line()
    prev = kind
}

END {
    finish_bullet()
    close_entry()
    if (build_version == "") problem(FNR, "error", "no \"### [X.Y.Z] - YYYY-MM-DD\" heading; the build needs one for its version")
    else printf "Build version: %s\n", build_version
    if (unreleased_items) printf "[Unreleased]: %d change(s) not in a version yet\n", unreleased_items
    printf "%d error(s), %d warning(s)\n", errors, warnings
    exit errors > 0
}
' "$file"
