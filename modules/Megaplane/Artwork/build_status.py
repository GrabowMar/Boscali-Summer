"""Draw the native status-display sprites from the shipped BSMP, using Python and Pillow.

Each tile has the same nose-up orthographic frame. The manifest is row-major starting
at the top left. Tile zero is the permanent outline; each other tile names an AeroPart.
"""

import gzip
from pathlib import Path
import struct

from PIL import Image, ImageChops, ImageDraw, ImageFilter


TILE, COLUMNS, AA = 256, 8, 4
# The order resolves overlapping structures as on an instrument diagram: root then
# its detachable panels and pods. The last four pods are mounted on the outer wings.
MIDLINE = [
    ("BS_Megaplane", ("Fuselage_Center",)), ("fuselage_R", ("Fuselage_Aft",)),
    ("tail", ("Fuselage_TailCone",)), ("body_F", ("Fuselage_Forward",)),
    ("fuselage_F", ("Fuselage_Cockpit",)), ("hump", ("Fuselage_Hump",)),
]
SIDES = [
    ("engine", "Wing_Root"), ("gearbay", "Wing_Inner"), ("wing1b", "Wing_Mid"),
    ("wing2", "Wing_Outer"), ("wingtip", "Wing_Tip"), ("aileron", "Aileron"),
    ("wing1a", "Flap_Outer"), ("intake", "Strongback"), ("nozzle", "Boom"),
    ("vstab1", "Fin"), ("rudder", "Rudder"),
]


def ownership():
    groups = {}
    for native, mesh in SIDES:
        for side in ("L", "R"):
            groups[native+"_"+side] = (mesh+"_"+side,)
    for side in ("L","R"):
        groups["gearbay_"+side] += ("Flap_Inner_"+side,)
        for bank in ("A", "F"):
            groups["gearbay_"+side] += ("LiftBank_"+bank+"_"+side,)
            groups["gearbay_"+side] += tuple(f"LiftDoor_{bank}_{side}_{i}" for i in range(4))
        groups["wing2_"+side] += ("Pod_"+side+"4","Pods_Nacelle4_"+side)
    # Draw the pressure keel over the wing gloves, which overlap its flanks in plan.
    groups.update(MIDLINE)
    groups["hstab"] = ("Tailplane", "Elevator", "TailCap_L", "TailCap_R")
    # The horizontal bridge crosses above the tailcone in plan view. Expose the
    # tailcone's centrally aligned footprint as its own selectable damage section.
    groups["tail"] = groups.pop("tail")
    for n, native in ((1,"pod1"),(2,"nacelle"),(3,"flap")):
        for side in ("L","R"):
            groups[native+"_"+side] = ("Pod_"+side+str(n),"Pods_Nacelle"+str(n)+"_"+side)
    # These slender vertical parts overlap broader structure directly beneath them.
    for native in ("intake", "vstab1", "rudder"):
        for side in ("L","R"):
            name=native+"_"+side
            groups[name]=groups.pop(name)
    return groups


def read_shapes(path, wanted, optional):
    with gzip.open(path, "rb") as stream:
        def read(fmt):
            return struct.unpack("<"+fmt, stream.read(struct.calcsize("<"+fmt)))
        def count():
            return read("i")[0]
        def label():
            length=shift=0
            while True:
                byte=stream.read(1)[0]
                length |= (byte & 127) << shift
                if byte < 128:
                    break
                shift += 7
            return stream.read(length).decode()
        if read("2i") != (0x504d5342,2):
            raise ValueError("Expected BSMP v2")
        for _ in range(count()):
            label()
            read("6f?")
        origins=[]
        shapes={}
        for _ in range(count()):
            name, parent, local = label(), count(), read("3f")
            origin = tuple(a+b for a,b in zip(origins[parent],local)) if parent>=0 else local
            origins.append(origin)
            n=count()
            if not n:
                continue
            keep=name in wanted or name in optional
            raw=stream.read(n*32)
            if keep:
                points=[(v[0]+origin[0],v[2]+origin[2]) for v in struct.iter_unpack("<8f",raw)]
                triangles=[]
            for _ in range(count()):
                _,size=read("ii")
                data=stream.read(size*4)
                if keep:
                    triangles.extend(struct.iter_unpack("<iii",data))
            if keep:
                shapes[name]=(points,triangles)
        if wanted - shapes.keys():
            raise ValueError("Missing geometry: " + ", ".join(sorted(wanted-shapes.keys())))
        return shapes


