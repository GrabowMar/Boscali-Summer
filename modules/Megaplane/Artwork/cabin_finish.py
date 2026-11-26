"""Texture and UV layout for the Groza's original cockpit panel atlas."""
from pathlib import Path


NAME = "CabinFinish"
FILENAME = "BS_Megaplane_CabinFinish_Tex.png"
SIZE = 1024
GUTTER = 4
ASSETS = Path(__file__).resolve().parents[1] / "Assets"
TILES = {
    # left, top, width, height in top-down image coordinates
    "charcoal": (0, 0, 256, 256), "olive": (256, 0, 256, 256),
    "vent": (512, 0, 256, 256), "nonskid": (768, 0, 256, 256),
    "breakers": (0, 256, 256, 128), "power": (256, 256, 256, 128),
    "comms_strip": (512, 256, 256, 128), "thermal": (768, 256, 256, 128),
    "nav": (0, 384, 256, 128), "comms": (256, 384, 256, 128),
    "engineering": (512, 384, 256, 128), "cargo": (768, 384, 256, 128),
    "hatch": (0, 512, 256, 512), "fabric": (256, 512, 256, 512),
    "plot": (512, 512, 512, 256), "headliner": (512, 768, 512, 256),
}


def material():
    import bpy
    from finish import new_paint

    result = bpy.data.materials.get("BS_Megaplane_" + NAME)
    if result is not None:
        return result
    result = new_paint(NAME, (.74, .74, .72), .24)
    shader = result.node_tree.nodes["Principled BSDF"]
    texture = result.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(ASSETS / FILENAME), check_existing=True)
    texture.image.pack()
    result.node_tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
    return result


def assign_finish(obj, face_index, tile, corners):
    """Apply a panel finish to a quad. Vertex indices run BL, BR, TR, TL."""
    mesh = obj.data
    face = mesh.polygons[face_index]
    assert len(corners) == 4 and set(corners) == set(face.vertices), (obj.name, face_index, tile)
    paint = material()
    if paint.name not in mesh.materials:
        mesh.materials.append(paint)
    face.material_index = mesh.materials.find(paint.name)
    x, y, width, height = TILES[tile]
    left, right = (x + GUTTER) / SIZE, (x + width - GUTTER) / SIZE
    bottom, top = 1 - (y + height - GUTTER) / SIZE, 1 - (y + GUTTER) / SIZE
    coordinates = dict(zip(corners, ((left, bottom), (right, bottom), (right, top), (left, top))))
    uv = mesh.uv_layers.active or mesh.uv_layers.new()
    values = uv.uv
    for loop in face.loop_indices:
        values[loop].vector = coordinates[mesh.loops[loop].vertex_index]
