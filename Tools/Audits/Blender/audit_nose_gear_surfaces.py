"""Report source geometry around the F-117 nose-gear opening and its door.

This is read-only.  It records exact transforms, material ownership, connected
components, downward-facing triangles, and boundary edges for the central belly
and nose-gear door so revisions can be compared without visual guesswork.
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict, deque
from pathlib import Path

import bpy


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    values = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(values)


def point(vector):
    return [round(float(value), 9) for value in vector]


def components(mesh):
    adjacency = defaultdict(set)
    for edge in mesh.edges:
        first, second = edge.vertices
        adjacency[first].add(second)
        adjacency[second].add(first)
    remaining = set(range(len(mesh.vertices)))
    sizes = []
    while remaining:
        seed = remaining.pop()
        pending = deque([seed])
        count = 0
        while pending:
            vertex = pending.popleft()
            count += 1
            for neighbor in adjacency[vertex]:
                if neighbor in remaining:
                    remaining.remove(neighbor)
                    pending.append(neighbor)
        sizes.append(count)
    return sorted(sizes, reverse=True)


def describe(obj):
    mesh = obj.data
    world_points = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
    edge_use = defaultdict(int)
    for polygon in mesh.polygons:
        vertices = tuple(polygon.vertices)
        for index, first in enumerate(vertices):
            second = vertices[(index + 1) % len(vertices)]
            edge_use[tuple(sorted((first, second)))] += 1
    boundary = [edge for edge, count in edge_use.items() if count == 1]
    downward = []
    normal_matrix = obj.matrix_world.to_3x3()
    for polygon in mesh.polygons:
        normal = (normal_matrix @ polygon.normal).normalized()
        if normal.y <= -0.75:
            center = obj.matrix_world @ polygon.center
            material = mesh.materials[polygon.material_index]
            downward.append({
                "polygon": polygon.index,
                "center": point(center),
                "normal": point(normal),
                "area": round(float(polygon.area), 12),
                "material": material.name if material else None,
            })
    return {
        "name": obj.name,
        "parent": obj.parent.name if obj.parent else None,
        "location": point(obj.location),
        "rotation_euler": point(obj.rotation_euler),
        "scale": point(obj.scale),
        "vertices": len(mesh.vertices),
        "edges": len(mesh.edges),
        "polygons": len(mesh.polygons),
        "components": components(mesh),
        "boundary_edge_count": len(boundary),
        "bounds_min": point([min(value[axis] for value in world_points) for axis in range(3)]),
        "bounds_max": point([max(value[axis] for value in world_points) for axis in range(3)]),
        "materials": [material.name if material else None for material in mesh.materials],
        "downward": downward,
    }


def main():
    options = arguments()
    targets = [
        "F117_Exterior_Mesh",
        "F117_GearDoor_Nose_Mesh",
    ]
    report = {"blend": bpy.data.filepath, "objects": []}
    for name in targets:
        obj = bpy.data.objects.get(name)
        if obj is None or obj.type != "MESH":
            raise RuntimeError(f"Missing required mesh object {name}")
        report["objects"].append(describe(obj))
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"NOSE_GEAR_SURFACE_AUDIT={options.output}")


if __name__ == "__main__":
    main()
