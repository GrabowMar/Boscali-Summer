#!/usr/bin/env python3
"""Improve the original pilot materials in a registered 2K player-only atlas.

Immutable assets/PilotModels/Source maps anchor the UV layout and suit identity.
A faithful detail restoration improves fabric and leather; matching normal and
roughness detail reduces the plastic appearance. Variants retain restrained tints.
Run in Blender through MCP; the native pilot's materials are never overwritten.
"""
from __future__ import annotations
import hashlib
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path.home()/'.nomodkit/blender-pylibs'))
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'assets/PilotModels/Source'
DEST=ROOT/'modules/Immersion/Assets/Source/Body'
REVIEW=ROOT/'assets/PilotModels/Review'
TINTS={'recon':(1.14,1.02,.88),'rescue':(.91,1.01,1.12),'nightops':(.72,.78,.81)}
PAPER_VERTICES=(2354,2355,2356,2357)
PAPER_UV=((.88939399,.27019301),(.98146540,.27050105),(.98144800,.22188300),(.88937694,.22157480))

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def sewn_paper(original,kind):
    """The dedicated old paper quad only, plus one texel of safe UV gutter."""
    w,h=original.size
    mask=Image.new('L',original.size)
    ImageDraw.Draw(mask).polygon([(u*w,(1-v)*h) for u,v in PAPER_UV],fill=255)
    mask=mask.filter(ImageFilter.MaxFilter(3))
    box=mask.getbbox();x0,y0,x1,y1=box;pw,ph=x1-x0,y1-y0
    if kind=='albedo':
        # Reuse the existing upper pouch's canvas, keeping this suit's own weave.
        donor=original.crop((round(w*203/1024),round(h*58/1024),round(w*282/1024),round(h*109/1024)))
        patch=donor.resize((pw,ph),Image.Resampling.BICUBIC)
        color=np.median(np.asarray(patch).reshape(-1,3),axis=0)
        dark=tuple(np.uint8(color*.68));thread=tuple(np.uint8(np.minimum(color*1.30+3,180)))
        p=ImageDraw.Draw(patch)
        p.rounded_rectangle((3,3,pw-4,ph-4),radius=4,outline=dark,width=1)
        for x in range(8,pw-7,8):
            for y in (6,ph-7):p.line((x,y,min(x+3,pw-7),y),fill=thread,width=1)
        for y in range(9,ph-8,8):
            for x in (6,pw-7):p.line((x,y,x,min(y+3,ph-8)),fill=thread,width=1)
    elif kind=='normal':
        donor=original.crop((round(w*203/1024),round(h*58/1024),round(w*282/1024),round(h*109/1024)))
        a=np.asarray(donor.resize((pw,ph),Image.Resampling.BICUBIC),dtype=np.float32)
        a[...,:2]=128+(a[...,:2]-128)*.35;a[...,2]=255
        patch=Image.fromarray(np.uint8(np.clip(a,0,255)))
    else:patch=Image.new('RGB',(pw,ph),(235,40,0)) # matte, nonmetallic canvas
    layer=original.copy();layer.paste(patch,(x0,y0))
    result=Image.composite(layer,original,mask)
    outside=np.asarray(mask)==0
    assert np.array_equal(np.asarray(result)[outside],np.asarray(original)[outside])
    return result,mask

