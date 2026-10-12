"""Bake top-down map silhouettes for the game's ground vehicles.

The game ships a plan-view silhouette per aircraft and ship but one capsule for every ground
vehicle. This reads each GroundVehicle prefab out of the installed game, flattens its render
meshes onto the ground plane and writes them as grey-on-clear cells of one sprite sheet, in
the vanilla icons' style: nose up, no outline, shaded by height like the game's vehicle icon.

    python tools/Build-VehicleMapIcons.py --write [--game <Nuclear Option dir>] [--preview <png>]

Needs UnityPy and pillow (the nomodkit venv has them). With --write it replaces
modules/Command/Assets/vehicle-map-icons.png and
modules/Command/Presentation/MapUi/MapVehicleIcons.Generated.cs; without it only the preview
is made. Re-run after a game update that adds vehicles; a vehicle missing from the sheet falls
back to its class stand-in.
"""
import argparse, io, math, os
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
from PIL import Image, ImageChops, ImageDraw, ImageFilter

CELL = 64          # sheet cell, pixels
MARGIN = 3         # clear border inside a cell
SUPER = 8          # supersampling while filling triangles
COLUMNS = 8
LOW_TONE = 0.42    # brightness of the lowest parts; the roof is 1
TONE_CURVE = 1.6   # above 1 keeps the hull dark and lifts only what stands on it
TINTS = ((51, 153, 255, 255), (255, 71, 61, 255))   # preview only: the map's friendly and hostile tints
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def local_matrix(transform):
    """Row-major 3x4: rotation times scale, then translation."""
    q, s, p = transform.m_LocalRotation, transform.m_LocalScale, transform.m_LocalPosition
    x, y, z, w = q.x, q.y, q.z, q.w
    r = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
         [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
         [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
    scale, move = (s.x, s.y, s.z), (p.x, p.y, p.z)
    return [[r[i][0] * scale[0], r[i][1] * scale[1], r[i][2] * scale[2], move[i]] for i in range(3)]


def combine(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) + (a[i][3] if j == 3 else 0.0) for j in range(4)]
            for i in range(3)]


IDENTITY = [[1.0, 0.0, 0.0, 0.0], [0.0, 1.0, 0.0, 0.0], [0.0, 0.0, 1.0, 0.0]]


def components(game_object):
    for pointer in game_object.m_Component:
        yield pointer.component if hasattr(pointer, 'component') else pointer


def collect(transform, parent, triangles):
    """Appends every active render mesh under a transform as ground-plane triangles."""
    game_object = transform.m_GameObject.read()
    if not game_object.m_IsActive:
        return
    world = combine(parent, local_matrix(transform))
    mesh, drawn = None, False
    for component in components(game_object):
        kind = component.type.name
        if kind == 'MeshFilter':
            mesh = component.read().m_Mesh
        elif kind == 'MeshRenderer':
            drawn = bool(component.read().m_Enabled)
    if mesh and drawn:
        try:
            handler = MeshHandler(mesh.read())
            handler.process()
            # World x (right), z (nose) and height, in that order.
            points = [tuple(world[r][0] * v[0] + world[r][1] * v[1] + world[r][2] * v[2] + world[r][3]
                            for r in (0, 2, 1)) for v in handler.m_Vertices]
            for submesh in handler.get_triangles():
                for a, b, c in submesh:
                    triangles.append((points[a], points[b], points[c]))
        except Exception as error:
            print('   mesh skipped:', error)
    for child in transform.m_Children:
        collect(child.read(), world, triangles)


