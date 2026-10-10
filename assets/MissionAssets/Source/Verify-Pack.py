"""Check all shipped model sets, articulated rigs, anchor references and disabled mission stubs."""
import os,sys,json,hashlib
from pathlib import Path
sys.path.insert(0,str(Path(os.environ['LOCALAPPDATA'])/'nomodkit/blender-pylibs'))
import UnityPy
from PIL import Image,ImageDraw,ImageFont
pack=Path(sys.argv[1]).resolve(); catalog=json.loads((pack/'catalog.json').read_text(encoding='utf-8-sig'))
assert len(catalog['assets'])==16
preview_only='--previews-only' in sys.argv
env=UnityPy.load(str(pack/'MissionAssets.nobp')) if not preview_only else None
paths=set(env.container) if env else set()
entries={e['key']:e for e in catalog['assets']}
for entry in entries.values():
    name=entry['model']; counts=entry['lod_triangles']
    assert counts['LOD0']>counts['LOD1']>counts['LOD2']>0 and counts['COL_Static']>0
    for ext in ('blend','fbx'): assert (pack/'Models'/f'{name}.{ext}').stat().st_size>0
    with Image.open(pack/'Previews'/f'{name}.png') as im: im.verify()
    assert len({a['name'] for a in entry['anchors']})==len(entry['anchors'])
    if not preview_only:
        assert any(p.endswith('/'+entry['key'].lower()+'.prefab') for p in paths)
        with Image.open(pack/'Previews/Unity'/f'{name}.png') as im: im.verify()
    if entry.get('rig_model'):
        assert (pack/'Models'/(entry['rig_model']+'.fbx')).stat().st_size>0
        assert (pack/'Models'/(name+'Authoring.blend')).stat().st_size>0
        if not preview_only: assert any(p.endswith('/'+entry['key'].lower()+'_rig.prefab') for p in paths)
index=json.loads((pack/'MissionStubs/index.json').read_text()); assert len(index['missions'])==9
allowed={'ElectronicWarfare','Recon','Rescue','RepairCover','BattlefieldSurvey','SupplyEscort','Defend','SortieReport'}
for key in index['missions']:
    stub=json.loads((pack/'MissionStubs'/f'{key}.json').read_text())
    assert stub['enabled'] is False and stub['automatic_registration'] is False and stub['status']=='design_stub'
    assert stub['kind'] in allowed and stub['authority']=='host'
    assert stub['limits']['board_cards']==3 and stub['limits']['accepted_per_faction']==2
    for asset in stub['assets']:
        entry=entries[asset['key']]; anchors={'GroundOrigin'}|{a['name'] for a in entry['anchors']}
        assert asset['anchor'] in anchors,(key,asset)
if not preview_only:
    manifest=json.loads(next(o.read().m_Script for o in env.objects if o.type.name=='TextAsset' and o.read().m_Name=='patch_manifest').lstrip('\ufeff'))
    assert manifest['schemaVersion']==3 and not manifest['Ops'] and not manifest['Patches']
    bundled_catalog=json.loads(next(o.read().m_Script for o in env.objects if o.type.name=='TextAsset' and o.read().m_Name=='catalog').lstrip('\ufeff'))
    assert bundled_catalog==catalog, 'Bundled catalog is stale'
sheet=Image.new('RGB',(1600,1420),(22,29,34)); draw=ImageDraw.Draw(sheet)
font=ImageFont.truetype('C:/Windows/Fonts/seguisb.ttf',22); small=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',16)
draw.text((24,15),'MISSION ASSETS / 12 GROUND + 4 ORBITAL',font=font,fill=(221,228,218))
draw.text((24,48),'Original Blender models | 3 LODs | anchor points | 4 satellite rigs | 9 mission design stubs',font=small,fill=(151,169,172))
for i,e in enumerate(catalog['assets']):
    x=i%4*400; y=85+i//4*330
    with Image.open(pack/'Previews'/f"{e['model']}.png") as im: im=im.convert('RGB'); im.thumbnail((390,292)); sheet.paste(im,(x+5,y))
    draw.text((x+12,y+296),e['model'],font=font,fill=(220,224,214))
sheet.save(pack/'Previews/Overview.jpg',quality=94)
hashes={str(p.relative_to(pack)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(pack.rglob('*')) if p.is_file() and p.suffix in ('.blend','.fbx','.nobp','.png','.jpg','.json') and p.name!='checksums.json'}
(pack/'checksums.json').write_text(json.dumps(hashes,indent=2))
print('PASS: 16 original model/LOD/preview sets, four satellite rig sources, nine disabled mission stubs with valid anchors'+('; 20 bundled prefabs and schema-3 asset-only manifest' if not preview_only else '')+'.')
