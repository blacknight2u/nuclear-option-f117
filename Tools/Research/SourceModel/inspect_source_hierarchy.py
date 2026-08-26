import bpy
import json
from mathutils import Vector


def descendants(root):
    stack = list(root.children)
    output = []
    while stack:
        current = stack.pop()
        output.append(current)
        stack.extend(current.children)
    return output


bpy.context.scene.frame_set(1)
parent = bpy.data.objects["parent"]
groups = []
for root in parent.children:
    meshes = [obj for obj in descendants(root) if obj.type == "MESH"]
    if root.type == "MESH":
        meshes.insert(0, root)
    if not meshes:
        continue
    corners = []
    for mesh in meshes:
        corners.extend(mesh.matrix_world @ Vector(corner) for corner in mesh.bound_box)
    minimum = [min(vertex[axis] for vertex in corners) for axis in range(3)]
    maximum = [max(vertex[axis] for vertex in corners) for axis in range(3)]
    dimensions = [maximum[axis] - minimum[axis] for axis in range(3)]
    center = [(minimum[axis] + maximum[axis]) * 0.5 for axis in range(3)]
    if (
        center[1] < -3.5
        and minimum[2] < 0.25
        and dimensions[0] < 4.0
        and dimensions[1] < 4.0
        and dimensions[2] > 0.5
    ):
        groups.append(
            {
                "root": root.name,
                "type": root.type,
                "center": [round(value, 3) for value in center],
                "minimum": [round(value, 3) for value in minimum],
                "maximum": [round(value, 3) for value in maximum],
                "dimensions": [round(value, 3) for value in dimensions],
                "mesh_count": len(meshes),
                "meshes": [mesh.name for mesh in meshes],
            }
        )

print("RESULT=" + json.dumps(sorted(groups, key=lambda item: item["root"])))
