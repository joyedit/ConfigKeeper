#!/bin/bash
# =============================================================================
# verify-backup.sh — Compare a ConfigKeeper backup zip against a live data dir.
#
# Usage: ./verify-backup.sh <backup.zip> <vs-data-path>
#
# Extracts the zip and byte-compares every entry against the same relative
# path under the data dir. Exit code 0 = everything matched.
# For clientsettings.json it additionally prints fieldOfView from both sides,
# since the game may legitimately rewrite that file with cosmetic changes.
# =============================================================================
set -u

ZIP="${1:?usage: verify-backup.sh <backup.zip> <vs-data-path>}"
DATA="${2:?usage: verify-backup.sh <backup.zip> <vs-data-path>}"

[ -f "$ZIP" ]  || { echo "ERROR: no such zip: $ZIP"; exit 2; }
[ -d "$DATA" ] || { echo "ERROR: no such data dir: $DATA"; exit 2; }

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
unzip -q "$ZIP" -d "$WORK"

ok=0; diff=0; missing=0

while IFS= read -r -d '' file; do
    rel="${file#"$WORK"/}"
    [ "$rel" = "_manifest.json" ] && continue

    live="$DATA/$rel"
    if [ ! -f "$live" ]; then
        echo "MISSING  $rel   (in backup, not on disk)"
        missing=$((missing+1))
    elif cmp -s "$file" "$live"; then
        echo "OK       $rel"
        ok=$((ok+1))
    else
        echo "DIFFERS  $rel"
        diff=$((diff+1))
    fi
done < <(find "$WORK" -type f -print0 | sort -z)

# Semantic spot-check on clientsettings: the game rewrites this file, so a
# byte diff alone can't tell "clobbered" from "cosmetically rewritten".
if [ -f "$WORK/clientsettings.json" ] && [ -f "$DATA/clientsettings.json" ]; then
    fov_zip=$(grep -oP '"fieldOfView"\s*:\s*\K[0-9]+' "$WORK/clientsettings.json" | head -1)
    fov_live=$(grep -oP '"fieldOfView"\s*:\s*\K[0-9]+' "$DATA/clientsettings.json" | head -1)
    echo ""
    echo "fieldOfView: backup=${fov_zip:-?}  live=${fov_live:-?}  $([ "${fov_zip:-x}" = "${fov_live:-y}" ] && echo '(match)' || echo '(MISMATCH)')"
fi

echo ""
echo "Result: $ok identical, $diff differ, $missing missing"
[ "$diff" -eq 0 ] && [ "$missing" -eq 0 ]
