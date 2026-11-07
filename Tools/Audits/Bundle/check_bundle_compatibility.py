"""Read-only checks of serialized script fields and Blueprinter patch member paths.

Requires UnityPy and a schema produced by Tools/Audits/Export-GameSchema.ps1.
This does not replace an in-game flight or multiplayer test.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

import UnityPy


def audit(bundle_path, schema_path):
    schema = json.loads(Path(schema_path).read_text(encoding="utf-8-sig"))
    types = schema["types"]
    env = UnityPy.load(str(bundle_path))
    failures = []
    scripts = {}
    field_checks = 0
    path_checks = 0

    def member(type_name, name, properties=False):
        while type_name in types:
            info = types[type_name]
            if name in info["fields"]:
                return info["fields"][name]
            if properties and name in info["properties"]:
                return info["properties"][name]
            type_name = info["baseType"]
        return None

    def qualified(name):
        return name.split(",", 1)[0].strip().replace("+", "/")

    for obj in env.objects:
        if obj.type.name != "MonoScript":
            continue
        data = obj.read_typetree()
        name = ".".join(filter(None, (data["m_Namespace"], data["m_ClassName"])))
        scripts[obj.path_id] = name
        if name not in types:
            failures.append(f"Missing script type: {name}")

    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        data = obj.read_typetree()
        script = scripts.get(data["m_Script"]["m_PathID"])
        if not script:
            failures.append(f"Unresolved script on object {obj.path_id}")
            continue
        for key in data:
            # These fields belong to Unity's native Object/Behaviour serialization.
            if key.startswith("m_"):
                continue
            field_checks += 1
            if member(script, key) is None:
                failures.append(f"Missing serialized field: {script}.{key}")

    manifests = [json.loads(o.read_typetree()["m_Script"]) for o in env.objects
                 if o.type.name == "TextAsset" and o.read_typetree()["m_Name"] == "patch_manifest"]
    if len(manifests) != 1:
        raise ValueError("Expected exactly one Blueprinter patch manifest")
    manifest = manifests[0]

    def visit(value):
        nonlocal path_checks
        if isinstance(value, list):
            for item in value:
                visit(item)
        elif isinstance(value, dict):
            if "memberPath" in value and value["memberPath"]:
                path_checks += 1
                type_name = qualified(value.get("componentType") or value.get("asset", {}).get("type", ""))
                path = value["memberPath"]
                # Blueprinter 2.1.1 PatchRunner strips this selector before
                # setting AudioSource.outputAudioMixerGroup.
                if path.startswith("outputAudioMixerGroup::"):
                    path, group = path.split("::", 1)
                    if not group:
                        failures.append("Empty AudioMixer group selector")
                for part in path.split("."):
                    match = re.fullmatch(r"([^\[\]]+)((?:\[\d+\])*)", part)
                    if not match:
                        failures.append(f"Unsupported member path: {value['memberPath']}")
                        break
                    result = member(type_name, match[1], properties=True)
                    if result is None:
                        failures.append(f"Missing patch member: {type_name}.{part} ({value['memberPath']})")
                        break
                    for _ in re.findall(r"\[\d+\]", match[2]):
                        if result.endswith("[]"):
                            result = result[:-2]
                        elif result.startswith("System.Collections.Generic.List`1<") and result.endswith(">"):
                            result = result[result.index("<") + 1:-1]
                        else:
                            failures.append(f"Patch indexes non-list type: {result}")
                    type_name = result
            for key, item in value.items():
                if key == "payloadJson" and isinstance(item, str):
                    visit(json.loads(item))
                else:
                    visit(item)

    visit(manifest)
    with open(bundle_path, "rb") as stream:
        bundle_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    return {
        "result": "FAIL" if failures else "PASS",
        "method": "Static serialized-field and Blueprinter member-path checks; no runtime flight validation.",
        "bundleSha256": bundle_hash,
        "gameAssemblySha256": schema["gameAssemblySha256"],
        "modVersion": manifest["modVersion"],
        "objectCount": len(env.objects),
        "scriptTypes": len(scripts),
        "serializedFieldChecks": field_checks,
        "patchMemberPathChecks": path_checks,
        "failures": failures,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    parser.add_argument("schema", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    report = audit(args.bundle, args.schema)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    if report["failures"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
