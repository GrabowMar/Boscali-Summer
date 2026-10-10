"""Blender-side validation of authored UVs, packed surfaces and export geometry."""
import bpy,json,sys,math
from pathlib import Path
packs=[Path(p).resolve() for p in sys.argv[sys.argv.index('--')+1:]]
for pack in packs:
    checked=[]
    for e in json.loads((pack/'catalog.json').read_text(encoding='utf-8-sig'))['assets']:
        if e['model']=='CargoShip': continue  # Separate native marine validation pipeline.
        bpy.ops.wm.open_mainfile(filepath=str(pack/'Models'/(e['model']+'.blend')))
        ob=bpy.data.objects['LOD0']; mesh=ob.data
        assert ob.matrix_world.determinant()>0,e['model']
        assert mesh.uv_layers.active is not None,e['model']
        assert all(len(i.data.uv_layers)>0 for i in (bpy.data.objects['LOD1'],bpy.data.objects['LOD2']))
        assert all(math.isfinite(c) for v in mesh.vertices for c in v.co)
        assert all(p.area>1e-12 for p in mesh.polygons),(e['model'],'degenerate face')
        textured=0
        for m in mesh.materials:
            images=[n.image for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
            assert len(images)==3 and all(i.packed_file for i in images),(e['model'],m.name,'unpacked/missing PBR maps')
            textured+=1
        lod_bounds=[]
        for level in range(3):
            m=bpy.data.objects['LOD'+str(level)].data
            lod_bounds.append([max(v.co[i] for v in m.vertices)-min(v.co[i] for v in m.vertices) for i in range(3)])
        if 'Satellite' in e['model'] or e['model']=='OrbitalServiceTug':
            # Solar-wing span must survive the far LOD; small fittings may disappear.
            assert all(bounds[0]>=lod_bounds[0][0]*.97 for bounds in lod_bounds),e['model']
        checked.append({'model':e['model'],'textured_materials':textured,'uv_vertices':len(mesh.uv_layers.active.data),'lod_bounds':lod_bounds})
    (pack/'geometry-validation.json').write_text(json.dumps(checked,indent=2))
    print('GEOMETRY_OK',pack.name,len(checked),'models',flush=True)
