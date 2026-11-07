"""Validate the exact authored asset set, including materialized Git LFS files."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
INVENTORY = ROOT / "Build/source-assets.json"


def asset_paths():
    assets = [ROOT / "F117_Production_Master.blend"]
    assets.extend(p for p in (ROOT / "UnityAuthoring/Assets/F117").rglob("*")
                  if p.is_file() and "Generated" not in p.parts
                  and p.suffix.lower() in {".fbx", ".png", ".meta"})
    return sorted(assets)


def record(path):
    with path.open("rb") as stream:
        prefix = stream.read(80)
        if prefix.startswith(b"version https://git-lfs.github.com/spec/"):
            raise ValueError(f"Git LFS pointer instead of asset: {path.relative_to(ROOT)}; run git lfs pull")
        stream.seek(0)
        if path.suffix.lower() == ".meta":
            # Git checkout may use CRLF on Windows and LF on the CI host.
            data = stream.read().replace(b"\r\n", b"\n")
            return {"path": path.relative_to(ROOT).as_posix(), "normalization": "lf",
                    "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    return {"path": path.relative_to(ROOT).as_posix(), "bytes": path.stat().st_size, "sha256": digest}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="Record deliberately edited source assets")
    args = parser.parse_args()
    actual = [record(path) for path in asset_paths()]
    if args.write:
        INVENTORY.parent.mkdir(parents=True, exist_ok=True)
        INVENTORY.write_text(json.dumps({"schemaVersion": 1, "assets": actual}, indent=2) + "\n", encoding="utf-8")
    expected = json.loads(INVENTORY.read_text(encoding="utf-8"))["assets"]
    if actual != expected:
        old = {x["path"]: x for x in expected}
        new = {x["path"]: x for x in actual}
        changed = sorted(p for p in old.keys() | new.keys() if old.get(p) != new.get(p))
        raise ValueError("Missing, added or changed assets: " + ", ".join(changed))
    version = ET.parse(ROOT / "Plugin/F117Nighthawk.csproj").findtext("PropertyGroup/Version")
    metadata = json.loads((ROOT / "Package/blacknight2u.f117a.nighthawk/meta.json").read_text(encoding="utf-8-sig"))
    plugin = (ROOT / "Plugin/Plugin.cs").read_text(encoding="utf-8-sig")
    builder = (ROOT / "UnityAuthoring/Assets/F117/Editor/F117Builder.cs").read_text(encoding="utf-8-sig")
    if not (metadata["artifact"]["version"] == version == re.search(r'PluginVersion = "([^"]+)"', plugin)[1]
            == re.search(r'const string Version = "([^"]+)"', builder)[1]):
        raise ValueError("Package, plugin, project and builder versions disagree")
    print(f"PASS: {len(actual)} source assets ({sum(a['bytes'] for a in actual):,} bytes); version {version}")


if __name__ == "__main__":
    main()
