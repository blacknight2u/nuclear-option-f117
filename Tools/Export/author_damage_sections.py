"""Author the F-117's only two genuine fixed-airframe break seams.

The production exterior is stored as one Blender object, but its left and right
whole-wing skins are already separate topological islands.  This tool separates
those islands (and their carried lights/decals) into three final export meshes:

    F117_Exterior_Mesh
    F117_Exterior_LeftWing_Mesh
    F117_Exterior_RightWing_Mesh

The two forward shoulder triangles on each source wing belong to the center
airframe.  Moving them before separation exposes the actual inner root cycles,
which receive one boundary-only structural face on both mating sections.  No
plane cuts, centroid fans, hidden cap objects, duplicate inner shells, or
coincident structure/exterior triangles are created.
"""

from __future__ import annotations

import hashlib
import math
import os
import struct
from collections import Counter, defaultdict, deque
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import tessellate_polygon


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
MASTER_PATH = REPOSITORY_ROOT / "F117_Production_Master.blend"
CENTRAL_NAME = "F117_Exterior_Mesh"
LEFT_NAME = "F117_Exterior_LeftWing_Mesh"
RIGHT_NAME = "F117_Exterior_RightWing_Mesh"
STRUCTURE_MATERIAL_NAME = "F117_AircraftStructure"
STRICT_WELD = 0.00001  # 0.01 mm; remains below the measured 0.268 mm root gap.
ROOT_SNAP_LIMIT = 0.0015
RING_COORDINATE_TOLERANCE = 0.00001
MEASUREMENT_TOLERANCE = 0.000005
FINGERPRINT_SCALE = 100000
COMPONENT_HASH_HEADER = b"F117_COMPONENT_GEOMETRY_V1\0"
COMPONENT_SET_HASH_HEADER = b"F117_COMPONENT_HASH_SET_V1\0"
MATERIAL_GEOMETRY_HASH_HEADER = b"F117_COMPONENT_MATERIAL_GEOMETRY_V1\0"
EXPECTED_SKIN_POLYGONS = {
    CENTRAL_NAME: 81290,
    LEFT_NAME: 1571,
    RIGHT_NAME: 1438,
}
EXPECTED_SECTION_POLYGONS = {
    CENTRAL_NAME: 81305,
    LEFT_NAME: 1578,
    RIGHT_NAME: 1446,
}
EXPECTED_SECTION_VERTICES = {
    CENTRAL_NAME: 147874,
    LEFT_NAME: 2168,
    RIGHT_NAME: 2099,
}
EXPECTED_STRUCTURE_TRIANGLES = {
    CENTRAL_NAME: 15,
    LEFT_NAME: 7,
    RIGHT_NAME: 8,
}
# These digests identify measured source islands by winding-preserving,
# world-space geometry. Polygon/vertex ordering is irrelevant; face winding,
# duplicate faces, material, triangle and welded-vertex contracts remain
# significant.
EXPECTED_COMPONENT_SET_DIGEST = "14dad9d0896ee4723b20a76025a9ed30d60d27e86fc13fff0727f63afe6e3034"
EXPECTED_CENTRAL_COMPONENT_SET_DIGEST = "046b9c3ea51464a3d7269a228088ef06a3d6ec83c7cb8a51a96f043c30c12f6b"
EXPECTED_SOURCE_MATERIAL_GEOMETRY_DIGEST = "403e556457a7bed1ac511406b03cb8a987293501617e457a764759b9b3bb529d"
EXPECTED_SIDE_COMPONENT_SET_DIGEST = {
    LEFT_NAME: "95fe93caf47bee48f25c5e29466d3f8e105a1aa444992ba03ac1b23b2033d9ac",
    RIGHT_NAME: "a0d2f3a4625eb275884be85c1fa68bf292d1b21139d88ab43e722d01e2ebbbd8",
}
EXPECTED_AUTHORED_SKIN_MATERIAL_DIGEST = {
    CENTRAL_NAME: "411134f84e517f27c77c40d299ee51c52153be3c383aebaa9a6773e5b785c744",
    LEFT_NAME: "cd7759e159421aeb30f46a80888b258a3c767ce34f138d25f34d15027f233eb2",
    RIGHT_NAME: "a8a7f95ebfb32917048b09f607a5299cc4f451b05dd0c0939d951dbb2423565a",
}
EXPECTED_AUTHORED_SKIN_COMPONENT_SET_DIGEST = {
    CENTRAL_NAME: "26ae101fab9c62d79c7e5f1bc22eee549dc5dfd8e8f0fd94219827e988be4b28",
    LEFT_NAME: "e9f242f3508d3b1ecaff14a54639de31b13b71ee50ccc6bcf01aaebc6be69873",
    RIGHT_NAME: "cef30b876caeb8064c8d60870b0b9ff210abc030d7f6d67d91a1b9d0880ae22b",
}

# digest: (owner, material name, source polygons, strict-welded vertices)
EXPECTED_WING_COMPONENTS = {
    "3de6265b555663190a67eae4ba1ef252fbe1963588e92087028fd6b09cf787c1":
        (LEFT_NAME, "F117_EXTERNAL_3", 621, 321),
    "538c8483f975b07aeb7b79127ebc8ad5d38d735e7ec5ec6ee30177b27cdb253a":
        (LEFT_NAME, "F117A_external_decals_new", 2, 4),
    "88e22739cb6a20ff365da2177ec362d04e1a10bb0484d40dd9f77f34a8241331":
        (LEFT_NAME, "F117A_external_decals_new", 2, 4),
    "5f724dc0fa3bc0bc56be4462b6f86ee669cf01b74b0b3cf9e68644e7596264c2":
        (LEFT_NAME, "F117A_external_decals_new", 2, 4),
    "e6b6dd80e7eb686f77a3fef391ee10a33ce25bc21ab5962c01178d70e4dc45aa":
        (LEFT_NAME, "F117_ext_glass", 223, 125),
    "cd9c442b701978fe6cf8497542b4540333a7ce099af5ec51eafdbf7883244fb2":
        (LEFT_NAME, "F117_ext_glass", 223, 126),
    "7886f2d9dd6184cb9fef5c1248f9ac13d92a22c58ab65776e80431b612866e49":
        (LEFT_NAME, "F117A_external_decals_new", 2, 4),
    "2b45acb8ddbb737931cd74d9c29436cbf8d2eca25552a951f338606202f2847b":
        (LEFT_NAME, "F117A_external_decals_new", 2, 4),
    "f29854c98fb96de1bbf826486cf75a419e2fda34c58945d2abbfd2d387ce40a1":
        (LEFT_NAME, "LIGHTS", 124, 62),
    "3f50b566acf5be8b8282e434da11b578fe95885642bb797b5dc4493c73866e0f":
        (LEFT_NAME, "LIGHTS", 124, 62),
    "0e8537bcbf6e0e4f560c000e6c8e7d755566d7929ac7a1cd2f553ee129442084":
        (LEFT_NAME, "LIGHTS", 124, 62),
    "f3b05026dcc091be85d6fc1cdb3e2013a5c54936793c88ff30e99cb467f1398b":
        (LEFT_NAME, "LIGHTS", 124, 62),
    "b340a9568556735b352f84ff76a5db63e7c9a5661c0b9b24d2eb1184998dae80":
        (RIGHT_NAME, "F117_EXTERNAL_4", 611, 315),
    "faa547c4b229826ff3d3e2bb4672021dbb8201343a62d43f1ecba0f6a7836d39":
        (RIGHT_NAME, "F117A_external_decals_new", 2, 4),
    "4f21fa687a2aa41be419c32ff77bd86635492b4a527b1093442eba18c5160521":
        (RIGHT_NAME, "F117A_external_decals_new", 2, 4),
    "add1ee0b56e5776a0b3a1f65daa1483ddc713c1ec05a7ffb8d37c29eb496c072":
        (RIGHT_NAME, "F117A_external_decals_new", 2, 4),
    "af22a427553490ea841e011575ea08dbb9e203c095d0f6870636a062093f5539":
        (RIGHT_NAME, "F117_ext_glass", 159, 96),
    "4fa34d72121786011a54bbe7eb763e0086c325286a88bca7e94cd157194866da":
        (RIGHT_NAME, "F117_ext_glass", 164, 98),
    "f18e93c435281f25e19d6467f28618dd53d183d7726997598a26184b99db8409":
        (RIGHT_NAME, "F117A_external_decals_new", 2, 4),
    "9e6dcd80fbbc360cf2eaf055568a08f1bcbf2df68d6e752833116ccfddaad0f9":
        (RIGHT_NAME, "F117A_external_decals_new", 2, 4),
    "25b347dc9acba2cebd644e48c1b47237a20e9190231f54d812b4e0688c8493cd":
        (RIGHT_NAME, "LIGHTS", 124, 62),
    "cf07ef881384dbe25fc840004109ceb6f4a97a7029c28881fcb870634d8139a0":
        (RIGHT_NAME, "LIGHTS", 124, 62),
    "c2dd1fc9ee5e2cc5509103fbe4c6568502a3d9a21cdcc818238923fb52529365":
        (RIGHT_NAME, "LIGHTS", 124, 62),
    "6576a51a108246495bcdbac084a1b0ae7fa2af356f567f2ce3a11b491439bd3c":
        (RIGHT_NAME, "LIGHTS", 124, 62),
}

