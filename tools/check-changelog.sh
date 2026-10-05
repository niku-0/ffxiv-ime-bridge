#!/usr/bin/env bash
# Fails unless the manifest's Changelog is written for this release in the format
# docs/agents/release.md describes. The release workflow runs it too, but only once the tag is public.
set -euo pipefail
cd "$(dirname "$0")/.."

manifest=src/FfxivImeBridge/FfxivImeBridge.json
changelog=$(jq -r '.Changelog // ""' "$manifest")
previous=$(jq -r '.[0].Changelog // ""' repo.json)
errors=()

if [ -z "$changelog" ]; then
  errors+=("Changelog in $manifest is empty")
elif [ "$changelog" = "$previous" ]; then
  errors+=("Changelog in $manifest is unchanged since the last release (repo.json)")
fi

while IFS= read -r line; do
  [ -z "$line" ] && continue
  case "$line" in
    "- "*) ;;
    *) errors+=("Not a '- ' bullet: $line") ;;
  esac
  if [[ "$line" =~ \#[0-9] ]]; then errors+=("Issue number: $line"); fi
  if [[ "$line" == *"**"* ]]; then errors+=("Markdown bold: $line"); fi
done <<< "$changelog"

if [ ${#errors[@]} -gt 0 ]; then
  printf '::error::%s\n' "${errors[@]}" >&2
  exit 1
fi
echo "Changelog OK"
