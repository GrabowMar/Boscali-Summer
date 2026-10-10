"""Verify the shipped native definition, registration, physics graph and game-asset patches."""
import os,sys,json,math,hashlib
from pathlib import Path
sys.path.insert(0,str(Path(os.environ['LOCALAPPDATA'])/'nomodkit/blender-pylibs'))
import UnityPy
pack=Path(sys.argv[1]).resolve(); env=UnityPy.load(str(pack/'CargoShip.nobp'))
types={}; rows=[]
for obj in env.objects:
    if obj.type.name=='MonoBehaviour':
        data=obj.read_typetree()
        try: cls=obj.read().m_Script.read().m_ClassName
        except Exception: continue
        types[cls]=types.get(cls,0)+1; rows.append((cls,data))
assert types.get('Ship')==1 and types.get('ShipDefinition')==1
assert types.get('ShipPart')==18 and types.get('ShipPropulsion')==1 and types.get('ShipAI')==1
assert types.get('ShipNetworkTransform')==1 and types.get('NetworkIdentity')==1
assert not any(types.get(k,0) for k in ('Turret','MissileLauncher','Gun','ResupplyShip','AryxKamikazeShipAI'))
definition=next(d for cls,d in rows if cls=='ShipDefinition')
assert definition['jsonKey']=='BS_ContainerFeeder' and definition['unitPrefab']['m_PathID']!=0
parts=[d for cls,d in rows if cls=='ShipPart']
assert sum(p['mass'] for p in parts)==8000000
hull=[p for p in parts if p['displacement']>0]
assert len(hull)==8 and sum(p['displacement'] for p in hull)==16000
assert all(p['forceTransform']['m_PathID'] and p['parentUnit']['m_PathID'] for p in parts)
assert all(len(p['damageMaterial']['renderers'])>0 for p in parts)
propulsion=next(d for cls,d in rows if cls=='ShipPropulsion')
assert len(propulsion['rotators'])==2 and all(p['transform']['m_PathID'] for p in propulsion['rotators'])
manifest=json.loads(next(o.read().m_Script for o in env.objects if o.type.name=='TextAsset' and o.read().m_Name=='patch_manifest').lstrip('\ufeff'))
assert any(op['opId']=='OpAddToEncyclopedia' and 'BS_ContainerFeeder' in op['payloadJson'] for op in manifest['Ops'])
assert any('Shader' in patch['GameAsset']['asset']['type'] for patch in manifest['Patches']), 'Native damage shader must be rebound by Blueprinter'
assert not any('Aryx' in str(d) for cls,d in rows)
result={'status':'PASS','native_components':types,'mass_kg':8000000,'hull_compartments':8,
        'registration':'OpAddToEncyclopedia','native_shader_patches':sum('Shader' in p['GameAsset']['asset']['type'] for p in manifest['Patches']),
        'bundle_sha256':hashlib.sha256((pack/'CargoShip.nobp').read_bytes()).hexdigest(),
        'live_acceptance':'not tested; no game deployment'}
(pack/'cargo-bundle-validation.json').write_text(json.dumps(result,indent=2))
print('PASS: registered native cargo ship, 18 parts / 8 hull compartments, mass, force/damage references, propeller rotators, native shader patch and no third-party/custom ship behaviour.')
