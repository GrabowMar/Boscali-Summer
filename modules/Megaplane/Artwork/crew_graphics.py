"""UV contract for the Groza's four crew workstations. Roles run left to right in both atlases."""
from pathlib import Path

import bpy

from finish import new_paint

ROLES = ("Nav", "Comms", "Engineering", "Cargo")
ASSETS = Path(__file__).resolve().parents[1] / "Assets"


def _material(name, luminous):
    full = "BS_Megaplane_" + name
    mat = bpy.data.materials.get(full)
    if mat is not None:
        return mat
    mat = new_paint(name, (.72, .72, .72), .22 if luminous else .28)
    shader = mat.node_tree.nodes.get("Principled BSDF")
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(str(ASSETS / (full + "_Tex.png")), check_existing=True)
    tex.image.pack()
    mat.node_tree.links.new(tex.outputs["Color"], shader.inputs["Base Color"])
    if luminous:
        mat.node_tree.links.new(tex.outputs["Color"], shader.inputs["Emission Color"])
        shader.inputs["Emission Strength"].default_value = .64
    return mat


def _apply(obj, face_index, mat, column, row, rows, corners):
    mesh = obj.data
    face = mesh.polygons[face_index]
    if mat.name not in mesh.materials:
        mesh.materials.append(mat)
    face.material_index = mesh.materials.find(mat.name)
    uv = mesh.uv_layers.active or mesh.uv_layers.new()
    # corners carries the vertex indices in reading order, even when the exported winding is reversed.
    corners = list(face.vertices) if corners is None else list(corners)
    assert len(corners) == 4 and set(corners) == set(face.vertices)
    inset_x, inset_y = .5 / 2048, .5 / (256 * rows)
    u0, u1 = column / 4 + inset_x, (column + 1) / 4 - inset_x
    v0, v1 = 1 - (row + 1) / rows + inset_y, 1 - row / rows - inset_y
    bounds = dict(zip(corners, ((u0, v0), (u1, v0), (u1, v1), (u0, v1))))
    for loop in face.loop_indices:
        uv.uv[loop].vector = bounds[mesh.loops[loop].vertex_index]


def assign_screen(obj, role, face_index=0, corners=None):
    """Assign top-row artwork. The runtime moves this UV window vertically when an indication changes."""
    _apply(obj, face_index, _material("CrewCRT_Emit", True), ROLES.index(role), 0, 8, corners)


def assign_print(obj, tile, face_index=0, corners=None):
    """Tiles 0–3 are role plaques, 4–7 are working cards; BL, BR, TR, TL corner indices are optional."""
    assert 0 <= tile < 8
    _apply(obj, face_index, _material("CrewPrint", False), tile % 4, tile // 4, 2, corners)
