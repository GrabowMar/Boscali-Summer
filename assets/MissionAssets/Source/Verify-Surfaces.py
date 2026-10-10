"""Verify real bundle PBR bindings and create a before/after evidence sheet."""
import os,sys,json
from pathlib import Path
sys.path.insert(0,str(Path(os.environ['LOCALAPPDATA'])/'nomodkit/blender-pylibs'))
import UnityPy
from PIL import Image,ImageDraw,ImageFont
pack=Path(sys.argv[1]).resolve(); env=UnityPy.load(str(pack/(pack.name+'.nobp')))
checked=[]
for o in env.objects:
    if o.type.name!='Material': continue
    mat=o.read(); key=mat.m_Name.removeprefix('FutureOps_')
    if not (pack/'Textures'/(key+'_MetalGloss.png')).exists(): continue
    textures=dict(mat.m_SavedProperties.m_TexEnvs)
    for prop,suffix in (('_BaseMap','Albedo'),('_BumpMap','Normal'),('_MetallicGlossMap','MetalGloss')):
        tex=textures[prop].m_Texture.read()
        assert tex.m_Name==key+'_'+suffix,(key,prop,tex.m_Name)
        assert tex.m_Width==512 and tex.m_Height==512
    checked.append(key)
assert len(set(checked))>=10,checked
(pack/'surface-validation.json').write_text(json.dumps({'bundle':pack.name+'.nobp','verified_pbr_materials':sorted(set(checked)),'channels':['albedo','normal','metallic_smoothness'],'reference_art_included':False},indent=2))
print('SURFACES_OK',pack.name,sorted(set(checked)))
if len(sys.argv)>2:
    before=Path(sys.argv[2]); names=['MobileJammer','RescueCrashSite','OpticalSatellite','OrbitalServiceTug']
    sheet=Image.new('RGB',(1000,1630),(22,29,34)); draw=ImageDraw.Draw(sheet)
    font=ImageFont.truetype('C:/Windows/Fonts/seguisb.ttf',24)
    draw.text((18,10),'BLENDER / BEFORE',font=font,fill='white')
    draw.text((518,10),'AFTER',font=font,fill='white')
    for row,name in enumerate(names):
        y=52+row*390
        for col,folder in enumerate((before,pack/'Previews')):
            im=Image.open(folder/(name+'.png')).convert('RGB'); im.thumbnail((480,360))
            sheet.paste(im,(col*500+10,y))
        draw.text((18,y+360),name,font=font,fill=(206,216,220))
    sheet.save(pack/'Previews/QualityComparison.jpg',quality=95)