# Canonical cycles start at maximum Z and follow the source ring toward the
# higher-Y neighbor. Coordinates are Blender world space, not Unity-mirrored.
EXPECTED_ROOT_POINTS = {
    LEFT_NAME: tuple(Vector(point) for point in (
        (2.144634485245, -0.067115396261, 4.345559597015),
        (2.301868915558, -0.021621122956, 3.315155029297),
        (2.657841444016, 0.082936033607, 0.980981886387),
        (2.572439908981, 0.152374669909, -0.240178912878),
        (2.446333169937, 0.139604493976, -2.216584205627),
        (2.496616125107, 0.077349387109, -4.315903663635),
        (2.332198381424, -0.288999050856, -2.902988195419),
        (2.343372583389, -0.315337985754, 0.504489660263),
        (2.750426054001, -0.116600200534, 2.168820619583),
        (3.020997047424, -0.080683700740, 2.212718009949),
        (2.475066423416, -0.073500484228, 3.567368745804),
    )),
    RIGHT_NAME: tuple(Vector(point) for point in (
        (-2.142585039139, -0.067612260580, 4.345559597015),
        (-2.295828819275, -0.022916095331, 3.343090057373),
        (-2.657388448715, 0.082439169288, 0.980981886387),
        (-2.559216737747, 0.152675941586, -0.240178912878),
        (-2.445879936218, 0.139905795455, -2.216584205627),
        (-2.489778041840, 0.102393105626, -3.342765569687),
        (-2.496163129807, 0.077650718391, -4.315903663635),
        (-2.331745386124, -0.308651298285, -2.902988195419),
        (-2.343717575073, -0.315834850073, 0.502893447876),
        (-2.751569509506, -0.116298899055, 2.165627479553),
        (-3.020544052124, -0.080382399261, 2.212718009949),
        (-2.476209402084, -0.073199182749, 3.556194782257),
    )),
}
EXPECTED_ROOT_TRIANGLES = {
    LEFT_NAME: ((3, 4, 5), (3, 5, 6), (2, 3, 6), (1, 2, 6),
                (1, 6, 7), (0, 1, 8), (8, 1, 7)),
    RIGHT_NAME: ((4, 5, 6), (3, 4, 6), (3, 6, 7), (2, 3, 7),
                 (1, 2, 7), (1, 7, 8), (0, 1, 9), (9, 1, 8)),
}
EXPECTED_FULL_ROOT_PERIMETER = {LEFT_NAME: 17.898011930, RIGHT_NAME: 17.900463268}
EXPECTED_ROOT_PERIMETER = {LEFT_NAME: 15.866261054, RIGHT_NAME: 15.922763401}
EXPECTED_ROOT_SURFACE_AREA = {LEFT_NAME: 3.220522968, RIGHT_NAME: 3.264000737}
EXPECTED_ROOT_MIN_TRIANGLE_AREA = {LEFT_NAME: 0.163269538, RIGHT_NAME: 0.161747771}
EXPECTED_ROOT_MIN_ANGLE = {LEFT_NAME: 9.340838955, RIGHT_NAME: 9.673754549}

# These are the only source faces moved across ownership.  The original whole-
# component fingerprints above remain the first proof; each shoulder is then
# identified independently by its exact material and winding-preserving ring
# indices so polygon ordering cannot silently select a different face.
EXPECTED_SHOULDER_TRIANGLES = {
    LEFT_NAME: (("F117_EXTERNAL_3", (0, 10, 1)),
                ("F117_EXTERNAL_3", (10, 9, 1))),
    RIGHT_NAME: (("F117_EXTERNAL_4", (11, 0, 1)),
                 ("F117_EXTERNAL_4", (11, 1, 10))),
}
# Exact authored corner normals from the intact continuous production exterior.
# BMesh point-merge correctly closes the measured root gap, but recomputes the
# four transferred shoulder faces unless these source normals are restored.
# That normal discontinuity is visibly misread as a deformed wedge beside both
# intakes even though the vertices and triangles remain position-correct.
EXPECTED_SHOULDER_CORNER_NORMALS = {
    LEFT_NAME: {
        0: Vector((0.189970925, 0.979255557, 0.070494764)),
        1: Vector((0.230806291, 0.968267083, 0.095850416)),
        9: Vector((0.268091351, 0.956914604, 0.111541383)),
        10: Vector((0.205618039, 0.975333750, 0.080283217)),
    },
    RIGHT_NAME: {
        0: Vector((-0.189996243, 0.979252100, 0.070475250)),
        1: Vector((-0.230792403, 0.968271494, 0.095838994)),
        10: Vector((-0.268100739, 0.956911325, 0.111546002)),
        11: Vector((-0.205485180, 0.974692225, 0.088040039)),
    },
}
EXPECTED_SNAP_DISTANCES = {
    LEFT_NAME: (
        (0.000945057114,), (0.001067417419,), (0.000904502289,),
        (0.000777495004,), (0.000862452427,),
        (0.001313082895, 0.000900339743), (0.000531315269,),
        (0.000318501328,), (0.000308534829,), (0.000850743307,),
        (0.000268222703, 0.000795308892),
    ),
    RIGHT_NAME: (
        (0.000734633570,), (0.000388263512,), (0.000845111855,),
        (0.000605034533,), (0.000759055259,), (0.000987052442,),
        (0.000679609347, 0.001087406204), (0.000740367544,),
        (0.000644235567,), (0.000724622139,), (0.000627300214,),
        (0.000382847236, 0.001158501057),
    ),
}
# The aircraft-right ring is Blender -X.  Its central shell requires exactly
# two authored subdivisions at the measured source edges before normalization.
RIGHT_CENTRAL_SPLITS = (
    (
        Vector((-2.476209402, -0.073199183, 3.556194782)),
        Vector((-1.054540873, -0.055811107, 7.073643684)),
        Vector((-3.020856619, -0.079876356, 2.212917328)),
        0.723557372106,
    ),
    (
        Vector((-2.295828819, -0.022916095, 3.343090057)),
        Vector((-2.142491817, -0.067140669, 4.345004082)),
        Vector((-2.657849312, 0.082470790, 0.981689572)),
        0.297882173218,
    ),
)


def position_key(point: Vector) -> tuple[int, int, int]:
    return tuple(round(value / STRICT_WELD) for value in point)


def euclidean_vertex_roots(source):
    """Deterministically weld by Euclidean distance, not rounded-cell identity."""
    points = [source.matrix_world @ vertex.co for vertex in source.data.vertices]
    parents = list(range(len(points)))

    def find(index):
        while parents[index] != index:
            parents[index] = parents[parents[index]]
            index = parents[index]
        return index

    def union(first, second):
        first, second = find(first), find(second)
        if first != second:
            lower, upper = sorted((first, second))
            parents[upper] = lower

    cells = defaultdict(list)
    limit_squared = STRICT_WELD * STRICT_WELD
    for index, point in enumerate(points):
        cell = tuple(math.floor(value / STRICT_WELD) for value in point)
        for x_offset in (-1, 0, 1):
            for y_offset in (-1, 0, 1):
                for z_offset in (-1, 0, 1):
                    neighbor = (cell[0] + x_offset, cell[1] + y_offset, cell[2] + z_offset)
                    for candidate in cells.get(neighbor, ()):
                        if (points[candidate] - point).length_squared <= limit_squared:
                            union(index, candidate)
        cells[cell].append(index)
    return points, [find(index) for index in range(len(points))]


def connected_components(source, polygon_indices=None):
    mesh = source.data
    owned = set(range(len(mesh.polygons)) if polygon_indices is None else polygon_indices)
    _, vertex_roots = euclidean_vertex_roots(source)
    root_to_polygons = defaultdict(set)
    for polygon_index in owned:
        for root in {vertex_roots[index] for index in mesh.polygons[polygon_index].vertices}:
            root_to_polygons[root].add(polygon_index)

    remaining = set(owned)
    components = []
    while remaining:
        seed = min(remaining)
        remaining.remove(seed)
        component = {seed}
        pending = deque((seed,))
        while pending:
            polygon_index = pending.popleft()
            neighbors = set()
            for vertex_index in mesh.polygons[polygon_index].vertices:
                neighbors.update(root_to_polygons[vertex_roots[vertex_index]])
            discovered = neighbors & remaining
            remaining.difference_update(discovered)
            component.update(discovered)
            pending.extend(sorted(discovered))
        components.append(component)
    components.sort(key=min)
    return components, vertex_roots


