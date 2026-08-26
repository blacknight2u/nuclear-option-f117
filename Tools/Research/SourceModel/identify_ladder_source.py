import bpy


bpy.context.scene.frame_set(1)
for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    qualifying = 0
    for polygon in obj.data.polygons:
        center = obj.matrix_world @ polygon.center
        if center.x > 1.75 and center.y < -5.2:
            qualifying += 1
    if qualifying:
        print(
            "LADDER_SOURCE="
            + repr(
                {
                    "name": obj.name,
                    "parent": obj.parent.name if obj.parent else None,
                    "qualifying_polygons": qualifying,
                    "total_polygons": len(obj.data.polygons),
                }
            )
        )
