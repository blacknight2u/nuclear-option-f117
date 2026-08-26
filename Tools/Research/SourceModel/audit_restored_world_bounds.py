import math
import bpy
from mathutils import Matrix, Vector


rotation = Matrix.Rotation(math.radians(-90.0), 4, "X")
depsgraph = bpy.context.evaluated_depsgraph_get()
names = ("part158", "part159", "part164", "part165", "part113.005", "part116", "part146.001", "part127.003", "part130", "part146")
print("F117_RESTORED_WORLD_BOUNDS_BEGIN")
for name in names:
    source = bpy.data.objects.get(name)
    if source is None:
        print(f"PART {name} MISSING")
        continue
    evaluated = source.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
    world = rotation @ source.matrix_world
    points = [world @ vertex.co for vertex in mesh.vertices]
    evaluated.to_mesh_clear()
    minimum = tuple(round(min(point[axis] for point in points), 5) for axis in range(3))
    maximum = tuple(round(max(point[axis] for point in points), 5) for axis in range(3))
    center = tuple(round(sum(point[axis] for point in points) / len(points), 5) for axis in range(3))
    print(f"PART {name} min={minimum} max={maximum} center={center} vertices={len(points)}")
print("F117_RESTORED_WORLD_BOUNDS_END")
