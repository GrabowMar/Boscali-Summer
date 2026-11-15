#!/usr/bin/env python3
"""Restore the approved old pilot UV/material identity and restrained suit tints.

Original UVs and material identity come from immutable assets/PilotModels/Source.
Only the legacy blank thigh paper is replaced by matching sewn canvas; all other
Standard/data-map pixels are unchanged. Other outfits tint plain suit cloth only.
The rejected fresh atlas generator is archived under assets/PilotModels/Rejected.
"""
from __future__ import annotations
import hashlib
import json
import shutil
import sys
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

def main():
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

if __name__=='__main__':main()
