import bpy
import math
import numpy as np
from mathutils import Vector


SIDES = {
    "L": "l_rudder_percent_key_AN_",
    "R": "r_rudder_percent_key_AN_",
}


def descendants(root):
    found = []
    stack = [root]
    while stack:
        current = stack.pop()
        found.append(current)
        stack.extend(current.children)
    return found


def world_vertices(objects):
    points = []
    for obj in objects:
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        points.extend(tuple(evaluated.matrix_world @ vertex.co) for vertex in mesh.vertices)
        evaluated.to_mesh_clear()
    return np.asarray(points, dtype=float)


def panel_normal(objects):
    points = world_vertices(objects)
    centered = points - points.mean(axis=0)
    values, vectors = np.linalg.eigh(centered.T @ centered / max(len(points), 1))
    normal = vectors[:, int(np.argmin(values))]
    normal /= np.linalg.norm(normal)
    if normal[0] < 0:
        normal = -normal
    return normal, points.mean(axis=0), values


def projected_mesh_area(objects, reference_normal):
    total = 0.0
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in objects:
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        matrix = evaluated.matrix_world
        for tri in mesh.loop_triangles:
            p0 = matrix @ mesh.vertices[tri.vertices[0]].co
            p1 = matrix @ mesh.vertices[tri.vertices[1]].co
            p2 = matrix @ mesh.vertices[tri.vertices[2]].co
            cross = np.asarray((p1 - p0).cross(p2 - p0))
            total += abs(np.dot(cross, reference_normal)) * 0.5
        evaluated.to_mesh_clear()
    # The closed panel has two broad faces. Divide their summed projection to
    # obtain the aerodynamic planform instead of double-counting the thickness.
    return total * 0.5


def fixed_triangles(excluded):
    result = []
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj in excluded:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        matrix = evaluated.matrix_world
        for tri in mesh.loop_triangles:
            p0 = matrix @ mesh.vertices[tri.vertices[0]].co
            p1 = matrix @ mesh.vertices[tri.vertices[1]].co
            p2 = matrix @ mesh.vertices[tri.vertices[2]].co
            cross = (p1 - p0).cross(p2 - p0)
            area2 = cross.length
            if area2 < 1e-8:
                continue
            normal = cross / area2
            center = (p0 + p1 + p2) / 3.0
            result.append((obj.name, np.asarray(center), np.asarray(normal), area2 * 0.5))
        evaluated.to_mesh_clear()
    return result


def projected(vector, axis):
    result = vector - axis * np.dot(vector, axis)
    length = np.linalg.norm(result)
    return result / length if length > 1e-8 else result


def signed_angle(start, end, axis):
    start = projected(start, axis)
    end = projected(end, axis)
    return math.degrees(math.atan2(np.dot(axis, np.cross(start, end)), np.dot(start, end)))


bpy.context.scene.frame_set(2)
bpy.context.view_layer.update()
all_rudder_objects = set()
side_data = {}
for side, driver_name in SIDES.items():
    driver = bpy.data.objects[driver_name]
    objects = descendants(driver)
    all_rudder_objects.update(objects)
    normal, center, eigenvalues = panel_normal(objects)
    hinge = np.asarray((driver.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized())
    side_data[side] = (driver, objects, normal, center, hinge)
    print("PANEL", side, "normal=", np.round(normal, 6), "center=", np.round(center, 6),
          "hinge=", np.round(hinge, 6), "eigen=", np.round(eigenvalues, 6),
          "projected_area=", f"{projected_mesh_area(objects, normal):.6f}")

triangles = fixed_triangles(all_rudder_objects)
for side, (driver, objects, neutral_normal, neutral_center, hinge) in side_data.items():
    # Look immediately forward of the moving panel, on the same side and height.
    candidates = []
    for name, center, normal, area in triangles:
        if np.sign(center[0]) != np.sign(neutral_center[0]):
            continue
        delta = center - neutral_center
        if center[1] > neutral_center[1] + 0.6 or center[1] < neutral_center[1] - 5.0:
            continue
        if abs(delta[0]) > 2.5 or abs(delta[2]) > 2.7:
            continue
        alignment = abs(np.dot(normal, neutral_normal))
        if alignment < 0.65:
            continue
        if np.dot(normal, neutral_normal) < 0:
            normal = -normal
        distance = np.linalg.norm(delta)
        weight = area * alignment / max(distance, 0.15)
        candidates.append((weight, name, center, normal, area, alignment, distance))

    candidates.sort(reverse=True, key=lambda item: item[0])
    selected = candidates[:80]
    by_object = {}
    weighted = np.zeros(3)
    weight_sum = 0.0
    for weight, name, center, normal, area, alignment, distance in selected:
        weighted += normal * weight
        weight_sum += weight
        by_object[name] = by_object.get(name, 0.0) + weight
    fixed_normal = weighted / max(np.linalg.norm(weighted), 1e-8)
    fixed_projected_area = 0.0
    for weight, name, center, normal, area, alignment, distance in candidates:
        if name not in ("part003", "part004"):
            continue
        fixed_projected_area += area * abs(np.dot(normal, fixed_normal))
    # Like the closed rudder, the fixed fin geometry contains both broad faces.
    fixed_projected_area *= 0.5
    print("FIXED", side, "normal=", np.round(fixed_normal, 6),
          "objects=", sorted(by_object.items(), key=lambda item: item[1], reverse=True)[:8],
          "selected=", len(selected), "projected_area=", f"{fixed_projected_area:.6f}")

    for frame_tenths in range(10, 31):
        frame = frame_tenths / 10.0
        bpy.context.scene.frame_set(int(frame), subframe=frame - int(frame))
        bpy.context.view_layer.update()
        moving_normal, _, _ = panel_normal(objects)
        if np.dot(moving_normal, neutral_normal) < 0:
            moving_normal = -moving_normal
        offset = signed_angle(fixed_normal, moving_normal, hinge)
        print("SWEEP", side, "frame=", f"{frame:.1f}", "offset_deg=", f"{offset:.6f}")
