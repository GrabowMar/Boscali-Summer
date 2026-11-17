"""Deterministic painted-shell maps. Authored from scratch; no third-party images."""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageFilter, ImageDraw
import math


def main():
    out = Path(__file__).resolve().parents[1] / "Textures"
    out.mkdir(parents=True, exist_ok=True)
    width, height = 4096, 2048
    rng = np.random.default_rng(74)
    grain = rng.normal(0, .72, (height, width)).astype(np.float32)
    broad = Image.fromarray(rng.integers(90, 165, (64, 128), dtype=np.uint8)).resize((width, height), Image.Resampling.BICUBIC).filter(ImageFilter.GaussianBlur(15))
    variation = (np.asarray(broad).astype(np.float32) - 127) * .055 + grain
    rgb = np.clip(np.array([194, 196, 188])[None, None, :] + variation[:, :, None], 0, 255).astype(np.uint8)
    # Fine longitudinal handling scuffs, deliberately restrained on the maintained enamel.
    for i in range(100):
        x, y = rng.integers(20, width-20), rng.integers(0, height)
        length = int(rng.integers(3, 24))
        rgb[y:y+1, x:x+length] = np.maximum(rgb[y:y+1, x:x+length].astype(np.int16)-int(rng.integers(2, 7)), 0)
    Image.fromarray(rgb).save(out / "SA74N_albedo.png")
    surface = np.zeros((height, width, 4), dtype=np.uint8)
    surface[:, :, 3] = np.clip(73 + variation * 1.7, 58, 94).astype(np.uint8)
    Image.fromarray(surface).resize((1024, 512), Image.Resampling.BOX).save(out / "SA74N_surface.png")
    # Native weapon-list convention: a 512x256 white outline on black, nose left.
    scale = 3
    icon = Image.new("RGB", (512*scale, 256*scale), "black")
    draw = ImageDraw.Draw(icon)
    def p(y, r): return ((256-y*55)*scale, (135-r*55)*scale)
    profile=[(-4.2,.49),(-4.05,.55),(-3.8,.59),(-3.55,.70),(-3.3,.88),(-3.05,1.06),(-2.8,1.25),(-2.6,1.39),(-2.42,1.47),(-2.25,1.5),(2.4,1.5)]
    profile += [(2.4+1.8*math.sin(i*math.pi/40), 1.5*math.cos(i*math.pi/40)) for i in range(1,21)]
    outline=[p(y,r) for y,r in profile]+[p(y,-r) for y,r in reversed(profile)]
    draw.line(outline+[outline[0]], fill=(225,230,230), width=2*scale, joint="curve")
    for y in [-2.23,-.82,1.2,2.4]: draw.line([p(y,-1.5),p(y,1.5)],fill=(180,185,185),width=scale)
    for sign in [-1,1]:
        fin=[p(y,r*sign) for r,y in [(.63,-4.1),(1.70,-4.1),(1.70,-3.64),(1.27,-2.9),(.91,-3.14),(.63,-4.1)]]
        draw.line(fin, fill=(225,230,230),width=2*scale,joint="curve")
    for y in [-1.2,1.15]:
        draw.line([p(y-.34,1.5),p(y-.34,1.68),p(y+.34,1.68),p(y+.34,1.5)],fill=(225,230,230),width=scale)
    icons=out.parent/"Icons"
    icons.mkdir(exist_ok=True)
    icon.resize((512,256),Image.Resampling.LANCZOS).save(icons/"Sa74N_Icon.png")
    print(json.dumps({"maps": [str(out / "SA74N_albedo.png"), str(out / "SA74N_surface.png")], "size": [width, height], "albedo_range": [int(rgb.min()), int(rgb.max())], "smoothness_range": [int(surface[:,:,3].min()), int(surface[:,:,3].max())]}))


if __name__ == "__main__":
    main()