def white(alpha):
    rgba=Image.new("RGBA",alpha.size,(255,255,255,0))
    rgba.putalpha(alpha)
    return rgba


def build_status(mesh_path, atlas_path, manifest_path):
    groups=ownership()
    optional={"Dorsal_ServiceTrunks":"hump"}
    for side in ("L", "R"):
        for tag in ("Up_", "Low_"):
            optional["Fairing_WingRoot"+tag+side]="engine_"+side
        optional["Strake_Fuselage_Forward_"+side]="body_F"
        optional["Strake_Fuselage_Cockpit_"+side]="fuselage_F"
    shapes=read_shapes(mesh_path,{name for names in groups.values() for name in names},optional)
    for mesh, native in optional.items():
        if mesh in shapes:
            groups[native] += (mesh,)
    width=TILE*AA
    points=[p for vertices,_ in shapes.values() for p in vertices]
    x0,x1=min(p[0] for p in points),max(p[0] for p in points)
    z0,z1=min(p[1] for p in points),max(p[1] for p in points)
    scale=(width-12*AA)/max(x1-x0,z1-z0)
    def project(p):
        return (round(width/2+(p[0]-(x0+x1)/2)*scale),
                round(width/2-(p[1]-(z0+z1)/2)*scale))
    labels=Image.new("L",(width,width))
    for i,(native,names) in enumerate(groups.items(),1):
        part_mask=Image.new("L",labels.size)
        draw=ImageDraw.Draw(part_mask)
        for name in names:
            vertices,triangles=shapes[name]
            # Expose the core flanks around the overlapping dorsal plate. The two
            # full-width footprints otherwise completely cover the central owner.
            if name=="Fuselage_Hump":
                projected=[project((x*.73,z)) for x,z in vertices]
            else:
                projected=list(map(project,vertices))
            for a,b,c in triangles:
                draw.polygon([projected[a],projected[b],projected[c]], fill=255)
        if native.startswith(("intake_", "vstab1_", "rudder_")):
            # Minimum instrument line weight at a 256-pixel native display resolution.
            part_mask=part_mask.filter(ImageFilter.MaxFilter(5))
        labels.paste(i,mask=part_mask)
    body=labels.point(lambda v:255 if v else 0)
    border=ImageChops.subtract(body.filter(ImageFilter.MaxFilter(7)),body.filter(ImageFilter.MinFilter(7)))
    slots=[]
    manifest=["background"]
    for i,name in enumerate(groups,1):
        mask=labels.point(lambda value:255 if value==i else 0)
        if not mask.getbbox():
            raise ValueError(f"{name} is fully obscured on the status diagram")
        # Leave a narrow line around each original structural boundary.
        fill=mask.filter(ImageFilter.MinFilter(3))
        seam=ImageChops.subtract(mask,fill).point(lambda v:round(v*.83))
        border=ImageChops.lighter(border,seam)
        sprite=fill.resize((TILE,TILE),Image.Resampling.LANCZOS)
        if sprite.getextrema()[1] < 128:
            raise ValueError(f"{name} does not have a readable native-resolution mask")
        slots.append(white(sprite))
        manifest.append(name)
    background=ImageChops.lighter(border,body.point(lambda v:round(v*.085)))
    slots.insert(0,white(background.resize((TILE,TILE),Image.Resampling.LANCZOS)))
    atlas=Image.new("RGBA",(COLUMNS*TILE, ((len(slots)+COLUMNS-1)//COLUMNS)*TILE))
    for i, sprite in enumerate(slots):
        atlas.paste(sprite, ((i%COLUMNS)*TILE,(i//COLUMNS)*TILE))
    atlas.save(atlas_path,optimize=True)
    manifest_path.write_text("\n".join(manifest)+"\n",encoding="ascii")
    print(f"{len(groups)} native damage sprites; {atlas.width}x{atlas.height} status atlas")


if __name__=="__main__":
    assets=Path(__file__).resolve().parent.parent/"Assets"
    build_status(assets/"megaplane.mesh.gz",assets/"BS_Megaplane_Status.png",assets/"megaplane-status.txt")
