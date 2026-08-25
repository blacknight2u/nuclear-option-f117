"""Report the source F-117 landing-gear rig without modifying the .blend.

The production exporter used to join each complete gear tree into one mesh.
This audit records the authored object, armature, vertex-group, bone-parent and
deployed/stowed bone transforms needed to preserve the original articulation.
"""

import bpy
from mathutils import Matrix


GEARS = {
    "Nose": "c_gear_AN_",
    "Left": "l_gear_AN_.001",
    "Right": "r_gear_AN_",
}


def descendants(root):
    return (root, *root.children_recursive)


def fmt_vector(value):
    return "(" + ",".join(f"{component:.5f}" for component in value) + ")"


def dominant_groups(mesh):
    totals = {group.index: 0.0 for group in mesh.vertex_groups}
    counts = {group.index: 0 for group in mesh.vertex_groups}
    for vertex in mesh.data.vertices:
        for assignment in vertex.groups:
            totals[assignment.group] = totals.get(assignment.group, 0.0) + assignment.weight
            counts[assignment.group] = counts.get(assignment.group, 0) + 1
    ranked = sorted(totals, key=lambda index: totals[index], reverse=True)
    return [
        (mesh.vertex_groups[index].name, totals[index], counts[index])
        for index in ranked
        if totals[index] > 0.0
    ]


def armatures_for(mesh):
    result = []
    for modifier in mesh.modifiers:
        if modifier.type == "ARMATURE" and modifier.object is not None:
            result.append(modifier.object)
    if mesh.parent is not None and mesh.parent.type == "ARMATURE" and mesh.parent not in result:
        result.append(mesh.parent)
    return result


for side, root_name in GEARS.items():
    root = bpy.data.objects[root_name]
    meshes = sorted((obj for obj in descendants(root) if obj.type == "MESH"), key=lambda item: item.name)
    print(f"GEAR {side} root={root_name} parent={root.parent.name if root.parent else '-'}")
    relevant_bones = set()
    armatures = set()
    for mesh in meshes:
        groups = dominant_groups(mesh)
        rigs = armatures_for(mesh)
        armatures.update(rigs)
        relevant_bones.update(name for name, _, _ in groups)
        group_text = ";".join(f"{name}:{weight:.1f}/{count}" for name, weight, count in groups)
        modifier_text = ",".join(rig.name for rig in rigs) or "-"
        print(
            f" MESH {mesh.name} parent={mesh.parent.name if mesh.parent else '-'} "
            f"parent_type={mesh.parent_type} armatures={modifier_text} groups={group_text or '-'}"
        )

    for armature in sorted(armatures, key=lambda item: item.name):
        print(f" ARMATURE {armature.name}")
        for bone_name in sorted(relevant_bones):
            pose_bone = armature.pose.bones.get(bone_name)
            data_bone = armature.data.bones.get(bone_name)
            if pose_bone is None or data_bone is None:
                continue
            parent = data_bone.parent.name if data_bone.parent else "-"
            print(
                f"  BONE {bone_name} parent={parent} connected={data_bone.use_connect} "
                f"head={fmt_vector(data_bone.head_local)} tail={fmt_vector(data_bone.tail_local)}"
            )
            for frame in (218, 1):
                bpy.context.scene.frame_set(frame)
                bpy.context.view_layer.update()
                world = armature.matrix_world @ pose_bone.matrix
                location, rotation, _ = world.decompose()
                axis, angle = rotation.to_axis_angle()
                print(
                    f"   FRAME {frame} loc={fmt_vector(location)} "
                    f"axis={fmt_vector(axis)} angle={angle * 57.295779513:.5f}"
                )