def suit_domain(data,size):
    uv=np.asarray(data['uv']).reshape(-1,2)
    tris=np.asarray(data['triangles']).reshape(-1,3)
    idx=np.asarray(data['boneIndices']).reshape(-1,4)
    weight=np.asarray(data['boneWeights']).reshape(-1,4)
    influence=np.zeros((len(uv),16))
    for slot in range(4):influence[np.arange(len(uv)),idx[:,slot]]+=weight[:,slot]
    suit=influence[:,[0,1,4,5,7,8,10,11,13,14]].sum(1)
    protected=influence[:,[2,3,6,9,12,15]].sum(1)
    allowed=Image.new('L',size);protect=Image.new('L',size)
    a,p=ImageDraw.Draw(allowed),ImageDraw.Draw(protect)
    for t in tris:
        points=[(float(uv[i,0]*size[0]),float((1-uv[i,1])*size[1])) for i in t]
        if suit[t].mean()>.85:a.polygon(points,fill=255)
        if protected[t].max()>.08:p.polygon(points,fill=255)
    # UV bleed belongs to the same original fabric, while glove/cuff margins
    # stay untouched. No image channel is blurred or re-baked.
    return np.asarray(allowed.filter(ImageFilter.MaxFilter(5)),dtype=np.float32)/255, np.asarray(protect.filter(ImageFilter.MaxFilter(7)))>0

def build_base():
    DEST.mkdir(parents=True,exist_ok=True);REVIEW.mkdir(parents=True,exist_ok=True)
    data=json.loads((BASE/'pilot-before.json').read_text())
    assert data['vertexCount']==7106 and len(data['triangles'])//3==11171, 'Use the immutable original pilot baseline'
    for name in ('pilot_albedo.png','pilot_normal.png','pilot_mask.png'):
        shutil.copyfile(BASE/name,DEST/name)
    original=Image.open(BASE/'pilot_albedo.png').convert('RGB')
    rgb=np.asarray(original,dtype=np.float32)/255
    r,g,b=rgb.transpose(2,0,1)
    allowed,protected=suit_domain(data,original.size)
    luma=r*.27+g*.59+b*.14
    # Yellowish woven canvas is deliberately excluded. Restrained multiplication
    # preserves the original folds, coarse weave, seams and wear at their source
    # resolution rather than replacing the material character with flat colour.
    # Colour classification is a broad material mask, not additional camouflage.
    # Hard per-texel hue tests amplified BC1 blocks when the sleeve filled the view.
    green=np.clip((g/np.maximum(r,.001)-1.0)/.08,0,1)*np.clip((g/np.maximum(b,.001)-1.0)/.12,0,1)
    plain=np.clip((b/np.maximum(g,.001)-.67)/.08,0,1)
    plain*=np.clip((.45-luma)/.10,0,1)
    mask=allowed*green*plain*(~protected)
    # Original dedicated equipment atlas (clip, webbing and hose) is never tinted.
    w,h=original.size
    mask[int(h*.414):int(h*.578),int(w*.212):int(w*.380)]=0
    soft=Image.fromarray(np.uint8(mask*255)).filter(ImageFilter.MedianFilter(5)).filter(ImageFilter.GaussianBlur(4))
    mask=np.asarray(soft,dtype=np.float32)/255
    mask[protected]=0
    mask[int(h*.414):int(h*.578),int(w*.212):int(w*.380)]=0
    protected_exact={}
    standard,patch_mask=sewn_paper(original,'albedo')
    standard.save(DEST/'pilot_albedo.png',optimize=True)
    for style,ratio in TINTS.items():
        adjusted=rgb*(1-mask[...,None])+rgb*np.asarray(ratio)[None,None,:]*mask[...,None]
        pixels=np.uint8(np.rint(np.clip(adjusted,0,1)*255))
        protected_exact[style]=bool(np.array_equal(pixels[protected],np.asarray(original)[protected]))
        assert protected_exact[style]
        repaired,_=sewn_paper(Image.fromarray(pixels),'albedo')
        repaired.save(DEST/f'pilot_albedo_{style}.png',optimize=True)
    outside_exact={}
    for name,kind in [('pilot_albedo.png','albedo'),('pilot_normal.png','normal'),('pilot_mask.png','mask')]:
        source_image=Image.open(BASE/name).convert('RGB')
        repaired,repair_mask=sewn_paper(source_image,kind)
        repaired.save(DEST/name,optimize=True)
        outside=np.asarray(repair_mask)==0
        outside_exact[name]=bool(np.array_equal(np.asarray(repaired)[outside],np.asarray(source_image)[outside]))
    assert all(outside_exact.values())
    audit={'pipeline':'Original 7106-vertex / 11171-triangle pilot UVs; fresh atlas rejected',
           'source':'assets/PilotModels/Source immutable baseline','outsideLegacyPaperPatchPixelsExact':outside_exact,
           'paperPatch':{'triangles':[6421,6422],'vertices':PAPER_VERTICES,'uv':PAPER_UV,'gutterPixels':1,'replacement':'Existing pouch canvas and restrained sewn edge'},
           'tintedPlainSuitTexels':int(np.count_nonzero(mask>.01)),
           'glovesPouchesHardware':'Original source detail retained; gloves/boots/equipment UVs protected',
           'protectedGloveBootPixelsExact':protected_exact,
           'variants':TINTS,'variantMask':'Continuous hue confidence; median5 + blur4 classification only; source detail untouched',
           'dimensions':{n:list(Image.open(DEST/n).size) for n in outside_exact},
           'nativeImageBlur':False,'nativeNormalRebake':False,'localizedCanvasNormal':True,
           'sha256':{p.name:sha(p) for p in DEST.glob('pilot_*.png')}}
    (REVIEW/'texture-audit.json').write_text(json.dumps(audit,indent=2))
    sheet=Image.new('RGB',(1536,1090),(24,29,29));d=ImageDraw.Draw(sheet)
    try:font=ImageFont.truetype('C:/Windows/Fonts/consolab.ttf',20)
    except OSError:font=ImageFont.load_default()
    files=[('ORIGINAL STANDARD','pilot_albedo.png'),('RECON','pilot_albedo_recon.png'),('RESCUE','pilot_albedo_rescue.png'),
           ('NIGHT OPS','pilot_albedo_nightops.png'),('ORIGINAL NORMAL','pilot_normal.png'),('ORIGINAL MATERIAL MASK','pilot_mask.png')]
    for i,(label,name) in enumerate(files):
        x,y=i%3*512,i//3*545
        sheet.paste(Image.open(DEST/name).resize((512,512)),(x,y+32));d.text((x+10,y+6),label,font=font,fill=(205,213,196))
    sheet.save(REVIEW/'texture-contact-sheet.png')
    print('RESULT '+json.dumps(audit))

