"""Read-only Blender audit for every physical F-117 MFD surface."""

import bpy
from mathutils import Vector


def rounded(vector):
    return tuple(round(value, 5) for value in vector)


cockpit = bpy.data.objects.get("F117_Cockpit_Mesh")
camera = bpy.data.objects.get("LOC_CockpitCamera")
if cockpit is None or cockpit.type != "MESH":
    raise RuntimeError("F117_Cockpit_Mesh is missing")
if camera is None:
    raise RuntimeError("LOC_CockpitCamera is missing")

slot_indices = {
    index for index, slot in enumerate(cockpit.material_slots)
    if slot.material and "MFD" in slot.material.name.upper()
}
polygons = [poly for poly in cockpit.data.polygons if poly.material_index in slot_indices]
by_vertex = {}
for polygon in polygons:
    for vertex in polygon.vertices:
        by_vertex.setdefault(vertex, []).append(polygon.index)
polygon_by_index = {polygon.index: polygon for polygon in polygons}

remaining = set(polygon_by_index)
components = []
while remaining:
    seed = remaining.pop()
    component = {seed}
    pending = [seed]
    while pending:
        polygon = polygon_by_index[pending.pop()]
        neighbors = {
            neighbor
            for vertex in polygon.vertices
            for neighbor in by_vertex.get(vertex, [])
        }
        new_neighbors = neighbors & remaining
        remaining.difference_update(new_neighbors)
        component.update(new_neighbors)
        pending.extend(new_neighbors)
    components.append(component)

uv_layer = cockpit.data.uv_layers.active
camera_world = camera.matrix_world.translation
print(f"MFD_AUDIT polygons={len(polygons)} components={len(components)} camera={rounded(camera_world)}")
for index, component in enumerate(sorted(components, key=lambda item: min(item))):
    component_polygons = [polygon_by_index[item] for item in component]
    vertices = sorted({vertex for polygon in component_polygons for vertex in polygon.vertices})
    world_points = [cockpit.matrix_world @ cockpit.data.vertices[vertex].co for vertex in vertices]
    minimum = Vector(tuple(min(point[axis] for point in world_points) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in world_points) for axis in range(3)))
    center = sum(world_points, Vector()) / len(world_points)
    weighted_normal = Vector()
    total_area = 0.0
    uvs = []
    for polygon in component_polygons:
        normal = cockpit.matrix_world.to_3x3() @ polygon.normal
        weighted_normal += normal.normalized() * polygon.area
        total_area += polygon.area
        if uv_layer is not None:
            uvs.extend(uv_layer.data[loop].uv.copy() for loop in polygon.loop_indices)
    normal = weighted_normal.normalized()
    toward_eye = (camera_world - center).normalized()
    uv_min = tuple(min(uv[axis] for uv in uvs) for axis in range(2)) if uvs else (0.0, 0.0)
    uv_max = tuple(max(uv[axis] for uv in uvs) for axis in range(2)) if uvs else (0.0, 0.0)
    print(
        f"COMPONENT {index} polys={len(component_polygons)} verts={len(vertices)} area={total_area:.5f} "
        f"min={rounded(minimum)} max={rounded(maximum)} center={rounded(center)} "
        f"normal={rounded(normal)} eyeDot={normal.dot(toward_eye):.5f} "
        f"uvMin={rounded(uv_min)} uvMax={rounded(uv_max)}"
    )
