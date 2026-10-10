"""Offline bundle/source coverage and contact-sheet check; Blender Python + existing UnityPy/Pillow."""
import sys, os, json, hashlib
from pathlib import Path
sys.path.insert(0, os.environ.get('NOMOD_BLENDER_PYLIBS',str(Path(os.environ['LOCALAPPDATA'])/'nomodkit/blender-pylibs')))
import UnityPy
from PIL import Image, ImageDraw, ImageFont

pack=Path(sys.argv[1]).resolve()
catalog=json.loads((pack/'catalog.json').read_text())
assert len(catalog['assets'])==13
previews_only='--previews-only' in sys.argv
env=UnityPy.load(str(pack/'FutureOps.nobp')) if not previews_only else None
prefabs={k for k in env.container if k.endswith('.prefab')} if env else set()
for entry in catalog['assets']:
    key=entry['key'].lower()
    if not previews_only: assert any(p.endswith('/'+key+'.prefab') for p in prefabs),key
    for ext in ('fbx','blend'): assert (pack/'Models'/f"{entry['model']}.{ext}").stat().st_size>0
    tris=entry['lod_triangles']; assert tris['LOD0']>tris['LOD1']>tris['LOD2']>0
    assert tris['COL_Static']>0
    with Image.open(pack/'Previews'/f"{entry['model']}.png") as im: im.verify()
    if not previews_only:
        with Image.open(pack/'Previews/Unity'/f"{entry['model']}.png") as im: im.verify()
if not previews_only:
    manifests=[o.read().m_Script for o in env.objects if o.type.name=='TextAsset' and o.read().m_Name=='patch_manifest']
    assert len(manifests)==1
    manifest=json.loads(manifests[0].lstrip('\ufeff')); assert manifest['schemaVersion']==3
    assert not manifest['Ops'] and not manifest['Patches']
    bundled_catalog=json.loads(next(o.read().m_Script for o in env.objects if o.type.name=='TextAsset' and o.read().m_Name=='catalog').lstrip('\ufeff'))
    assert bundled_catalog==catalog,'Bundled catalog is stale'
    lit=[o.read_typetree() for o in env.objects if o.type.name=='Shader'
         and o.read_typetree().get('m_ParsedForm',{}).get('m_Name')=='Universal Render Pipeline/Lit']
    assert len(lit)==1 and len(lit[0]['compressedBlob'])>1024
    assert any(s['m_Passes'] for s in lit[0]['m_ParsedForm']['m_SubShaders']), 'URP render passes stripped'
sheet=Image.new('RGB',(1600,1430),(24,31,35)); draw=ImageDraw.Draw(sheet)
font_path='C:/Windows/Fonts/seguisb.ttf'
font=ImageFont.truetype(font_path,23); small=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
draw.text((25,15),'FUTURE OPS / 13 ASSETS',font=font,fill=(219,226,218))
draw.text((25,49),'Blender sources · metre scale · 3 LODs · static colliders · Blueprinter .nobp',font=small,fill=(145,164,166))
for i,e in enumerate(catalog['assets']):
    x=(i%4)*400; y=90+(i//4)*330
    with Image.open(pack/'Previews'/f"{e['model']}.png") as im:
        im=im.convert('RGB'); im.thumbnail((390,292)); sheet.paste(im,(x+5,y))
    draw.text((x+12,y+294),e['model'],font=font,fill=(218,224,214))
sheet.save(pack/'Previews/Overview.jpg',quality=94)
hashes={str(p.relative_to(pack)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in
        sorted(pack.rglob('*')) if p.is_file() and p.suffix in ('.nobp','.blend','.fbx','.png','.jpg')}
(pack/'checksums.json').write_text(json.dumps(hashes,indent=2))
print('PASS: source and preview coverage; overview written.' if previews_only else
      'PASS: 13 source/preview/prefab sets, descending LOD budgets, colliders, schema-3 manifest with no registration ops; overview and SHA256 checksums written.')
