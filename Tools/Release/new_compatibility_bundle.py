"""Version-only repack of the immutable published v0.5.0 aircraft bundle.

Requires UnityPy. Does not run Unity or pretend to rebuild the authored assets.
The entire bundle must remain byte-identical except the manifest version token.
For geometry, physics, materials or other asset edits use the Unity builder.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import zipfile

import UnityPy

BASELINE_ZIP_SHA256 = "df7b1dfb7c0fd6387fbf210d4e0dbfd93a3fc948b515b28b2f4b76b488e29f81"
BASELINE_BUNDLE_SHA256 = "47fa4ab1643d1d38988c05260f34316cc5de86413eb7479ff4bcaab773ffe71b"
BUNDLE_NAME = "blacknight2u.f117a.nighthawk.nobp"


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("published_zip", type=Path)
    parser.add_argument("game_schema", type=Path)
    parser.add_argument("plugin_report", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+\.\d+", args.version) or tuple(map(int, args.version.split("."))) <= (0, 5, 0):
        raise ValueError("Expected a newer semantic version than 0.5.0")
    if args.output.exists() or Path(str(args.output) + ".validation.json").exists():
        raise FileExistsError("Refusing to overwrite a bundle or its receipt")
    if sha256(args.published_zip) != BASELINE_ZIP_SHA256:
        raise ValueError("Input must be the exact published F-117 v0.5.0 release ZIP")
    with zipfile.ZipFile(args.published_zip) as archive:
        baseline = archive.read(BUNDLE_NAME)
    if hashlib.sha256(baseline).hexdigest() != BASELINE_BUNDLE_SHA256:
        raise ValueError("Published bundle hash mismatch")
    old = b'"modVersion": "0.5.0"'
    new = f'"modVersion": "{args.version}"'.encode("ascii")
    if len(old) != len(new) or baseline.count(old) != 1:
        raise ValueError("An equal-length, unique manifest version token is required; otherwise rebuild in Unity")
    offset = baseline.index(old)
    revised = baseline[:offset] + new + baseline[offset + len(old):]
    before = UnityPy.load(baseline)
    after = UnityPy.load(revised)
    before_objects = {(o.assets_file.name, o.path_id): o for o in before.objects}
    after_objects = {(o.assets_file.name, o.path_id): o for o in after.objects}
    if before_objects.keys() != after_objects.keys():
        raise ValueError("Bundle object inventory changed")
    changed = []
    for key, original in before_objects.items():
        updated = after_objects[key]
        if original.get_raw_data() == updated.get_raw_data():
            continue
        if original.type.name != "TextAsset" or updated.type.name != "TextAsset":
            raise ValueError(f"Non-manifest asset changed: {key}")
        old_tree = original.read_typetree()
        new_tree = updated.read_typetree()
        if old_tree["m_Name"] != "patch_manifest" or old_tree["m_Name"] != new_tree["m_Name"]:
            raise ValueError("Unexpected TextAsset change")
        old_manifest = json.loads(old_tree["m_Script"])
        new_manifest = json.loads(new_tree["m_Script"])
        old_manifest["modVersion"] = args.version
        if old_manifest != new_manifest:
            raise ValueError("Blueprinter patches/ops changed")
        changed.append(key)
    if len(changed) != 1:
        raise ValueError("Expected exactly one changed manifest object")
    api_report = json.loads(args.plugin_report.read_text(encoding="utf-8-sig"))
    schema = json.loads(args.game_schema.read_text(encoding="utf-8-sig"))
    if api_report["result"] != "PASS" or api_report["gameAssemblySha256"] != schema["gameAssemblySha256"]:
        raise ValueError("A successful plugin audit for the same game assembly is required")
    if not api_report["plugin"].startswith(f"F117Nighthawk, Version={args.version}.0,"):
        raise ValueError("Plugin audit does not match the requested release version")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("xb") as stream:
        stream.write(revised)
    checker_path = Path(__file__).resolve().parents[1] / "Audits/Bundle/check_bundle_compatibility.py"
    spec = importlib.util.spec_from_file_location("bundle_compatibility", checker_path)
    checker = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(checker)
    report = checker.audit(args.output, args.game_schema)
    if report["result"] != "PASS":
        raise ValueError(f"Bundle compatibility failed: {report['failures']}")
    receipt = {
        "version": args.version,
        "sha256": sha256(args.output),
        "result": "PASS",
        "validationKind": "published-bundle-compatibility",
        "sourceVersion": "0.5.0",
        "sourceReleaseSha256": BASELINE_ZIP_SHA256,
        "sourceBundleSha256": BASELINE_BUNDLE_SHA256,
        "unchangedAssetObjects": len(before_objects) - 1,
        "changedObjects": ["patch_manifest.modVersion"],
        "pluginSha256": api_report["pluginSha256"],
        "gameAssemblySha256": schema["gameAssemblySha256"],
        "bundleChecks": report,
        "limitation": "Static compatibility and byte-preservation checks; no Unity rebuild, flight or multiplayer test.",
    }
    Path(str(args.output) + ".validation.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))


if __name__ == "__main__":
    main()