def canonical_cycle(values):
    values = tuple(values)
    return min(values[offset:] + values[:offset] for offset in range(len(values)))


def geometry_digest(source, polygon_indices, include_material=False, keys=None):
    """V1 order-independent digest that preserves winding and duplicate faces."""
    mesh = source.data
    polygon_indices = tuple(sorted(polygon_indices))
    if keys is None:
        world = source.matrix_world
        keys = [position_key(world @ vertex.co) for vertex in mesh.vertices]
    vertices = sorted({keys[index] for polygon_index in polygon_indices
                       for index in mesh.polygons[polygon_index].vertices})
    faces = []
    for polygon_index in polygon_indices:
        polygon = mesh.polygons[polygon_index]
        face = canonical_cycle(tuple(keys[index] for index in polygon.vertices))
        if include_material:
            if polygon.material_index >= len(mesh.materials):
                raise RuntimeError(f"{source.name} has an invalid material index")
            material = mesh.materials[polygon.material_index]
            if material is None:
                raise RuntimeError(f"{source.name} has an empty material slot")
            faces.append((material.name, face))
        else:
            faces.append(face)
    faces.sort()

    header = MATERIAL_GEOMETRY_HASH_HEADER if include_material else COMPONENT_HASH_HEADER
    payload = bytearray(header)
    payload += struct.pack(">Q", FINGERPRINT_SCALE)
    payload += struct.pack(">Q", len(vertices))
    for key in vertices:
        payload += struct.pack(">qqq", *key)
    payload += struct.pack(">Q", len(faces))
    for record in faces:
        if include_material:
            material_name, face = record
            encoded = material_name.encode("utf-8")
            payload += struct.pack(">I", len(encoded)) + encoded
        else:
            face = record
        payload += struct.pack(">I", len(face))
        for key in face:
            payload += struct.pack(">qqq", *key)
    return hashlib.sha256(payload).hexdigest()


def component_set_digest(digests):
    raw = sorted(bytes.fromhex(digest) for digest in digests)
    payload = bytearray(COMPONENT_SET_HASH_HEADER)
    payload += struct.pack(">I", len(raw))
    for digest in raw:
        payload += digest
    return hashlib.sha256(payload).hexdigest()


def identify_shoulder_polygons(source, component, owner):
    """Identify only the four measured forward shoulder faces.

    The containing whole-wing component is fingerprinted before this function
    runs.  Matching material plus winding-preserving world-space geometry then
    proves that ownership is transferred from exactly the intended source
    faces, independent of polygon ordering.
    """
    expected = {}
    root = EXPECTED_ROOT_POINTS[owner]
    for material_name, ring_indices in EXPECTED_SHOULDER_TRIANGLES[owner]:
        face = canonical_cycle(tuple(position_key(root[index]) for index in ring_indices))
        signature = (material_name, face)
        if signature in expected:
            raise RuntimeError(f"{owner} shoulder contract contains a duplicate face")
        expected[signature] = ring_indices

    matches = defaultdict(list)
    world = source.matrix_world
    mesh = source.data
    for polygon_index in component:
        polygon = mesh.polygons[polygon_index]
        if polygon.material_index >= len(mesh.materials):
            raise RuntimeError(f"{owner} shoulder candidate has an invalid material index")
        material = mesh.materials[polygon.material_index]
        if material is None:
            continue
        face = canonical_cycle(tuple(
            position_key(world @ mesh.vertices[index].co) for index in polygon.vertices))
        signature = (material.name, face)
        if signature in expected:
            matches[signature].append(polygon_index)

    invalid = {
        signature: indices for signature, indices in matches.items() if len(indices) != 1
    }
    missing = set(expected) - set(matches)
    if missing or invalid:
        raise RuntimeError(
            f"{owner} measured shoulder faces changed "
            f"(missing={sorted(missing)}, invalid={invalid})")
    shoulders = {indices[0] for indices in matches.values()}
    if len(shoulders) != 2:
        raise RuntimeError(f"{owner} must transfer exactly two shoulder polygons")
    return shoulders


def restore_and_validate_shoulder_normals(section):
    """Restore the intact source's split normals on the four shoulder faces.

    Geometry/material validation runs before this repair.  Face lookup uses the
    same measured world-space ring and material contract as ownership transfer,
    so this cannot paint over an unrelated or malformed face.
    """
    if section.name != CENTRAL_NAME:
        raise RuntimeError("Shoulder normals belong only to the central damage section")
    mesh = section.data
    normals = [corner.vector.copy() for corner in mesh.corner_normals]
    normal_matrix = section.matrix_world.to_3x3().inverted().transposed()
    inverse_normal_matrix = normal_matrix.inverted()
    repaired_faces = 0

    for owner, expected_faces in EXPECTED_SHOULDER_TRIANGLES.items():
        ring = EXPECTED_ROOT_POINTS[owner]
        expected_normals = EXPECTED_SHOULDER_CORNER_NORMALS[owner]
        for material_name, ring_indices in expected_faces:
            targets = [ring[index] for index in ring_indices]
            matches = []
            for polygon in mesh.polygons:
                if polygon.material_index >= len(mesh.materials) or len(polygon.vertices) != 3:
                    continue
                material = mesh.materials[polygon.material_index]
                if material is None or material.name != material_name:
                    continue
                points = [section.matrix_world @ mesh.vertices[index].co
                          for index in polygon.vertices]
                if all(any((point - target).length <= RING_COORDINATE_TOLERANCE
                           for point in points) for target in targets):
                    matches.append(polygon)
            if len(matches) != 1:
                raise RuntimeError(
                    f"{owner} shoulder-normal face {ring_indices} matched {len(matches)} polygons")
            polygon = matches[0]
            for loop_index in polygon.loop_indices:
                point = section.matrix_world @ \
                    mesh.vertices[mesh.loops[loop_index].vertex_index].co
                matching_ring = [index for index in ring_indices
                                 if (point - ring[index]).length <= RING_COORDINATE_TOLERANCE]
                if len(matching_ring) != 1:
                    raise RuntimeError(
                        f"{owner} shoulder-normal corner no longer matches its measured ring")
                world_normal = expected_normals[matching_ring[0]].normalized()
                normals[loop_index] = (inverse_normal_matrix @ world_normal).normalized()
            repaired_faces += 1

    if repaired_faces != 4:
        raise RuntimeError(f"Restored {repaired_faces} shoulder-normal faces; expected four")
    mesh.normals_split_custom_set(normals)
    mesh.update()

    failures = []
    for owner, expected_faces in EXPECTED_SHOULDER_TRIANGLES.items():
        ring = EXPECTED_ROOT_POINTS[owner]
        expected_normals = EXPECTED_SHOULDER_CORNER_NORMALS[owner]
        material_names = {material for material, _ in expected_faces}
        expected_sets = [set(indices) for _, indices in expected_faces]
        for polygon in mesh.polygons:
            if polygon.material_index >= len(mesh.materials):
                continue
            material = mesh.materials[polygon.material_index]
            if material is None or material.name not in material_names or len(polygon.vertices) != 3:
                continue
            corner_ring = []
            for loop_index in polygon.loop_indices:
                point = section.matrix_world @ \
                    mesh.vertices[mesh.loops[loop_index].vertex_index].co
                matches = [index for index, target in enumerate(ring)
                           if (point - target).length <= RING_COORDINATE_TOLERANCE]
                if len(matches) != 1:
                    corner_ring = []
                    break
                corner_ring.append(matches[0])
            if set(corner_ring) not in expected_sets:
                continue
            for loop_index, ring_index in zip(polygon.loop_indices, corner_ring):
                actual = (normal_matrix @ mesh.corner_normals[loop_index].vector).normalized()
                expected = expected_normals[ring_index].normalized()
                if actual.dot(expected) < 0.9999999:
                    failures.append((owner, polygon.index, ring_index, actual, expected))
    if failures:
        raise RuntimeError(f"Shoulder custom-normal restoration failed: {failures}")
    print("SHOULDER_NORMALS=restored-and-validated,faces:4")


