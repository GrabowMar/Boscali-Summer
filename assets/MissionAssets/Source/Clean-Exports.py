"""Repair existing exports without rerendering visually identical zero-area faces.
Future generation performs the same cleanup in SurfaceFinish.finish.
"""
import bpy,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from SurfaceFinish import clean
bpy.context.preferences.filepaths.save_version=0
def export(path,objects):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects: ob.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,axis_forward='-Z',axis_up='Y',
        apply_scale_options='FBX_SCALE_NONE',bake_space_transform=False,object_types={'MESH','EMPTY'},
        mesh_smooth_type='EDGE',use_tspace=False,use_custom_props=True,add_leaf_bones=False,bake_anim=False)
for folder in sys.argv[sys.argv.index('--')+1:]:
    pack=Path(folder).resolve(); catalog=json.loads((pack/'catalog.json').read_text(encoding='utf-8-sig'))
    for entry in catalog['assets']:
        name=entry['model']
        if name=='CargoShip': continue
        path=pack/'Models'/(name+'.blend'); bpy.ops.wm.open_mainfile(filepath=str(path))
        objects=[bpy.data.objects[n] for n in ('LOD0','LOD1','LOD2','COL_Static')]
        for ob in objects:
            clean(ob.data); ob.data.calc_loop_triangles(); entry['lod_triangles'][ob.name]=len(ob.data.loop_triangles)
        export(pack/'Models'/(name+'.fbx'),objects); bpy.ops.wm.save_as_mainfile(filepath=str(path))
        if entry.get('rig_model'):
            author=pack/'Models'/(name+'Authoring.blend'); bpy.ops.wm.open_mainfile(filepath=str(author))
            for ob in bpy.data.objects:
                if ob.type=='MESH': clean(ob.data)
            root=bpy.data.objects[name+'_Rig']; export(pack/'Models'/(entry['rig_model']+'.fbx'),[root]+list(root.children_recursive))
            bpy.ops.wm.save_as_mainfile(filepath=str(author))
    (pack/'catalog.json').write_text(json.dumps(catalog,indent=2))
    print('CLEAN_EXPORTS_OK',pack.name,flush=True)
