#!/bin/bash
# =============================================================================
# mutate-sandbox.sh — Simulate settings loss in the sandbox data dir.
#
# Usage: ./mutate-sandbox.sh <vs-data-path>
#
# Run AFTER exporting a backup and quitting the game. Changes/deletes the
# seeded marker files and forces fieldOfView to 20 so a subsequent import
# has something real to restore.
# =============================================================================
set -eu

DATA="${1:?usage: mutate-sandbox.sh <vs-data-path>}"
[ -d "$DATA" ] || { echo "ERROR: no such data dir: $DATA"; exit 2; }

echo '{ "marker": "CKTEST-MUTATED" }' > "$DATA/ModConfig/cktest.json"
rm -f "$DATA/ModConfig/cktest-subdir/nested.json"
echo '{ "marker": "CKTEST-MACRO-MUTATED" }' > "$DATA/Macros/cktest-macro.json"
echo '[]' > "$DATA/serverlist.json"

if [ -f "$DATA/clientsettings.json" ]; then
    python3 - "$DATA/clientsettings.json" <<'EOF'
import json, sys
path = sys.argv[1]
with open(path) as f:
    cfg = json.load(f)
for section in cfg.values():
    if isinstance(section, dict) and "fieldOfView" in section:
        print(f"fieldOfView: {section['fieldOfView']} -> 20")
        section["fieldOfView"] = 20
with open(path, "w") as f:
    json.dump(cfg, f, indent=2)
EOF
fi

echo "Sandbox mutated:"
echo "  - ModConfig/cktest.json           -> marker changed"
echo "  - ModConfig/cktest-subdir/nested.json -> DELETED"
echo "  - Macros/cktest-macro.json        -> marker changed"
echo "  - serverlist.json                 -> emptied"
echo "  - clientsettings.json             -> fieldOfView forced to 20"
