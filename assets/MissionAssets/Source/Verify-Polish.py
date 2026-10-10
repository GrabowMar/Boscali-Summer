"""Compare satellite geometry/renderer budgets with the previous authored delivery."""
import bpy,json,sys
from pathlib import Path
args=sys.argv[sys.argv.index('--')+1:]; pack=Path(args[0]).resolve(); evidence=Path(args[1]).resolve()
before={e['model']:e for e in json.loads((evidence/'mission-before.json').read_text(encoding='utf-8-sig'))['assets']}
current={e['model']:e for e in json.loads((pack/'catalog.json').read_text(encoding='utf-8-sig'))['assets']}
report=[]
for name in ('OpticalSatellite','SarSatellite','RelaySatellite','OrbitalServiceTug'):
    def stats(file):
        bpy.ops.wm.open_mainfile(filepath=str(file)); bpy.context.view_layer.update()
        root=bpy.data.objects[name+'_Rig']; meshes=[o for o in root.children_recursive if o.type=='MESH']
        return len(meshes),sum(len(o.data.materials) for o in meshes)
    old=stats(evidence/'rigs-before'/(name+'Authoring.blend'))
    new=stats(pack/'Models'/(name+'Authoring.blend'))
    assert new[0]<=5 and new[0]<old[0]*.10,(name,old,new)
    old_tris=before[name]['lod_triangles']['LOD0']; new_tris=current[name]['lod_triangles']['LOD0']
    assert new_tris<old_tris*.65,(name,old_tris,new_tris)
    bpy.ops.wm.open_mainfile(filepath=str(pack/'Models'/(name+'.blend')))
    coverage=[]
    for level in range(3):
        mesh=bpy.data.objects['LOD'+str(level)].data
        coverage.append(sum(p.area for p in mesh.polygons if mesh.materials[p.material_index].name=='SolarBlue'))
    assert min(coverage)>=coverage[0]*.80,(name,'solar surface lost in LOD',coverage)
    report.append({'model':name,'lod0_triangles_before':old_tris,'lod0_triangles_after':new_tris,
        'rig_renderers_before':old[0],'rig_renderers_after':new[0],
        'rig_material_passes_before':old[1],'rig_material_passes_after':new[1],
        'solar_surface_area_per_lod':coverage,'lod_triangles':current[name]['lod_triangles']})
(pack/'polish-validation.json').write_text(json.dumps(report,indent=2))
print('POLISH_OK',json.dumps(report),flush=True)