def silhouette(triangles):
    """Draws the model from above, nose up, into one antialiased cell; None for an empty model.

    A bare outline of a vehicle is a rounded box whatever it carries, so the cell is shaded by
    height the way the game's own vehicle icon is: running gear dark, hull mid, and whatever
    stands on the roof - turret, gun, launch tubes, dish - brightest.
    """
    if not triangles:
        return None
    xs = [p[0] for triangle in triangles for p in triangle]
    zs = [p[1] for triangle in triangles for p in triangle]
    ys = sorted(p[2] for triangle in triangles for p in triangle)
    span = max(max(xs) - min(xs), max(zs) - min(zs))
    # Ignore the odd aerial when ranging the heights.
    floor, roof = ys[len(ys) // 50], ys[-1 - len(ys) // 50]
    if span <= 0 or roof <= floor:
        return None
    size = CELL * SUPER
    scale = (CELL - 2 * MARGIN) * SUPER / span
    centre = ((min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2)
    mask = Image.new('L', (size, size), 0)
    shade = Image.new('L', (size, size), 0)
    mask_pen, shade_pen = ImageDraw.Draw(mask), ImageDraw.Draw(shade)
    # Painter's order: low parts first, so the roof ends up on top.
    for triangle in sorted(triangles, key=lambda t: max(p[2] for p in t)):
        outline = [(size / 2 + (p[0] - centre[0]) * scale, size / 2 - (p[1] - centre[1]) * scale)
                   for p in triangle]
        height = min(1.0, max(0.0, (max(p[2] for p in triangle) - floor) / (roof - floor)))
        mask_pen.polygon(outline, fill=255)
        shade_pen.polygon(outline, fill=int(255 * (LOW_TONE + (1 - LOW_TONE) * height ** TONE_CURVE)))
    # Close hairline gaps between parts, then shed aerials and other detail under a pixel.
    mask = mask.filter(ImageFilter.MaxFilter(SUPER + 1)).filter(ImageFilter.MinFilter(SUPER + 1))
    shade = shade.filter(ImageFilter.MaxFilter(SUPER + 1))
    cell = Image.merge('RGBA', (shade, shade, shade, mask))
    return cell.resize((CELL, CELL), Image.LANCZOS)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game', default=r'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option')
    parser.add_argument('--preview')
    parser.add_argument('--write', action='store_true')
    args = parser.parse_args()
    env = UnityPy.load(os.path.join(args.game, 'NuclearOption_Data', 'resources.assets'))

    roots = {}
    for obj in env.objects:
        if obj.type.name != 'MonoBehaviour':
            continue
        try:
            behaviour = obj.read(check_read=False)
            if behaviour.m_Script.read().m_ClassName != 'GroundVehicle':
                continue
            game_object = behaviour.m_GameObject.read()
        except Exception:
            continue
        roots[game_object.m_Name] = game_object

    cells = {}
    for name in sorted(roots, key=str.lower):
        transform = next(c.read() for c in components(roots[name]) if c.type.name == 'Transform')
        triangles = []
        collect(transform, IDENTITY, triangles)
        cell = silhouette(triangles)
        print(f'{name}: {len(triangles)} triangles' + ('' if cell else '  (empty, skipped)'))
        if cell:
            cells[name] = cell

    names = list(cells)
    rows = math.ceil(len(names) / COLUMNS)
    if args.write:
        sheet = Image.new('RGBA', (COLUMNS * CELL, rows * CELL), (255, 255, 255, 0))
        for index, name in enumerate(names):
            sheet.paste(cells[name], ((index % COLUMNS) * CELL, (index // COLUMNS) * CELL))
        os.makedirs(os.path.join(REPO, 'modules', 'Command', 'Assets'), exist_ok=True)
        sheet.save(os.path.join(REPO, 'modules', 'Command', 'Assets', 'vehicle-map-icons.png'), optimize=True)
        table = ',\n'.join('            "' + name + '"' for name in names)
        source = f'''// Generated by tools/Build-VehicleMapIcons.py from the installed game's vehicle models. Do not edit.
namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{{
    internal static partial class MapVehicleIcons
    {{
        private const int Cell = {CELL};
        private const int Columns = {COLUMNS};

        /// <summary>Vehicle prefab names in sheet order, left to right, top row first.</summary>
        private static readonly string[] Names =
        {{
{table}
        }};
    }}
}}
'''
        target = os.path.join(REPO, 'modules', 'Command', 'Presentation', 'MapUi', 'MapVehicleIcons.Generated.cs')
        with io.open(target, 'w', encoding='utf-8', newline='\r\n') as handle:
            handle.write(source)

    if args.preview:
        tile = 150
        preview = Image.new('RGBA', (COLUMNS * tile, rows * (tile + 20)), (30, 42, 50, 255))
        pen = ImageDraw.Draw(preview)
        for index, name in enumerate(names):
            x, y = (index % COLUMNS) * tile, (index // COLUMNS) * (tile + 20)
            tinted = Image.composite(Image.new('RGBA', (CELL, CELL), TINTS[index % 2]), Image.new('RGBA', (CELL, CELL)), cells[name].getchannel('A'))
            tinted = Image.merge('RGBA', [Image.eval(ImageChops.multiply(tinted.getchannel(c), cells[name].getchannel('R')), lambda v: v) for c in 'RGB'] + [cells[name].getchannel('A')])
            preview.alpha_composite(tinted.resize((128, 128), Image.LANCZOS), (x + 2, y))
            preview.alpha_composite(tinted.resize((21, 21), Image.LANCZOS), (x + 126, y + 108))
            pen.text((x + 4, y + 130), name, fill=(200, 215, 220, 255))
        preview.save(args.preview)
    print(len(names), 'vehicles baked')


if __name__ == '__main__':
    main()