def classify_measured_sections(source, components):
    """Prove every source island's geometry before assigning any ownership."""
    fingerprint_keys = [
        position_key(source.matrix_world @ vertex.co) for vertex in source.data.vertices]
    component_digests = [(geometry_digest(source, component, keys=fingerprint_keys), component)
                         for component in components]
    digests = [digest for digest, _ in component_digests]
    if len(set(digests)) != len(digests):
        raise RuntimeError("Production exterior contains duplicate component fingerprints")
    if component_set_digest(digests) != EXPECTED_COMPONENT_SET_DIGEST:
        raise RuntimeError("Production exterior component geometry changed")
    if geometry_digest(source, range(len(source.data.polygons)), include_material=True,
                       keys=fingerprint_keys) != \
            EXPECTED_SOURCE_MATERIAL_GEOMETRY_DIGEST:
        raise RuntimeError("Production exterior geometry/material contract changed")

    by_digest = {digest: component for digest, component in component_digests}
    missing = sorted(set(EXPECTED_WING_COMPONENTS) - set(by_digest))
    if missing:
        raise RuntimeError(f"Measured whole-wing components are missing: {missing}")

    owners = {LEFT_NAME: [], RIGHT_NAME: []}
    for digest, (owner, expected_material, expected_polygons, expected_vertices) in \
            EXPECTED_WING_COMPONENTS.items():
        component = by_digest[digest]
        materials = set()
        welded_keys = set()
        for polygon_index in component:
            polygon = source.data.polygons[polygon_index]
            if polygon.material_index >= len(source.data.materials):
                raise RuntimeError(f"{owner} component has an invalid material index")
            material = source.data.materials[polygon.material_index]
            materials.add(None if material is None else material.name)
            welded_keys.update(
                position_key(source.matrix_world @ source.data.vertices[index].co)
                for index in polygon.vertices)
        actual = (len(component), len(welded_keys), materials)
        expected = (expected_polygons, expected_vertices, {expected_material})
        if actual != expected:
            raise RuntimeError(f"{owner} component {digest[:12]} contract changed: {actual}")
        owners[owner].append(component)

    original_left = set().union(*owners[LEFT_NAME])
    original_right = set().union(*owners[RIGHT_NAME])
    original_central = set(range(len(source.data.polygons))) - original_left - original_right
    if (original_left & original_right) or \
            len(original_central | original_left | original_right) != len(source.data.polygons):
        raise RuntimeError("Measured section ownership is not an exact partition")

    # Prove the original whole-component ownership before splitting the two
    # measured shoulder faces out of each main wing island.
    original_sections = {
        CENTRAL_NAME: original_central,
        LEFT_NAME: original_left,
        RIGHT_NAME: original_right,
    }
    expected_sets = {
        CENTRAL_NAME: EXPECTED_CENTRAL_COMPONENT_SET_DIGEST,
        LEFT_NAME: EXPECTED_SIDE_COMPONENT_SET_DIGEST[LEFT_NAME],
        RIGHT_NAME: EXPECTED_SIDE_COMPONENT_SET_DIGEST[RIGHT_NAME],
    }
    expected_counts = {CENTRAL_NAME: 181, LEFT_NAME: 12, RIGHT_NAME: 12}
    expected_original_polygons = {CENTRAL_NAME: 81286, LEFT_NAME: 1573, RIGHT_NAME: 1440}
    for name, owned in original_sections.items():
        owned_digests = [digest for digest, component in component_digests
                         if component <= owned]
        if len(owned) != expected_original_polygons[name]:
            raise RuntimeError(f"{name} owns {len(owned)} skin polygons; expected "
                               f"{expected_original_polygons[name]}")
        if len(owned_digests) != expected_counts[name]:
            raise RuntimeError(f"{name} component ownership count changed")
        if component_set_digest(owned_digests) != expected_sets[name]:
            raise RuntimeError(f"{name} component ownership fingerprint changed")

    left_main = by_digest[
        "3de6265b555663190a67eae4ba1ef252fbe1963588e92087028fd6b09cf787c1"]
    right_main = by_digest[
        "b340a9568556735b352f84ff76a5db63e7c9a5661c0b9b24d2eb1184998dae80"]
    left_shoulders = identify_shoulder_polygons(source, left_main, LEFT_NAME)
    right_shoulders = identify_shoulder_polygons(source, right_main, RIGHT_NAME)
    left = original_left - left_shoulders
    right = original_right - right_shoulders
    central = original_central | left_shoulders | right_shoulders
    section_polygons = {CENTRAL_NAME: central, LEFT_NAME: left, RIGHT_NAME: right}
    if any(len(owned) != EXPECTED_SKIN_POLYGONS[name]
           for name, owned in section_polygons.items()):
        counts = {name: len(owned) for name, owned in section_polygons.items()}
        raise RuntimeError(f"Shoulder-reassigned skin polygon counts changed: {counts}")
    if (central & left) or (central & right) or (left & right) or \
            len(central | left | right) != len(source.data.polygons):
        raise RuntimeError("Shoulder reassignment is not an exact source partition")
    return central, left, right, left_main, right_main


def boundary_loops(source, component, vertex_keys):
    edge_counts = defaultdict(int)
    key_positions = {}
    for polygon_index in component:
        polygon = source.data.polygons[polygon_index]
        keys = [vertex_keys[index] for index in polygon.vertices]
        for vertex_index, key in zip(polygon.vertices, keys):
            key_positions.setdefault(key, source.matrix_world @ source.data.vertices[vertex_index].co)
        for index in range(len(keys)):
            edge = tuple(sorted((keys[index], keys[(index + 1) % len(keys)])))
            edge_counts[edge] += 1

    adjacency = defaultdict(list)
    for (first, second), count in edge_counts.items():
        if count == 1:
            adjacency[first].append(second)
            adjacency[second].append(first)

    remaining = set(adjacency)
    loops = []
    while remaining:
        seed = remaining.pop()
        connected = {seed}
        pending = [seed]
        while pending:
            key = pending.pop()
            for neighbor in adjacency[key]:
                if neighbor not in connected:
                    connected.add(neighbor)
                    remaining.discard(neighbor)
                    pending.append(neighbor)
        if any(len(adjacency[key]) != 2 for key in connected):
            continue
        ordered = [seed]
        previous = None
        current = seed
        while True:
            choices = [neighbor for neighbor in adjacency[current] if neighbor != previous]
            if not choices:
                raise RuntimeError("Closed authored boundary unexpectedly terminated")
            following = choices[0]
            if following == seed:
                break
            if following in ordered:
                raise RuntimeError("Authored boundary revisited a vertex before closing")
            ordered.append(following)
            previous, current = current, following
        loops.append([key_positions[key] for key in ordered])
    return loops


def identify_root_loop(source, component, vertex_roots, owner):
    expected = EXPECTED_ROOT_POINTS[owner]
    matches = []
    for loop in boundary_loops(source, component, vertex_roots):
        if len(loop) != len(expected):
            continue
        mapped = []
        valid = True
        for point in loop:
            indices = [index for index, target in enumerate(expected)
                       if (point - target).length <= RING_COORDINATE_TOLERANCE]
            if len(indices) != 1:
                valid = False
                break
            mapped.append(indices[0])
        if not valid or set(mapped) != set(range(len(expected))):
            continue
        steps = {(mapped[(index + 1) % len(mapped)] - mapped[index]) % len(mapped)
                 for index in range(len(mapped))}
        if steps not in ({1}, {len(mapped) - 1}):
            raise RuntimeError(f"{owner} root vertices do not form the measured cyclic order")
        by_expected = {mapped[index]: loop[index] for index in range(len(loop))}
        matches.append([by_expected[index] for index in range(len(expected))])
    if len(matches) != 1:
        raise RuntimeError(f"{owner} must contain exactly one measured root cycle; found {len(matches)}")
    points = matches[0]
    perimeter = sum((points[(index + 1) % len(points)] - points[index]).length
                    for index in range(len(points)))
    if abs(perimeter - EXPECTED_FULL_ROOT_PERIMETER[owner]) > MEASUREMENT_TOLERANCE:
        raise RuntimeError(f"{owner} measured root perimeter changed: {perimeter:.9f}")
    return points


def structural_root_points(owner):
    """Return the measured inner break cycle after the center shoulder patch."""
    points = EXPECTED_ROOT_POINTS[owner][1:-1]
    if len(points) not in (9, 10):
        raise RuntimeError(f"{owner} structural root point contract changed")
    return points


def structure_material():
    material = bpy.data.materials.get(STRUCTURE_MATERIAL_NAME)
    if material is None:
        material = bpy.data.materials.new(STRUCTURE_MATERIAL_NAME)
    material.diffuse_color = (0.035, 0.038, 0.042, 1.0)
    material.metallic = 0.12
    material.roughness = 0.72
    return material


def triangle_metrics(points, triangles):
    areas = []
    angles = []
    for indices in triangles:
        first, second, third = (points[index] for index in indices)
        edges = ((second - first), (third - second), (first - third))
        area = edges[0].cross(third - first).length * 0.5
        areas.append(area)
        lengths = [edge.length for edge in edges]
        for index in range(3):
            before = lengths[(index - 1) % 3]
            after = lengths[index]
            opposite = lengths[(index + 1) % 3]
            cosine = max(-1.0, min(1.0,
                (before * before + after * after - opposite * opposite) /
                (2.0 * before * after)))
            angles.append(math.degrees(math.acos(cosine)))
    return sum(areas), min(areas), min(angles)


