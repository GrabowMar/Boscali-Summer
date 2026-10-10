"""Extract local vanilla reference meshes (helper) and this material/texture report.
Reference outputs belong in ignored evidence, never in the distributable pack.
"""
import os,sys,json
from pathlib import Path
sys.path.insert(0,str(Path(os.environ['LOCALAPPDATA'])/'nomodkit/blender-pylibs'))
import UnityPy
source=Path(sys.argv[1]); out=Path(sys.argv[2]); out.mkdir(parents=True,exist_ok=True)
env=UnityPy.load(str(source)); transforms={}; names={}; objects={o.path_id:o for o in env.objects}
for o in env.objects:
    if o.type.name=='GameObject':
        go=o.read(); names[o.path_id]=go.m_Name
    elif o.type.name=='Transform':
        t=o.read(); transforms[o.path_id]=(t.m_GameObject.path_id,t.m_Father.path_id)
go_tf={g:t for t,(g,parent) in transforms.items()}
def owner(g):
    t=go_tf.get(g); seen=set()
    while t in transforms and t not in seen:
        seen.add(t); g,parent=transforms[t]
        if names.get(g) in ('Truck2-R','RadarContainer1','Frigate1'): return names[g]
        t=parent
    return None
report=[]; saved=set()
for o in env.objects:
    if o.type.name!='MeshRenderer': continue
    renderer=o.read(); root=owner(renderer.m_GameObject.path_id)
    if not root: continue
    for ptr in renderer.m_Materials:
        mat=ptr.read(); record={'unit':root,'renderer':names.get(renderer.m_GameObject.path_id),'material':mat.m_Name,'textures':[]}
        for prop,texenv in mat.m_SavedProperties.m_TexEnvs:
            texref=texenv.m_Texture
            if not texref.path_id: continue
            try: tex=texref.read()
            except Exception: continue
            if not hasattr(tex,'image'): continue
            file=''.join(c if c.isalnum() or c in '._-' else '_' for c in tex.m_Name)+'.png'
            if file not in saved: tex.image.save(out/file); saved.add(file)
            record['textures'].append({'property':prop,'name':tex.m_Name,'width':tex.m_Width,'height':tex.m_Height,'reference_file':file})
        report.append(record)
(out/'material-report.json').write_text(json.dumps({'source':str(source),'reference_only':True,'renderers':report},indent=2))
print('REFERENCE_OK',len(report),'material bindings,',len(saved),'textures extracted')
