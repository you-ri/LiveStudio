#!/usr/bin/env bash
# Merge a single version entry into an existing VPM repository JSON (vpm.json).
#
# Usage:
#   Scripts/MergeVpmIndex.sh <existing_vpm.json> <package.json> <zip_url> <zip_sha256> > out.json
#
# Arguments:
#   existing_vpm.json    Path to current vpm.json (or an empty-skeleton file
#                        if this is the first publish). Read but not modified.
#   package.json         The package's package.json at the version being
#                        published. Its full contents become the version entry.
#   zip_url              Absolute https URL where the zipped package can be
#                        downloaded (typically a GitHub Release asset URL).
#   zip_sha256           SHA-256 of the zip file (lowercase hex string).
#
# Output:
#   Merged vpm.json is written to stdout.
#
# Behaviour notes:
# - The version entry mirrors the package.json contents and adds the "url" and
#   "zipSHA256" fields, matching the VPM repository schema VCC consumes.
# - Re-publishing the same <name, version> pair overwrites the existing entry.
# - Older versions are preserved.
set -euo pipefail

if [ "$#" -ne 4 ]; then
  echo "usage: $0 <existing_vpm.json> <package.json> <zip_url> <zip_sha256>" >&2
  exit 2
fi

EXISTING_INDEX="$1"
PACKAGE_JSON="$2"
ZIP_URL="$3"
ZIP_SHA256="$4"

for f in "$EXISTING_INDEX" "$PACKAGE_JSON"; do
  if [ ! -r "$f" ]; then
    echo "cannot read: $f" >&2
    exit 2
  fi
done

PKG_NAME=$(jq -r '.name // empty' "$PACKAGE_JSON")
PKG_VERSION=$(jq -r '.version // empty' "$PACKAGE_JSON")

if [ -z "$PKG_NAME" ]; then
  echo "package.json missing .name" >&2
  exit 2
fi
if [ -z "$PKG_VERSION" ]; then
  echo "package.json missing .version" >&2
  exit 2
fi

# Build the version entry: full package.json contents plus url + zipSHA256.
VERSION_ENTRY=$(jq \
  --arg url "$ZIP_URL" \
  --arg sha "$ZIP_SHA256" \
  '. + {url: $url, zipSHA256: $sha}' \
  "$PACKAGE_JSON")

# Merge into the existing index (jq auto-creates intermediate objects).
jq \
  --arg pkg "$PKG_NAME" \
  --arg ver "$PKG_VERSION" \
  --argjson entry "$VERSION_ENTRY" \
  '.packages[$pkg].versions[$ver] = $entry' \
  "$EXISTING_INDEX"