def validate_disk_triangulation(owner, triangles):
    expected_count = len(structural_root_points(owner))
    edge_counts = Counter()
    for triangle in triangles:
        if len(set(triangle)) != 3 or any(index < 0 or index >= expected_count for index in triangle):
            raise RuntimeError(f"{owner} structural triangulation has invalid indices")
        for offset in range(3):
            edge_counts[tuple(sorted((triangle[offset], triangle[(offset + 1) % 3])))] += 1
    perimeter = {tuple(sorted((index, (index + 1) % expected_count)))
                 for index in range(expected_count)}
    if {edge for edge, count in edge_counts.items() if count == 1} != perimeter:
        raise RuntimeError(f"{owner} structural triangulation does not use only the authored boundary")
    if any(count not in (1, 2) for count in edge_counts.values()):
        raise RuntimeError(f"{owner} structural triangulation is non-manifold")
    if expected_count - len(edge_counts) + len(triangles) != 1:
        raise RuntimeError(f"{owner} structural triangulation is not a single disk")


def triangulate_ring(points, owner):
    triangles = tessellate_polygon([points])
    indices = []
    for triangle in triangles:
        item = []
        for point in triangle:
            if isinstance(point, int):
                if point < 0 or point >= len(points):
                    raise RuntimeError("Constrained root triangulation returned an invalid boundary index")
                item.append(point)
                continue
            matches = [index for index, candidate in enumerate(points) if (candidate - point).length <= STRICT_WELD]
            if len(matches) != 1:
                raise RuntimeError("Constrained root triangulation did not map uniquely to its boundary")
            item.append(matches[0])
        indices.append(tuple(item))
    if len(indices) != len(points) - 2:
        raise RuntimeError("Constrained root triangulation added or lost structural triangles")
    expected = EXPECTED_ROOT_TRIANGLES[owner]
    if {tuple(sorted(triangle)) for triangle in indices} != \
            {tuple(sorted(triangle)) for triangle in expected}:
        raise RuntimeError(f"{owner} Blender tessellation no longer matches the measured root surface")
    validate_disk_triangulation(owner, expected)
    area, minimum_area, minimum_angle = triangle_metrics(points, expected)
    if abs(area - EXPECTED_ROOT_SURFACE_AREA[owner]) > MEASUREMENT_TOLERANCE:
        raise RuntimeError(f"{owner} root surface area changed: {area:.9f}")
    if abs(minimum_area - EXPECTED_ROOT_MIN_TRIANGLE_AREA[owner]) > MEASUREMENT_TOLERANCE:
        raise RuntimeError(f"{owner} root minimum triangle area changed: {minimum_area:.9f}")
    if abs(minimum_angle - EXPECTED_ROOT_MIN_ANGLE[owner]) > 0.001:
        raise RuntimeError(f"{owner} root minimum angle changed: {minimum_angle:.6f}")
    return expected


def normalize_central_boundary(bm, section, root_ring, owner, split_specs):
    shell_materials = {
        index
        for index, material in enumerate(section.data.materials)
        if material is not None and material.name in {"F117_EXTERNAL_1", "F117_EXTERNAL_2"}
    }
    if len(shell_materials) != 2:
        raise RuntimeError("Central upper/lower shell materials are unavailable for root normalization")

    boundary_edges = [
        edge for edge in bm.edges
        if len(edge.link_faces) == 1 and edge.link_faces[0].material_index in shell_materials
    ]
    boundary_vertices = {vertex for edge in boundary_edges for vertex in edge.verts}
    snap_vertices = defaultdict(set)
    split_target_indices = set()
    for target, _, _, _ in split_specs:
        matches = [index for index, point in enumerate(root_ring)
                   if (point - target).length <= STRICT_WELD]
        if len(matches) != 1:
            raise RuntimeError("A measured central edge split does not map to exactly one root vertex")
        split_target_indices.add(matches[0])

    for ring_index, target in enumerate(root_ring):
        nearby = [
            vertex for vertex in boundary_vertices
            if ((section.matrix_world @ vertex.co) - target).length <= ROOT_SNAP_LIMIT
        ]
        if not nearby and ring_index not in split_target_indices:
            raise RuntimeError(f"Central root point {ring_index} has no measured source-boundary vertex")
        snap_vertices[ring_index].update(nearby)

    split_count = 0
    for target, expected_first, expected_second, expected_factor in split_specs:
        ring_matches = [index for index, point in enumerate(root_ring)
                        if (point - target).length <= STRICT_WELD]
        if len(ring_matches) != 1:
            raise RuntimeError("A measured central edge split does not map to exactly one root vertex")
        ring_index = ring_matches[0]
        matching_edges = []
        for edge in boundary_edges:
            first = section.matrix_world @ edge.verts[0].co
            second = section.matrix_world @ edge.verts[1].co
            direct = (first - expected_first).length <= STRICT_WELD and \
                     (second - expected_second).length <= STRICT_WELD
            reverse = (first - expected_second).length <= STRICT_WELD and \
                      (second - expected_first).length <= STRICT_WELD
            if direct or reverse:
                matching_edges.append((edge, expected_factor if direct else 1.0 - expected_factor))
        if len(matching_edges) != 1:
            raise RuntimeError("A measured central root edge must map to exactly one source edge")
        edge, factor = matching_edges[0]
        if not edge.is_valid:
            raise RuntimeError("A measured central root edge became invalid before subdivision")
        _, new_vertex = bmesh.utils.edge_split(edge, edge.verts[0], factor)
        snap_vertices[ring_index].add(new_vertex)
        split_count += 1

    assignments = defaultdict(list)
    for ring_index, vertices in snap_vertices.items():
        for vertex in vertices:
            assignments[vertex].append(ring_index)
    overlaps = {vertex: indices for vertex, indices in assignments.items() if len(indices) != 1}
    if overlaps:
        raise RuntimeError(f"{owner} central root snap assignments overlap")

    maximum_move = 0.0
    measured_distances = []
    for ring_index in range(len(root_ring)):
        vertices = snap_vertices[ring_index]
        target = root_ring[ring_index]
        local_target = section.matrix_world.inverted() @ target
        source_points = sorted(
            (section.matrix_world @ vertex.co for vertex in vertices),
            key=lambda point: (point.x, point.y, point.z))
        unique_points = []
        for point in source_points:
            if not any((point - candidate).length <= STRICT_WELD
                       for candidate in unique_points):
                unique_points.append(point)
        distances = sorted((point - target).length for point in unique_points)
        measured_distances.append(tuple(distances))
        for vertex in vertices:
            maximum_move = max(maximum_move, ((section.matrix_world @ vertex.co) - target).length)
            vertex.co = local_target
    expected_distances = EXPECTED_SNAP_DISTANCES[owner]
    if len(measured_distances) != len(expected_distances):
        raise RuntimeError(f"{owner} central root snap contract has the wrong length")
    for ring_index, (actual, expected) in enumerate(zip(measured_distances, expected_distances)):
        actual = tuple(sorted(actual))
        expected = tuple(sorted(expected))
        if len(actual) != len(expected) or any(
                abs(first - second) > MEASUREMENT_TOLERANCE
                for first, second in zip(actual, expected)):
            raise RuntimeError(
                f"{owner} central root snap distances changed at point {ring_index}: {actual}")
    if maximum_move > ROOT_SNAP_LIMIT + 1.0e-7:
        raise RuntimeError("Central root normalization exceeded its measured movement budget")
    return maximum_move, split_count


def weld_center_shoulders(bm, section, full_ring, owner):
    """Join only the transferred shoulder patch to the normalized center shell."""
    shell_materials = {
        index for index, material in enumerate(section.data.materials)
        if material is not None and material.name in {"F117_EXTERNAL_1", "F117_EXTERNAL_2"}
    }
    shoulder_names = {material for material, _ in EXPECTED_SHOULDER_TRIANGLES[owner]}
    shoulder_materials = {
        index for index, material in enumerate(section.data.materials)
        if material is not None and material.name in shoulder_names
    }
    if len(shell_materials) != 2 or len(shoulder_materials) != 1:
        raise RuntimeError(f"{owner} shoulder weld material contract changed")

    used_ring_indices = sorted({
        index for _, triangle in EXPECTED_SHOULDER_TRIANGLES[owner] for index in triangle
    })
    expected_indices = [0, 1, len(full_ring) - 2, len(full_ring) - 1]
    if used_ring_indices != expected_indices:
        raise RuntimeError(f"{owner} shoulder weld point contract changed")

    merge_records = []
    for ring_index in used_ring_indices:
        target = full_ring[ring_index]
        local_target = section.matrix_world.inverted() @ target
        candidates = [
            vertex for vertex in bm.verts
            if ((section.matrix_world @ vertex.co) - target).length <= STRICT_WELD and
            any(face.material_index in shell_materials | shoulder_materials
                for face in vertex.link_faces)
        ]
        shell_vertices = {
            vertex for vertex in candidates
            if any(face.material_index in shell_materials for face in vertex.link_faces)
        }
        shoulder_vertices = {
            vertex for vertex in candidates
            if any(face.material_index in shoulder_materials for face in vertex.link_faces)
        }
        if not shell_vertices or not shoulder_vertices:
            raise RuntimeError(
                f"{owner} shoulder point {ring_index} does not join shell and shoulder geometry")
        merge_vertices = shell_vertices | shoulder_vertices
        before = len(bm.verts)
        bmesh.ops.pointmerge(bm, verts=list(merge_vertices), merge_co=local_target)
        removed = before - len(bm.verts)
        if removed != len(merge_vertices) - 1:
            raise RuntimeError(f"{owner} shoulder point {ring_index} did not weld deterministically")
        merge_records.append((ring_index, len(shell_vertices), len(shoulder_vertices), removed))

    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    if len(merge_records) != 4:
        raise RuntimeError(f"{owner} did not weld exactly four shoulder boundary points")
    return tuple(merge_records)


