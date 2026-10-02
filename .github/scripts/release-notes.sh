#!/usr/bin/env bash
# Extract one version's CHANGELOG.md section for a release.
#
#   release-notes.sh <version> [out-dir]
#
# Writes two files into out-dir (default: the current directory):
#
#   release-notes.md          the section body verbatim, up to the next "## [" heading. It is
#                             the GitHub release's notes, and is empty when the section is
#                             missing.
#   package-release-notes.md  the same text for the NuGet packages' <releaseNotes>, cut at a
#                             line boundary under nuget.org's 35,000 character limit with a
#                             pointer to the release page. Absent when the section is missing
#                             or empty, so the pack keeps the static text of
#                             Directory.Build.props (PackageReleaseNotes) instead of failing.
#
# The packs read it through -p:PackageReleaseNotesFile=<absolute path>.
set -euo pipefail

version="${1:?usage: release-notes.sh <version> [out-dir]}"
out="${2:-.}"
changelog="${CHANGELOG:-CHANGELOG.md}"
# Characters (bytes under mawk, which only over-counts) the package notes may take, leaving
# room under the 35,000 limit for the pointer line below.
max="${PACKAGE_NOTES_MAX:-32000}"

awk -v ver="## [$version]" '
  index($0, ver) == 1 { found=1; next }
  found && /^## \[/ { exit }
  found { print }
' "$changelog" > "$out/release-notes.md"

rm -f "$out/package-release-notes.md"
# A section holding only blank lines counts as missing.
if ! grep -q '[^[:space:]]' "$out/release-notes.md"; then
  echo "No CHANGELOG.md section for $version: the packages keep the static release notes." >&2
  exit 0
fi

# Whole lines only: n is the running size of the lines kept, so the cut never splits a
# multibyte character.
awk -v max="$max" '{ n += length($0) + 1 } n > max { exit } { print }' \
  "$out/release-notes.md" > "$out/package-release-notes.md"

if ! cmp -s "$out/release-notes.md" "$out/package-release-notes.md"; then
  printf '\n(Truncated, the full notes are at https://github.com/Pixnop/Atlas/releases/tag/v%s)\n' \
    "$version" >> "$out/package-release-notes.md"
fi