def enhance_player_set():
    """Bake a registered 2K player atlas and matching material detail.

    The generated restoration supplies new fabric/leather detail. A confidence
    mask rejects large colour/layout changes so original UV landmarks remain
    usable; the immutable native texture inputs are never changed.
    """
    size=(2048,2048)
    generated=BASE/'pilot_albedo_hq_generated.png'
    assert generated.is_file(),'Generate the UV-registered restoration before this bake'
    legacy={name:Image.open(DEST/name).convert('RGB') for name in
            ('pilot_albedo.png','pilot_albedo_recon.png','pilot_albedo_rescue.png','pilot_albedo_nightops.png','pilot_normal.png','pilot_mask.png')}
    reference=legacy['pilot_albedo.png'].resize(size,Image.Resampling.LANCZOS)
    ref=np.asarray(reference,dtype=np.float32)/255
    candidate=Image.open(generated).convert('RGB').resize(size,Image.Resampling.LANCZOS)
    restored=np.asarray(candidate,dtype=np.float32)/255
    difference=np.abs(restored-ref).max(2)
    confidence=np.clip((.16-difference)/.10,0,1)
    confidence=np.asarray(Image.fromarray(np.uint8(confidence*255)).filter(ImageFilter.GaussianBlur(2)),dtype=np.float32)/255
    confidence*=np.clip((.23-difference)/.10,0,1)
    standard=ref*(1-confidence[...,None])+restored*confidence[...,None]
    standard=np.clip(standard,0,1)
    # Preserve the same material variations using a smooth multiplicative field.
    for name in ('pilot_albedo.png','pilot_albedo_recon.png','pilot_albedo_rescue.png','pilot_albedo_nightops.png'):
        tint=np.asarray(legacy[name].resize(size,Image.Resampling.BICUBIC),dtype=np.float32)/255
        ratio=np.where(ref>3/255,np.clip(tint/np.maximum(ref,1/255),.5,1.5),1)
        result=standard if name=='pilot_albedo.png' else standard*ratio
        Image.fromarray(np.uint8(np.rint(np.clip(result,0,1)*255))).save(DEST/name,optimize=True)
    data=json.loads((BASE/'pilot-before.json').read_text())
    allowed,protected=suit_domain(data,size)
    r,g,b=ref.transpose(2,0,1);luma=r*.27+g*.59+b*.14
    green=np.clip((g/np.maximum(r,.001)-1)/.08,0,1)*np.clip((g/np.maximum(b,.001)-1)/.12,0,1)
    canvas=np.clip((.88-b/np.maximum(g,.001))/.18,0,1)*np.clip((luma-.05)/.10,0,1)
    cloth=np.clip(green+canvas,0,1)*np.maximum(allowed,protected.astype(np.float32))
    cloth=np.asarray(Image.fromarray(np.uint8(cloth*255)).filter(ImageFilter.GaussianBlur(2)),dtype=np.float32)/255
    leather=protected.astype(np.float32)*(1-green)*np.clip((.30-luma)/.12,0,1)
    material=np.clip(cloth+leather,0,1)
    grey=standard@np.array([.27,.59,.14],dtype=np.float32)
    blur=np.asarray(Image.fromarray(np.uint8(grey*255)).filter(ImageFilter.GaussianBlur(5)),dtype=np.float32)/255
    detail=grey-blur
    dy,dx=np.gradient(detail)
    normal=np.asarray(legacy['pilot_normal.png'].resize(size,Image.Resampling.BICUBIC),dtype=np.float32)/127.5-1
    normal/=np.maximum(np.linalg.norm(normal,axis=2,keepdims=True),1e-6)
    base_normal=normal.copy()
    normal[...,0]-=np.clip(dx*.8,-.05,.05)*material
    normal[...,1]+=np.clip(dy*.8,-.05,.05)*material
    normal/=np.maximum(np.linalg.norm(normal,axis=2,keepdims=True),1e-6)
    Image.fromarray(np.uint8(np.rint(np.clip(normal*.5+.5,0,1)*255))).save(DEST/'pilot_normal.png',optimize=True)
    packed=np.asarray(legacy['pilot_mask.png'].resize(size,Image.Resampling.BICUBIC),dtype=np.float32)/255
    old_smoothness=packed[...,1].copy()
    cloth_smoothness=np.clip(.16+detail*.3,.10,.23)
    leather_smoothness=np.clip(.28+detail*.4,.20,.36)
    packed[...,1]=packed[...,1]*(1-cloth)+cloth_smoothness*cloth
    packed[...,1]=packed[...,1]*(1-leather)+leather_smoothness*leather
    packed[...,2]*=1-material
    assert np.isfinite(normal).all() and np.isfinite(packed).all()
    Image.fromarray(np.uint8(np.rint(np.clip(packed,0,1)*255))).save(DEST/'pilot_mask.png',optimize=True)
    normal_change=np.degrees(np.arccos(np.clip((base_normal*normal).sum(2),-1,1)))
    audit=json.loads((REVIEW/'texture-audit.json').read_text())
    audit['playerEnhancement']={'resolution':size,'generatedSource':str(generated.relative_to(ROOT)),
        'sourceResolution':Image.open(generated).size,'uvRegistration':'original UVs; reject large per-pixel restoration differences',
        'restorationConfidenceMean':float(confidence.mean()),'newNormalDetailMeanDegrees':float(normal_change.mean()),
        'newNormalDetailMaxDegrees':float(normal_change.max()),'clothSmoothnessMeanBefore':float(old_smoothness[cloth>.7].mean()),
        'clothSmoothnessMeanAfter':float(packed[...,1][cloth>.7].mean()),'scope':'owned player cockpit textures only',
        'normalConvention':'same tangent-space convention as original; renormalized',
        'materialMaps':'AO(R) retained, fine cloth/leather roughness(G), material-specific metal(B)'}
    audit['dimensions']={name:list(Image.open(DEST/name).size) for name in legacy}
    audit['sha256']={p.name:sha(p) for p in DEST.glob('pilot_*.png')}
    # Original-preservation assertions above describe the immutable base pass;
    # this explicitly requested HQ pass improves the final player pixels.
    audit['basePassPreservation']=audit.pop('outsideLegacyPaperPatchPixelsExact')
    audit['gloveBootBasePassExact']=audit.pop('protectedGloveBootPixelsExact')
    (REVIEW/'texture-audit.json').write_text(json.dumps(audit,indent=2))
    sheet=Image.new('RGB',(1536,1090),(24,29,29));draw=ImageDraw.Draw(sheet)
    font=ImageFont.truetype('C:/Windows/Fonts/consolab.ttf',20)
    for i,(label,name) in enumerate([('PLAYER HQ STANDARD','pilot_albedo.png'),('RECON','pilot_albedo_recon.png'),('RESCUE','pilot_albedo_rescue.png'),('NIGHT OPS','pilot_albedo_nightops.png'),('PLAYER HQ NORMAL','pilot_normal.png'),('PLAYER HQ MATERIAL','pilot_mask.png')]):
        x,y=i%3*512,i//3*545
        sheet.paste(Image.open(DEST/name).resize((512,512)),(x,y+32));draw.text((x+10,y+6),label,font=font,fill=(205,213,196))
    sheet.save(REVIEW/'texture-contact-sheet.png')
    print('PLAYER_HQ_RESULT '+json.dumps(audit['playerEnhancement']))

