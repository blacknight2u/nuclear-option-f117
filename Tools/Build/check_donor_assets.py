"""Check that the pinned donor contains every external asset used by F117Builder."""
import json
import sys
from pathlib import Path
import UnityPy

bundle = UnityPy.load(sys.argv[1])
lock = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
names = {name.lower() for name in bundle.container}
missing = set(lock["donorBundle"]["requiredAssets"]) - names
if missing:
    raise SystemExit("Missing donor assets: " + ", ".join(sorted(missing)))
print(f"PASS: all {len(lock['donorBundle']['requiredAssets'])} required donor assets recovered")
