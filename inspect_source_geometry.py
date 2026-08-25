import bpy
import json
from mathutils import Vector


bpy.context.scene.frame_set(1)
output = []
for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = [min(vertex[axis] for vertex in corners) for axis in range(3)]
    maximum = [max(vertex[axis] for vertex in corners) for axis in range(3)]
    dimensions = [maximum[axis] - minimum[axis] for axis in range(3)]
    center = [(minimum[axis] + maximum[axis]) * 0.5 for axis in range(3)]
    if (
        0.4 < abs(center[0]) < 1.8
        and -6.5 < center[1] < -3.2
        and minimum[2] < -0.25
        and dimensions[0] < 2.0
        and dimensions[1] < 3.0
        and dimensions[2] > 0.35
    ):
        output.append(
            {
                "name": obj.name,
                "parent": obj.parent.name if obj.parent else None,
                "center": [round(value, 3) for value in center],
                "dimensions": [round(value, 3) for value in dimensions],
                "triangles": sum(len(poly.vertices) - 2 for poly in obj.data.polygons),
            }
        )

print("RESULT=" + json.dumps(sorted(output, key=lambda item: item["center"][2])))
