#!/usr/bin/env bash
#
# Prints the section of docs/RELEASE-NOTES.md for one version, for the GitHub release body.
#
#   bash tools/release-notes.sh 1.0.0 > notes.md
#
# A section starts at a "## <version>" heading (optionally "## [1.0.0]" and/or followed by a date)
# and ends at the next "## " heading. Exits 1 if there is no such section or it is empty, so a
# release cannot go out with blank notes.

set -euo pipefail

version="${1:-}"
[[ -n "$version" ]] || { echo "usage: release-notes.sh <version> [notes-file]" >&2; exit 2; }
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
file="${2:-$script_dir/../docs/RELEASE-NOTES.md}"
[[ -f "$file" ]] || { echo "no such file: $file" >&2; exit 2; }

notes="$(awk -v v="$version" '
    /^## / {
        if (found) exit
        heading = $2
        gsub(/[\[\]]/, "", heading)
        if (heading == v) { found = 1; next }
    }
    found { print }
' "$file")"

if ! grep -q '[^[:space:]]' <<< "$notes"; then
    echo "docs/RELEASE-NOTES.md has no notes for $version: add a \"## $version\" section" >&2
    exit 1
fi

printf '%s\n' "$notes"