def duplicate_section(source, name, polygon_indices, root_ring, desired_normal, material,
                      root_owner, normalize_central=False, split_specs=(),
                      normalization_ring=None):
    mesh = source.data.copy()
    mesh.name = name
    section = source.copy()
    section.data = mesh
    section.name = name
    for key in tuple(section.keys()):
        if key.startswith("f117_"):
            del section[key]
    for collection in source.users_collection:
        collection.objects.link(section)
    section.parent = source.parent
    section.matrix_world = source.matrix_world.copy()
    mesh.materials.append(material)
    structure_index = len(mesh.materials) - 1

    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    discard = [face for face in bm.faces if face.index not in polygon_indices]
    bmesh.ops.delete(bm, geom=discard, context="FACES")
    loose = [vertex for vertex in bm.verts if not vertex.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")

    if normalize_central:
        if normalization_ring is None:
            normalization_ring = root_ring
        maximum_move, split_groups = normalize_central_boundary(
            bm, section, normalization_ring, root_owner, split_specs)
        section["f117_root_maximum_snap_mm"] = maximum_move * 1000.0
        section["f117_root_edge_splits"] = split_groups
        if normalization_ring is not root_ring:
            weld_center_shoulders(bm, section, normalization_ring, root_owner)

    local_ring = [section.matrix_world.inverted() @ point for point in root_ring]
    # Structural material needs its own hard normals/UVs, just like stock aircraft
    # structure submeshes.  Duplicate only the exact boundary vertices; do not copy
    # any exterior shell or create separate renderer objects.
    ring_vertices = [bm.verts.new(point) for point in local_ring]
    bm.verts.index_update()

    structural_faces = []
    for triangle in triangulate_ring(root_ring, root_owner):
        first, second, third = (ring_vertices[index] for index in triangle)
        world_first = section.matrix_world @ first.co
        world_second = section.matrix_world @ second.co
        world_third = section.matrix_world @ third.co
        normal = (world_second - world_first).cross(world_third - world_first)
        if normal.dot(desired_normal) < 0.0:
            second, third = third, second
        face = bm.faces.new((first, second, third))
        face.material_index = structure_index
        structural_faces.append(face)

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    mesh.update(calc_edges=True)
    repaired = mesh.validate(verbose=True, clean_customdata=False)
    repaired_materials = mesh.validate_material_indices()
    if repaired or repaired_materials:
        raise RuntimeError(f"{name} contained invalid mesh data that Blender had to repair")
    section["f117_damage_section"] = name
    section["f117_structural_triangles"] = len(structural_faces)
    return section


def structural_face_groups(section):
    material_slots = [
        index for index, material in enumerate(section.data.materials)
        if material is not None and material.name == STRUCTURE_MATERIAL_NAME
    ]
    if len(material_slots) != 1:
        raise RuntimeError(f"{section.name} must have exactly one aircraft-structure material slot")
    structure_index = material_slots[0]
    faces = [polygon for polygon in section.data.polygons
             if polygon.material_index == structure_index]
    expected_faces = EXPECTED_STRUCTURE_TRIANGLES[section.name]
    if len(faces) != expected_faces or any(len(face.vertices) != 3 for face in faces):
        raise RuntimeError(
            f"{section.name} has {len(faces)} structural triangles; expected {expected_faces}")

    world = section.matrix_world
    face_keys = []
    key_positions = {}
    key_to_faces = defaultdict(set)
    face_normals = {}
    for face_index, face in enumerate(faces):
        points = [world @ section.data.vertices[index].co for index in face.vertices]
        keys = tuple(position_key(point) for point in points)
        if len(set(keys)) != 3:
            raise RuntimeError(f"{section.name} has a degenerate structural triangle")
        triangle_key = tuple(sorted(keys))
        if triangle_key in face_normals:
            raise RuntimeError(f"{section.name} has a duplicate structural triangle")
        normal = (points[1] - points[0]).cross(points[2] - points[0])
        if normal.length <= 1.0e-10:
            raise RuntimeError(f"{section.name} has a zero-area structural triangle")
        face_normals[triangle_key] = normal.normalized()
        face_keys.append(keys)
        for key, point in zip(keys, points):
            key_positions.setdefault(key, point)
            key_to_faces[key].add(face_index)

    remaining = set(range(len(faces)))
    groups = []
    while remaining:
        seed = remaining.pop()
        connected = {seed}
        pending = [seed]
        while pending:
            face_index = pending.pop()
            neighbors = {
                neighbor for key in face_keys[face_index]
                for neighbor in key_to_faces[key]
            } & remaining
            remaining.difference_update(neighbors)
            connected.update(neighbors)
            pending.extend(neighbors)
        keys = {key for face_index in connected for key in face_keys[face_index]}
        triangles = {
            tuple(sorted(face_keys[face_index])): face_normals[tuple(sorted(face_keys[face_index]))]
            for face_index in connected
        }
        groups.append({
            "keys": keys,
            "positions": {key: key_positions[key] for key in keys},
            "triangles": triangles,
            "center_x": sum(key_positions[key].x for key in keys) / len(keys),
        })
    return groups


def expected_root_keys(owner):
    return tuple(position_key(point) for point in structural_root_points(owner))


def expected_structural_triangles(owner):
    keys = expected_root_keys(owner)
    return {
        tuple(sorted(keys[index] for index in triangle))
        for triangle in EXPECTED_ROOT_TRIANGLES[owner]
    }


def validate_skin_root_interface(section, owner):
    structure_slots = {
        index for index, material in enumerate(section.data.materials)
        if material is not None and material.name == STRUCTURE_MATERIAL_NAME
    }
    root_keys = expected_root_keys(owner)
    root_key_set = set(root_keys)
    edge_counts = Counter()
    for polygon in section.data.polygons:
        if polygon.material_index in structure_slots:
            continue
        keys = [position_key(section.matrix_world @ section.data.vertices[index].co)
                for index in polygon.vertices]
        for index in range(len(keys)):
            edge_counts[tuple(sorted((keys[index], keys[(index + 1) % len(keys)])))] += 1
    actual = {edge for edge, count in edge_counts.items()
              if count == 1 and set(edge) <= root_key_set}
    expected = {
        tuple(sorted((root_keys[index], root_keys[(index + 1) % len(root_keys)])))
        for index in range(len(root_keys))
    }
    if actual != expected:
        raise RuntimeError(
            f"{section.name} skin does not expose the exact {owner} root cycle "
            f"(missing={sorted(expected - actual)}, extra={sorted(actual - expected)})")
    if any(edge_counts[edge] != 1 for edge in expected):
        raise RuntimeError(f"{section.name} {owner} skin root is not a single boundary cycle")


def validate_structural_group(section, group, owner, outward_x):
    expected_keys = set(expected_root_keys(owner))
    if group["keys"] != expected_keys:
        raise RuntimeError(f"{section.name} structural vertices are not the measured {owner} ring")
    expected_triangles = expected_structural_triangles(owner)
    if set(group["triangles"]) != expected_triangles:
        raise RuntimeError(f"{section.name} does not use the measured {owner} root triangulation")
    validate_disk_triangulation(owner, EXPECTED_ROOT_TRIANGLES[owner])
    for key, expected_point in zip(expected_root_keys(owner), structural_root_points(owner)):
        if key not in group["positions"]:
            raise RuntimeError(f"{section.name} is missing a measured {owner} root point")
        if (group["positions"][key] - expected_point).length > RING_COORDINATE_TOLERANCE:
            raise RuntimeError(f"{section.name} moved a measured {owner} structural root point")
    for normal in group["triangles"].values():
        if normal.x * outward_x <= 0.0:
            raise RuntimeError(f"{section.name} has an inward-facing {owner} structural triangle")
    validate_skin_root_interface(section, owner)


def validate_structural_pairs(sections):
    groups = {name: structural_face_groups(section) for name, section in sections.items()}
    if len(groups[CENTRAL_NAME]) != 2:
        raise RuntimeError("Central structure must contain only the two measured inner root rings")
    if len(groups[LEFT_NAME]) != 1:
        raise RuntimeError("Left wing structure is not the measured 9-vertex root ring")
    if len(groups[RIGHT_NAME]) != 1:
        raise RuntimeError("Right wing structure is not the measured 10-vertex root ring")

    for wing_name, center_outward, wing_outward in (
            (LEFT_NAME, 1.0, -1.0), (RIGHT_NAME, -1.0, 1.0)):
        wing = groups[wing_name][0]
        expected_keys = set(expected_root_keys(wing_name))
        matching = [group for group in groups[CENTRAL_NAME]
                    if group["keys"] == expected_keys]
        if len(matching) != 1:
            raise RuntimeError(f"{wing_name} does not share exactly one root ring with the center section")
        central = matching[0]
        validate_structural_group(sections[CENTRAL_NAME], central, wing_name, center_outward)
        validate_structural_group(sections[wing_name], wing, wing_name, wing_outward)
        for triangle in central["triangles"]:
            if central["triangles"][triangle].dot(wing["triangles"][triangle]) > -0.9999:
                raise RuntimeError(f"{wing_name} structural faces do not oppose their center mates")


def validate_global_triangle_ownership(sections):
    """Reject every structural/exterior overlap across all three owner meshes."""
    groups = defaultdict(list)
    for section_name, section in sections.items():
        world = section.matrix_world
        mesh = section.data
        mesh.calc_loop_triangles()
        for rendered_triangle in mesh.loop_triangles:
            polygon = mesh.polygons[rendered_triangle.polygon_index]
            if polygon.material_index >= len(mesh.materials):
                raise RuntimeError(f"{section_name} contains an invalid material index")
            material = mesh.materials[polygon.material_index]
            if material is None:
                raise RuntimeError(f"{section_name} contains an empty material slot")
            points = [world @ mesh.vertices[index].co for index in rendered_triangle.vertices]
            triangle = tuple(sorted(position_key(point) for point in points))
            normal = (points[1] - points[0]).cross(points[2] - points[0])
            structure = material.name == STRUCTURE_MATERIAL_NAME
            if normal.length <= 1.0e-10 and structure:
                raise RuntimeError(f"{section_name} contains a degenerate structural triangle")
            groups[triangle].append({
                "section": section_name,
                "polygon": polygon.index,
                "material": material.name,
                "structure": structure,
                "normal": normal.normalized() if normal.length > 1.0e-10 else None,
            })

    structural_pairs = 0
    for triangle, records in groups.items():
        structural = [record for record in records if record["structure"]]
        exterior = [record for record in records if not record["structure"]]
        if structural and exterior:
            raise RuntimeError(
                "Aircraft structure overlaps exterior geometry at triangle "
                f"{triangle}: structure={[(item['section'], item['polygon']) for item in structural]}, "
                f"exterior={[(item['section'], item['polygon'], item['material']) for item in exterior]}")
        if not structural:
            continue
        if len(structural) != 2:
            raise RuntimeError(
                f"Structural triangle {triangle} has {len(structural)} owners; expected two")
        owners = {record["section"] for record in structural}
        if CENTRAL_NAME not in owners or len(owners) != 2 or \
                not (owners & {LEFT_NAME, RIGHT_NAME}):
            raise RuntimeError(f"Structural triangle {triangle} has invalid owners: {sorted(owners)}")
        if structural[0]["normal"].dot(structural[1]["normal"]) > -0.9999:
            raise RuntimeError(f"Structural triangle {triangle} does not have opposing owner normals")
        structural_pairs += 1
    expected_pairs = EXPECTED_STRUCTURE_TRIANGLES[LEFT_NAME] + \
        EXPECTED_STRUCTURE_TRIANGLES[RIGHT_NAME]
    if structural_pairs != expected_pairs:
        raise RuntimeError(
            f"Authored structure has {structural_pairs} paired triangles; expected {expected_pairs}")


def validate_section_contract(section):
    if section.type != "MESH" or section.data is None:
        raise RuntimeError(f"Authored damage section is not a mesh: {section.name}")
    if section.data.name != section.name:
        raise RuntimeError(f"{section.name} does not own its matching production mesh")
    if section.data.users != 1:
        raise RuntimeError(f"{section.name} mesh data must have exactly one owner")

    probe = section.data.copy()
    try:
        repaired = probe.validate(verbose=False, clean_customdata=False)
        repaired_materials = probe.validate_material_indices()
    finally:
        bpy.data.meshes.remove(probe)
    if repaired or repaired_materials:
        raise RuntimeError(f"{section.name} contains invalid mesh data")

    if len(section.data.polygons) != EXPECTED_SECTION_POLYGONS[section.name]:
        raise RuntimeError(
            f"{section.name} polygon count changed: {len(section.data.polygons)}; "
            f"expected {EXPECTED_SECTION_POLYGONS[section.name]}")
    if len(section.data.vertices) != EXPECTED_SECTION_VERTICES[section.name]:
        raise RuntimeError(
            f"{section.name} vertex count changed: {len(section.data.vertices)}; "
            f"expected {EXPECTED_SECTION_VERTICES[section.name]}")

    expected_properties = {
        "f117_damage_section": section.name,
        "f117_structural_triangles": EXPECTED_STRUCTURE_TRIANGLES[section.name],
    }
    if section.name == CENTRAL_NAME:
        expected_properties.update({
            "f117_root_edge_splits": 2,
            "f117_root_maximum_snap_mm": max(
                distance for owner in EXPECTED_SNAP_DISTANCES.values()
                for distances in owner for distance in distances) * 1000.0,
        })
    actual_keys = {key for key in section.keys() if key.startswith("f117_")}
    if actual_keys != set(expected_properties):
        raise RuntimeError(f"{section.name} has stale controlled properties: {sorted(actual_keys)}")
    for key, expected in expected_properties.items():
        actual = section[key]
        if isinstance(expected, float):
            if not math.isfinite(float(actual)) or abs(float(actual) - expected) > 0.005:
                raise RuntimeError(f"{section.name} property {key} is stale: {actual}")
        elif actual != expected:
            raise RuntimeError(f"{section.name} property {key} is stale: {actual}")

    structure_slots = [
        index for index, material in enumerate(section.data.materials)
        if material is not None and material.name == STRUCTURE_MATERIAL_NAME
    ]
    if len(structure_slots) != 1:
        raise RuntimeError(f"{section.name} must have exactly one structure material slot")
    structure_index = structure_slots[0]
    skin_polygons = [polygon.index for polygon in section.data.polygons
                     if polygon.material_index != structure_index]
    if len(skin_polygons) != EXPECTED_SKIN_POLYGONS[section.name]:
        raise RuntimeError(f"{section.name} skin polygon count changed")
    fingerprint_keys = [
        position_key(section.matrix_world @ vertex.co) for vertex in section.data.vertices]
    material_digest = geometry_digest(
        section, skin_polygons, include_material=True, keys=fingerprint_keys)
    if material_digest != EXPECTED_AUTHORED_SKIN_MATERIAL_DIGEST[section.name]:
        raise RuntimeError(
            f"{section.name} authored skin geometry/material changed: {material_digest}")

    components, _ = connected_components(section, skin_polygons)
    component_digests = [
        geometry_digest(section, component, keys=fingerprint_keys) for component in components]
    expected_count = 180 if section.name == CENTRAL_NAME else 12
    if len(components) != expected_count:
        raise RuntimeError(f"{section.name} authored skin component count changed: {len(components)}")
    if component_set_digest(component_digests) != \
            EXPECTED_AUTHORED_SKIN_COMPONENT_SET_DIGEST[section.name]:
        raise RuntimeError(
            f"{section.name} authored skin component fingerprint changed: "
            f"{component_set_digest(component_digests)}")
    if section.name in (LEFT_NAME, RIGHT_NAME):
        validate_wing_decal_clearance(section)


def validate_wing_decal_clearance(section):
    """Require every wing badge quad to clear its real supporting skin by 0.5 mm."""
    decal_indices = []
    skin_triangles = []
    world_vertices = [section.matrix_world @ vertex.co for vertex in section.data.vertices]
    for polygon in section.data.polygons:
        material = section.data.materials[polygon.material_index]
        material_name = material.name if material is not None else ""
        if material_name == "F117A_external_decals_new":
            decal_indices.append(polygon.index)
        if not material_name.startswith("F117_EXTERNAL_"):
            continue
        vertices = tuple(polygon.vertices)
        for index in range(1, len(vertices) - 1):
            skin_triangles.append((vertices[0], vertices[index], vertices[index + 1]))
    components, _ = connected_components(section, decal_indices)
    if len(components) != 5:
        raise RuntimeError(
            f"{section.name} has {len(components)} wing-badge components; expected five")
    bvh = BVHTree.FromPolygons(world_vertices, skin_triangles, all_triangles=True)
    minimum_clearance = float("inf")
    for component in components:
        component_vertices = {
            vertex for polygon_index in component
            for vertex in section.data.polygons[polygon_index].vertices
        }
        if len(component) != 2 or len(component_vertices) != 4 or any(
                len(section.data.polygons[index].vertices) != 3 for index in component):
            raise RuntimeError(
                f"{section.name} badge {sorted(component)} is not an exact two-triangle quad")
        for polygon_index in component:
            polygon = section.data.polygons[polygon_index]
            a, b, c = (world_vertices[index] for index in polygon.vertices)
            for first in range(31):
                for second in range(31 - first):
                    u = first / 30.0
                    v = second / 30.0
                    point = a * (1.0 - u - v) + b * u + c * v
                    nearest, normal, _face, _distance = bvh.find_nearest(point)
                    if nearest is None or normal is None:
                        raise RuntimeError(
                            f"{section.name} badge projection missed its supporting skin")
                    clearance = (point - nearest).dot(normal)
                    minimum_clearance = min(minimum_clearance, clearance)
                    if clearance < 0.000498:
                        raise RuntimeError(
                            f"{section.name} badge {sorted(component)} clips or sits too close "
                            f"to its wing skin: {clearance * 1000.0:.6f} mm; required >=0.498 mm")
    print(f"WING_BADGE_CLEARANCE={section.name}:{minimum_clearance * 1000.0:.6f}mm")


def validate_existing_sections():
    left = bpy.data.objects.get(LEFT_NAME)
    right = bpy.data.objects.get(RIGHT_NAME)
    if left is None and right is None:
        return False
    if left is None or right is None:
        raise RuntimeError("Only part of the authored three-section exterior exists")
    sections = {
        name: bpy.data.objects.get(name) for name in (CENTRAL_NAME, LEFT_NAME, RIGHT_NAME)
    }
    if any(section is None for section in sections.values()):
        raise RuntimeError("The authored center section is missing")
    stale = [obj.name for obj in bpy.data.objects
             if obj.name.endswith("_CentralWork") or "_DamageInterior_" in obj.name or
             "_SeamCap" in obj.name or "_Skin_" in obj.name]
    if stale:
        raise RuntimeError(f"Synthetic damage geometry remains in the production model: {stale}")
    if len({section.data.as_pointer() for section in sections.values()}) != 3:
        raise RuntimeError("Authored damage sections must own three distinct meshes")
    expected_objects = set(sections.values())
    structure_owners = {
        obj for obj in bpy.data.objects
        if obj.type == "MESH" and obj.data is not None and any(
            material is not None and material.name == STRUCTURE_MATERIAL_NAME
            for material in obj.data.materials)
    }
    controlled_owners = {
        obj for obj in bpy.data.objects
        if any(key.startswith("f117_") for key in obj.keys())
    }
    if structure_owners != expected_objects or controlled_owners != expected_objects:
        unexpected = sorted(
            obj.name for obj in (structure_owners | controlled_owners) - expected_objects)
        raise RuntimeError(f"Unexpected authored damage geometry/metadata owners remain: {unexpected}")
    for section in sections.values():
        validate_section_contract(section)
    validate_structural_pairs(sections)
    validate_global_triangle_ownership(sections)
    restore_and_validate_shoulder_normals(sections[CENTRAL_NAME])
    return True


def author_damage_sections():
    if validate_existing_sections():
        print("DAMAGE_SECTIONS=already-authored")
        return
    if bpy.data.objects.get(LEFT_NAME) is not None or bpy.data.objects.get(RIGHT_NAME) is not None:
        raise RuntimeError("Only part of the authored three-section exterior exists")

    source = bpy.data.objects.get(CENTRAL_NAME)
    if source is None or source.type != "MESH":
        raise RuntimeError("The unsplit production exterior mesh is unavailable")
    if len(source.data.vertices) != 152114 or len(source.data.polygons) != 84299:
        raise RuntimeError(
            "The unsplit production exterior raw vertex/polygon contract changed")
    if any(len(polygon.vertices) != 3 for polygon in source.data.polygons):
        raise RuntimeError("The unsplit production exterior must remain exactly triangulated")
    used_vertices = {
        index for polygon in source.data.polygons for index in polygon.vertices
    }
    if len(used_vertices) != len(source.data.vertices):
        raise RuntimeError("The unsplit production exterior contains loose/trash vertices")
    probe = source.data.copy()
    try:
        repaired = probe.validate(verbose=False, clean_customdata=False)
        repaired_materials = probe.validate_material_indices()
    finally:
        bpy.data.meshes.remove(probe)
    if repaired or repaired_materials:
        raise RuntimeError("The unsplit production exterior contains invalid mesh data")

    components, vertex_roots = connected_components(source)
    central_polygons, left_polygons, right_polygons, left_main, right_main = \
        classify_measured_sections(source, components)
    left_full_ring = identify_root_loop(source, left_main, vertex_roots, LEFT_NAME)
    right_full_ring = identify_root_loop(source, right_main, vertex_roots, RIGHT_NAME)
    left_ring = left_full_ring[1:-1]
    right_ring = right_full_ring[1:-1]
    for owner, actual, expected in (
            (LEFT_NAME, left_ring, structural_root_points(LEFT_NAME)),
            (RIGHT_NAME, right_ring, structural_root_points(RIGHT_NAME))):
        if len(actual) != len(expected) or any(
                (first - second).length > RING_COORDINATE_TOLERANCE
                for first, second in zip(actual, expected)):
            raise RuntimeError(f"{owner} measured inner structural ring changed")

    material = structure_material()
    # In Blender coordinates +X exports as aircraft left; point each mating face
    # outward from its owning section.  FBX applies the matching normal transform.
    central_left = duplicate_section(source, CENTRAL_NAME + "_CentralWork", central_polygons,
                                     left_ring, Vector((1.0, 0.0, 0.0)), material,
                                     LEFT_NAME,
                                     normalize_central=True, split_specs=(),
                                     normalization_ring=left_full_ring)
    # Append the second root-structure face directly to the same mesh; the final model retains no
    # intermediate or hidden geometry object.
    mesh = central_left.data
    structure_index = next(index for index, item in enumerate(mesh.materials)
                           if item is material)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    maximum_move, split_groups = normalize_central_boundary(
        bm, central_left, right_full_ring, RIGHT_NAME, split_specs=RIGHT_CENTRAL_SPLITS)
    central_left["f117_root_maximum_snap_mm"] = max(
        float(central_left.get("f117_root_maximum_snap_mm", 0.0)), maximum_move * 1000.0)
    central_left["f117_root_edge_splits"] = int(
        central_left.get("f117_root_edge_splits", 0)) + split_groups
    weld_center_shoulders(bm, central_left, right_full_ring, RIGHT_NAME)
    local_ring = [central_left.matrix_world.inverted() @ point for point in right_ring]
    ring_vertices = [bm.verts.new(point) for point in local_ring]
    for triangle in triangulate_ring(right_ring, RIGHT_NAME):
        first, second, third = (ring_vertices[index] for index in triangle)
        normal = ((central_left.matrix_world @ second.co) - (central_left.matrix_world @ first.co)).cross(
            (central_left.matrix_world @ third.co) - (central_left.matrix_world @ first.co))
        if normal.dot(Vector((-1.0, 0.0, 0.0))) < 0.0:
            second, third = third, second
        face = bm.faces.new((first, second, third))
        face.material_index = structure_index
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    mesh.update(calc_edges=True)
    repaired = mesh.validate(verbose=True, clean_customdata=False)
    repaired_materials = mesh.validate_material_indices()
    if repaired or repaired_materials:
        raise RuntimeError("Central section contained invalid mesh data that Blender had to repair")
    central_left["f117_structural_triangles"] = 15

    left = duplicate_section(source, LEFT_NAME, left_polygons, left_ring,
                             Vector((-1.0, 0.0, 0.0)), material, LEFT_NAME)
    right = duplicate_section(source, RIGHT_NAME, right_polygons, right_ring,
                              Vector((1.0, 0.0, 0.0)), material, RIGHT_NAME)

    original_mesh = source.data
    bpy.data.objects.remove(source, do_unlink=True)
    if original_mesh.users == 0:
        bpy.data.meshes.remove(original_mesh)
    central_left.name = CENTRAL_NAME
    central_left.data.name = CENTRAL_NAME
    central_left["f117_damage_section"] = CENTRAL_NAME

    if not validate_existing_sections():
        raise RuntimeError("Authored damage-section validation did not complete")
    print(
        "DAMAGE_SECTIONS=authored,objects:3,interfaces:2,"
        "structuralTriangles:30,leftComponents:12,rightComponents:12"
    )


def main():
    loaded_file = Path(bpy.data.filepath).resolve()
    if os.path.normcase(os.fspath(loaded_file)) != os.path.normcase(os.fspath(MASTER_PATH.resolve())):
        raise RuntimeError(f"Refusing to modify unexpected Blender file: {bpy.data.filepath}")
    author_damage_sections()
    bpy.ops.wm.save_as_mainfile(filepath=os.fspath(MASTER_PATH))
    print(f"SAVED_BLEND={MASTER_PATH}")


if __name__ == "__main__":
    main()
