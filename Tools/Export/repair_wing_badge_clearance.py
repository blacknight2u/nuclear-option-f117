"""Normalize the seven measured wing badge cards to a minimal safe clearance.

An earlier repair forced every card to roughly 2.1 mm above its supporting skin,
which made the transparent cards visibly float. This source repair reconstructs
the imported placement and moves only cards that would otherwise intersect the
wing. Topology, UVs, materials, and object ownership remain unchanged.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


ROOT = Path(__file__).resolve().parents[2]
MASTER = ROOT / "F117_Production_Master.blend"
DECAL_MATERIAL = "F117A_external_decals_new"
TARGET_CLEARANCE = 0.0005
SAMPLE_STEPS = 30

RAISED_DIGESTS = {
    ("F117_Exterior_LeftWing_Mesh", (1561, 1562)):
        "c7d690628e6333456f2c2d3c648bd2f5f75bb660150e1f16a5ac3b3da8d1763b",
    ("F117_Exterior_LeftWing_Mesh", (1563, 1564)):
        "6078187bb667a2004ac8a482b97450a8ab94bbeb2b3369e4ff5e379e818dc153",
    ("F117_Exterior_LeftWing_Mesh", (1567, 1568)):
        "c2357992c243c7fde269e750db70b327d8f78044946e7025606cde6e946da53a",
    ("F117_Exterior_RightWing_Mesh", (1430, 1431)):
        "64b7f0412c24f61bf2d3c9315bcf75168b0a44d2d979d236405ac033eb434572",
    ("F117_Exterior_RightWing_Mesh", (1432, 1433)):
        "8c73575d50db7345de66ea8e386a435cd7fcdfcb612b27ed7cf5e7c99292af95",
    ("F117_Exterior_RightWing_Mesh", (1434, 1435)):
        "ec897738913b1913514d1333903b05d4c9d49b13a34fbc27178aba3404d8643e",
    ("F117_Exterior_RightWing_Mesh", (1436, 1437)):
        "84e19b9769e5a3bd1a7d9006f53fface3524a8bcf1c388751be0810f9b654cdd",
}

NORMALIZED_DIGESTS = {
    ("F117_Exterior_LeftWing_Mesh", (1561, 1562)):
        "56635f50e2301524c9860fdc402d4b783791881d2bef40215e1c6f413699a351",
    ("F117_Exterior_LeftWing_Mesh", (1563, 1564)):
        "3cf86c7d7cc1627a4e936c1d485fed5ef6bd33aab581b0bc6353a9daf1df6be6",
    ("F117_Exterior_LeftWing_Mesh", (1567, 1568)):
        "78a3b51902a1b851311e5398ebe9be08976282ebe814fe1103a49205ee6a9b4d",
    ("F117_Exterior_RightWing_Mesh", (1430, 1431)):
        "5cc9234a7239e7f205ac8a8f4aaaca4261381cbabc8c8cf7771eece5545ab3f5",
    ("F117_Exterior_RightWing_Mesh", (1432, 1433)):
        "33ca5f5c8ace0f5b4f7748565e68398b512edbaae7fa0a2f88ad5696a55b2349",
    ("F117_Exterior_RightWing_Mesh", (1434, 1435)):
        "3010325fdb9704f5956c90fb8eb75272bfbe87a96595de5644edb10943195a2d",
    ("F117_Exterior_RightWing_Mesh", (1436, 1437)):
        "864d39104ce9c401bf5904b8633ba18e94a13927d660b0b3b18441fd014dc423",
}


@dataclass(frozen=True)
class Repair:
    owner: str
    polygons: tuple[int, int]
    vertices: tuple[int, int, int, int]
    local_delta: tuple[float, float, float]
    expected_before: str


REPAIRS = (
    Repair("F117_Exterior_LeftWing_Mesh", (1561, 1562), (2139, 2140, 2141, 2142),
           (0.000015914755, 0.000003662075, 0.000475934678),
           "a9cd5b49a279addcfa245b7896e4aabbdc4c2ecadbe6a7d52b5532d4dbdb840c"),
    Repair("F117_Exterior_LeftWing_Mesh", (1563, 1564), (2143, 2144, 2145, 2146),
           (0.000013522068, 0.000010076559, 0.000360445469),
           "1e4ed3e3d98a76c34479beeb1e7dda14e9d3fc634eacca30e78d8e1d56c7ca35"),
    Repair("F117_Exterior_LeftWing_Mesh", (1567, 1568), (2151, 2152, 2153, 2154),
           (0.000049291961, -0.000001709544, -0.000185991710),
           "78a3b51902a1b851311e5398ebe9be08976282ebe814fe1103a49205ee6a9b4d"),
    Repair("F117_Exterior_RightWing_Mesh", (1430, 1431), (2073, 2074, 2075, 2076),
           (-0.000010009158, 0.000010293482, 0.000276671315),
           "6d7962a2eee3b9ae6f4ffd409a3dceecc9f3bbc14c5fc69f1f20fe26254bf0c8"),
    Repair("F117_Exterior_RightWing_Mesh", (1432, 1433), (2077, 2078, 2079, 2080),
           (-0.000007741655, 0.000005304103, 0.000163682795),
           "33ca5f5c8ace0f5b4f7748565e68398b512edbaae7fa0a2f88ad5696a55b2349"),
    Repair("F117_Exterior_RightWing_Mesh", (1434, 1435), (2081, 2082, 2083, 2084),
           (-0.000032659045, -0.000002540922, -0.000159521296),
           "3010325fdb9704f5956c90fb8eb75272bfbe87a96595de5644edb10943195a2d"),
    Repair("F117_Exterior_RightWing_Mesh", (1436, 1437), (2085, 2086, 2087, 2088),
           (-0.000031341435, -0.000008580227, -0.000098094657),
           "864d39104ce9c401bf5904b8633ba18e94a13927d660b0b3b18441fd014dc423"),
)


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true")
    values = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(values)


def coordinate_digest(obj, vertex_indices):
    payload = bytearray(b"F117_WING_BADGE_COORDINATES_V1\0")
    for index in vertex_indices:
        payload.extend(struct.pack("<I", index))
        point = obj.data.vertices[index].co
        payload.extend(struct.pack("<ddd", point.x, point.y, point.z))
    return hashlib.sha256(payload).hexdigest()


def require_component(repair):
    obj = bpy.data.objects.get(repair.owner)
    if obj is None or obj.type != "MESH":
        raise RuntimeError(f"Missing wing owner {repair.owner}")
    mesh = obj.data
    if max(repair.polygons) >= len(mesh.polygons) or max(repair.vertices) >= len(mesh.vertices):
        raise RuntimeError(f"{repair.owner} badge indices no longer exist")
    polygons = [mesh.polygons[index] for index in repair.polygons]
    actual_vertices = set(index for polygon in polygons for index in polygon.vertices)
    if actual_vertices != set(repair.vertices) or any(len(polygon.vertices) != 3 for polygon in polygons):
        raise RuntimeError(
            f"{repair.owner} polygons {repair.polygons} are no longer the audited two-triangle quad")
    materials = [mesh.materials[polygon.material_index].name for polygon in polygons]
    if materials != [DECAL_MATERIAL, DECAL_MATERIAL]:
        raise RuntimeError(
            f"{repair.owner} polygons {repair.polygons} material changed: {materials}")
    return obj


def skin_bvh(obj):
    vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    triangles = []
    for polygon in obj.data.polygons:
        material = obj.data.materials[polygon.material_index]
        name = material.name if material is not None else ""
        if not name.startswith("F117_EXTERNAL_"):
            continue
        polygon_vertices = tuple(polygon.vertices)
        if len(polygon_vertices) == 3:
            triangles.append(polygon_vertices)
        else:
            for index in range(1, len(polygon_vertices) - 1):
                triangles.append((polygon_vertices[0], polygon_vertices[index],
                                  polygon_vertices[index + 1]))
    if not triangles:
        raise RuntimeError(f"{obj.name} has no exterior skin triangles")
    return BVHTree.FromPolygons(vertices, triangles, all_triangles=True), vertices


def minimum_clearance(obj, polygon_indices):
    bvh, world_vertices = skin_bvh(obj)
    minimum = float("inf")
    for polygon_index in polygon_indices:
        indices = tuple(obj.data.polygons[polygon_index].vertices)
        a, b, c = (world_vertices[index] for index in indices)
        for first in range(SAMPLE_STEPS + 1):
            for second in range(SAMPLE_STEPS + 1 - first):
                u = first / SAMPLE_STEPS
                v = second / SAMPLE_STEPS
                point = a * (1.0 - u - v) + b * u + c * v
                nearest, normal, _face, _distance = bvh.find_nearest(point)
                if nearest is None or normal is None:
                    raise RuntimeError(f"Could not project {obj.name} decal onto its wing skin")
                minimum = min(minimum, (point - nearest).dot(normal))
    return minimum


def main():
    options = arguments()
    loaded = Path(bpy.data.filepath).resolve()
    if os.path.normcase(os.fspath(loaded)) != os.path.normcase(os.fspath(MASTER.resolve())):
        raise RuntimeError(f"Refusing unexpected Blender file: {bpy.data.filepath}")

    for repair in REPAIRS:
        obj = require_component(repair)
        digest = coordinate_digest(obj, repair.vertices)
        key = (repair.owner, repair.polygons)
        if digest == NORMALIZED_DIGESTS[key]:
            clearance = minimum_clearance(obj, repair.polygons)
            if clearance < TARGET_CLEARANCE - 0.000002:
                raise RuntimeError(
                    f"{repair.owner} {repair.polygons} normalized fingerprint has only "
                    f"{clearance * 1000.0:.6f} mm clearance")
            print(f"BADGE={repair.owner}:{repair.polygons[0]}-{repair.polygons[1]} "
                  f"STATE=already-normalized FINAL_MM={clearance * 1000.0:.6f} "
                  f"FINAL_DIGEST={digest}")
            continue
        expected = RAISED_DIGESTS[key]
        if digest != expected:
            raise RuntimeError(
                f"{repair.owner} {repair.polygons} current fingerprint changed: {digest}")

        current = {index: obj.data.vertices[index].co.copy()
                   for index in repair.vertices}
        current_clearance = minimum_clearance(obj, repair.polygons)
        delta = Vector(repair.local_delta)
        for vertex_index in repair.vertices:
            obj.data.vertices[vertex_index].co = current[vertex_index] - delta
        obj.data.update(calc_edges=True)
        imported_clearance = minimum_clearance(obj, repair.polygons)

        amount = 0.0
        if imported_clearance < TARGET_CLEARANCE:
            low = 0.0
            high = 1.0
            for _iteration in range(32):
                amount = (low + high) * 0.5
                for vertex_index in repair.vertices:
                    obj.data.vertices[vertex_index].co = (
                        current[vertex_index] - delta + delta * amount)
                obj.data.update(calc_edges=True)
                if minimum_clearance(obj, repair.polygons) < TARGET_CLEARANCE:
                    low = amount
                else:
                    high = amount
            amount = high

        for vertex_index in repair.vertices:
            obj.data.vertices[vertex_index].co = (
                current[vertex_index] - delta + delta * amount)
        obj.data.update(calc_edges=True)
        repaired_clearance = minimum_clearance(obj, repair.polygons)
        if repaired_clearance < TARGET_CLEARANCE - 0.000002:
            raise RuntimeError(
                f"{repair.owner} {repair.polygons} clearance is only "
                f"{repaired_clearance * 1000.0:.6f} mm")
        print(f"BADGE={repair.owner}:{repair.polygons[0]}-{repair.polygons[1]} "
              f"CURRENT_MM={current_clearance * 1000.0:.6f} "
              f"IMPORTED_MM={imported_clearance * 1000.0:.6f} "
              f"REPAIR_AMOUNT={amount:.9f} "
              f"FINAL_MM={repaired_clearance * 1000.0:.6f} "
              f"FINAL_DIGEST={coordinate_digest(obj, repair.vertices)}")
        if not options.apply:
            for vertex_index in repair.vertices:
                obj.data.vertices[vertex_index].co = current[vertex_index]
            obj.data.update(calc_edges=True)

    if options.apply:
        bpy.ops.wm.save_as_mainfile(filepath=os.fspath(MASTER))
        print(f"SAVED_BLEND={MASTER}")

    # Print the two export-contract fingerprints affected by the intentional
    # coordinate repair. These values are independently reviewed before the
    # canonical exporter is updated; the repair never weakens its hash locks.
    sys.path.insert(0, os.fspath(ROOT / "Tools" / "Export"))
    import author_damage_sections as damage
    for owner in (damage.LEFT_NAME, damage.RIGHT_NAME):
        section = bpy.data.objects[owner]
        structure_index = next(index for index, material in enumerate(section.data.materials)
                               if material is not None and
                               material.name == damage.STRUCTURE_MATERIAL_NAME)
        skin_polygons = [polygon.index for polygon in section.data.polygons
                         if polygon.material_index != structure_index]
        keys = [damage.position_key(section.matrix_world @ vertex.co)
                for vertex in section.data.vertices]
        material_digest = damage.geometry_digest(
            section, skin_polygons, include_material=True, keys=keys)
        components, _ = damage.connected_components(section, skin_polygons)
        component_digests = [damage.geometry_digest(section, component, keys=keys)
                             for component in components]
        print(f"SECTION={owner} MATERIAL_DIGEST={material_digest} "
              f"COMPONENT_SET_DIGEST={damage.component_set_digest(component_digests)}")


if __name__ == "__main__":
    main()