def main():
    # Bake away from files loaded by Blender/Unity; publish only complete PNGs.
    global DEST
    destination=DEST
    destination.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='pilot-textures-',dir=destination.parent) as temporary:
        try:
            DEST=Path(temporary)
            build_base()
            enhance_player_set()
            collar_material()
            for source in DEST.glob('pilot_*.png'):
                os.replace(source,destination/source.name)
        finally:
            DEST=destination


def collar_material():
    """Give the closing lining its own cloth swatch and matched boundary AO."""
    contract_path=BASE/'pilot_collar_atlas.json'
    assert contract_path.is_file(),'The dedicated collar UVs require Source/pilot_collar_atlas.json'
    contract=json.loads(contract_path.read_text())
    chart=np.asarray(contract['capChartUV169']);faces=np.asarray(contract['capTriangles300'])
    native_mask=np.asarray(Image.open(DEST/'pilot_mask.png').convert('RGB'),dtype=np.float64)
    def sample(uv):
        x=np.clip(uv[0]*2048-.5,0,2047);y=np.clip((1-uv[1])*2048-.5,0,2047)
        ix,iy=int(x),int(y);fx,fy=x-ix,y-iy
        return (native_mask[iy,ix]*(1-fx)+native_mask[iy,min(ix+1,2047)]*fx)*(1-fy)+(native_mask[min(iy+1,2047),ix]*(1-fx)+native_mask[min(iy+1,2047),min(ix+1,2047)]*fx)*fy
    boundary=[]
    for point in contract['boundary36']:
        weights=np.asarray([item['areaWeight'] for item in point['native']]);weights/=weights.sum()
        boundary.append(sum(sample(item['uv'])*weight for item,weight in zip(point['native'],weights)))
    boundary=np.asarray(boundary);neighbours=[set() for _ in chart]
    for face in faces:
        for a,b in zip(face,np.roll(face,-1)):neighbours[a].add(b);neighbours[b].add(a)
    matrix=np.zeros((133,133));rhs=np.zeros((133,3))
    for i in range(36,169):
        for j in neighbours[i]:
            weight=1/max(np.linalg.norm(chart[i]-chart[j]),1e-9);matrix[i-36,i-36]+=weight
            if j<36:rhs[i-36]+=weight*boundary[j]
            else:matrix[i-36,j-36]-=weight
    values=np.vstack((boundary,np.linalg.solve(matrix,rhs)))
    x,y,w,h=contract['newInteriorPixelRectTopLeft'];pad=contract['gutterPixels']
    pixels=np.c_[chart[:,0]*(w-1),(1-chart[:,1])*(h-1)]
    material=np.broadcast_to(boundary.mean(0),(h,w,3)).copy()
    for face in faces:
        points=pixels[face];low=np.maximum(np.floor(points.min(0)).astype(int),0);high=np.minimum(np.ceil(points.max(0)).astype(int),[w-1,h-1])
        xx,yy=np.meshgrid(np.arange(low[0],high[0]+1),np.arange(low[1],high[1]+1));q=np.stack((xx,yy),axis=-1)
        a,b,c=points;v0=b-a;v1=c-a;det=v0[0]*v1[1]-v0[1]*v1[0]
        if abs(det)<1e-10:continue
        rel=q-a;s=(rel[...,0]*v1[1]-rel[...,1]*v1[0])/det;t=(v0[0]*rel[...,1]-v0[1]*rel[...,0])/det
        inside=(s>=-1e-6)&(t>=-1e-6)&(s+t<=1+1e-6)
        shade=(1-s-t)[...,None]*values[face[0]]+s[...,None]*values[face[1]]+t[...,None]*values[face[2]]
        region=material[low[1]:high[1]+1,low[0]:high[0]+1];region[inside]=shade[inside]
    lo,hi=np.asarray(contract['oldCapUVBounds']);donor_box=(round(lo[0]*2048),round((1-hi[1])*2048),round(hi[0]*2048),round((1-lo[1])*2048))
    patches={}
    for name in ('pilot_albedo.png','pilot_albedo_recon.png','pilot_albedo_rescue.png','pilot_albedo_nightops.png'):
        patches[name]=np.asarray(Image.open(DEST/name).convert('RGB').crop(donor_box).resize((w,h),Image.Resampling.LANCZOS)).copy()
    grey=patches['pilot_albedo.png']@np.array([.27,.59,.14])/255
    blur=np.asarray(Image.fromarray(np.uint8(grey*255)).filter(ImageFilter.GaussianBlur(3)))/255
    dy,dx=np.gradient(grey-blur);normal=np.stack((-dx*.4,dy*.4,np.ones_like(dx)),axis=-1);normal/=np.linalg.norm(normal,axis=2,keepdims=True)
    patches['pilot_normal.png']=np.uint8(np.rint((normal*.5+.5)*255))
    patches['pilot_mask.png']=np.uint8(np.rint(np.clip(material,0,255)))
    for name,patch in patches.items():
        image=Image.open(DEST/name).convert('RGB');padded=np.pad(patch,((pad,pad),(pad,pad),(0,0)),mode='edge');image.paste(Image.fromarray(padded),(x-pad,y-pad));image.save(DEST/name,optimize=True)
    audit=json.loads((REVIEW/'texture-audit.json').read_text())
    audit['collarMaterial']={'contract':str(contract_path.relative_to(ROOT)),'atlasRect':contract['newInteriorPixelRectTopLeft'],'unusedOutsideCap':contract['existingOutsideCapUVBBoxOverlaps']==0,'boundaryAORange':(boundary[:,0]/255).tolist(),'method':'Native boundary AO/smoothness/metal with harmonic interior; mild seamless cloth normal; eight-pixel gutter'}
    audit['sha256']={p.name:sha(p) for p in DEST.glob('pilot_*.png')}
    (REVIEW/'texture-audit.json').write_text(json.dumps(audit,indent=2))

if __name__=='__main__':main()
