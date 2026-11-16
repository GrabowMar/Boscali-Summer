// BROKEN HELMET (2026-11-16): the visor's glitch math, shared by NOA/UI/VisorGlitch (line art, sprites) and NOA/TMP/VisorGlitchSDF (words).
// Everything is driven by globals the mod sets once a frame (Core/Fx/VisorGlitch.cs); a material carries nothing of its own.
//   _BH_Fx      x master (0: every term off, ghost passes collapse; the shader then draws exactly what the stock UI/TMP shader does)
//               y time (s)   z step seed (changes ~14 times a second: blocks and bar jitter hold for a step)   w unused
//   _BH_Tear0/1/2  one tear band each: x centre (0 top .. 1 bottom of the screen), y height (0..1), z sideways shift in screen px, w seam strength
//   _BH_Chroma  x ghost split in px (red left, cyan right)   y fine scanline shimmer 0..1   z vertical roll in px   w unused
//   _BH_Drop    x block density 0..1 (24x8 px cells)   y brightness inside a dropped block (0 blank)   z whole-picture flicker multiplier   w block seed
//   _BH_Bars    x strength 0..1   y position of the first bar (0..1, drifts)   z spacing to the second bar   w sideways jitter in px inside a bar
//   _BH_Calm0/1 screen rects (xmin, ymin, xmax, ymax, 0..1 top-origin; max < min: none) where the effect is eased to a mild version
// Displacement is in whole screen pixels in the vertex stage; blocks, scanlines, bars, flicker and seams are in the fragment stage.
// Original code, written for this project; no source is copied.
#ifndef NOA_VISOR_GLITCH_INCLUDED
#define NOA_VISOR_GLITCH_INCLUDED

float4 _BH_Fx, _BH_Tear0, _BH_Tear1, _BH_Tear2, _BH_Chroma, _BH_Drop, _BH_Bars, _BH_Calm0, _BH_Calm1;

float bh_hash(float2 p)
{
    float3 q = frac(float3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// Screen position in px, y from the top (the platform's clip flip taken out).
float2 bh_screen(float4 clip)
{
    float2 ndc = clip.xy / max(abs(clip.w), 0.0001) * sign(clip.w + 0.00001);
    return float2((ndc.x * 0.5 + 0.5) * _ScreenParams.x, (0.5 - 0.5 * ndc.y * _ProjectionParams.x) * _ScreenParams.y);
}

float bh_inrect(float2 v, float4 r)
{
    return step(r.x, v.x) * step(v.x, r.z) * step(r.y, v.y) * step(v.y, r.w);
}

// 1 inside a calm rect (the banner, the arcs, the threat scope), else 0.
float bh_calm(float2 sp)
{
    float2 v = sp / _ScreenParams.xy;
    return saturate(bh_inrect(v, _BH_Calm0) + bh_inrect(v, _BH_Calm1));
}

float bh_bar(float v)
{
    float d1 = abs(frac(v - _BH_Bars.y + 0.5) - 0.5);
    float d2 = abs(frac(v - _BH_Bars.y - _BH_Bars.z + 0.5) - 0.5);
    return saturate(smoothstep(0.045, 0.0, d1) + 0.7 * smoothstep(0.03, 0.0, d2)) * _BH_Bars.x;
}

float bh_band(float4 t, float v)
{
    return step(abs(v - t.x), t.y * 0.5) * step(0.0001, t.y);
}

// The sideways and vertical shift of a point at screen position sp (px, y from the top), in whole px.
float2 bh_displace(float2 sp)
{
    float v = sp.y / _ScreenParams.y;
    float calm = bh_calm(sp);
    float k = lerp(1.0, 0.3, calm);
    float dx = _BH_Tear0.z * bh_band(_BH_Tear0, v) + _BH_Tear1.z * bh_band(_BH_Tear1, v) + _BH_Tear2.z * bh_band(_BH_Tear2, v);
    dx += _BH_Bars.w * (bh_hash(float2(floor(v * 120.0), _BH_Fx.z)) * 2.0 - 1.0) * bh_bar(v);
    return floor(float2(dx, _BH_Chroma.z) * k + 0.5);
}

// Brightness multiplier of a pixel (dropout blocks, flicker, fine scanlines, rolling bars) and the extra seam light along the tear bands.
float bh_light(float2 sp, out float seam)
{
    float v = sp.y / _ScreenParams.y;
    float calm = bh_calm(sp);
    float m = 1.0;
    float cell = _BH_Drop.x * lerp(1.0, 0.12, calm);
    if (cell > 0.0001)
    {
        float2 id = floor(sp / float2(24.0, 8.0));
        float on = step(bh_hash(id + _BH_Drop.w), cell);
        m *= lerp(1.0, lerp(_BH_Drop.y, 1.0, calm * 0.8), on);
    }
    m *= lerp(1.0, _BH_Drop.z, 1.0 - calm * 0.65);
    if (_BH_Chroma.y > 0.001)
        m *= 1.0 - _BH_Chroma.y * 0.4 * step(0.5, frac(sp.y * 0.5 + _BH_Fx.y * 6.0)) * lerp(1.0, 0.4, calm);
    m *= 1.0 - 0.72 * bh_bar(v) * lerp(1.0, 0.5, calm);
    float hpx = _ScreenParams.y;
    float s0 = _BH_Tear0.w * step(0.0001, _BH_Tear0.y) * step(abs(abs(v - _BH_Tear0.x) * hpx - _BH_Tear0.y * hpx * 0.5), 1.0);
    float s1 = _BH_Tear1.w * step(0.0001, _BH_Tear1.y) * step(abs(abs(v - _BH_Tear1.x) * hpx - _BH_Tear1.y * hpx * 0.5), 1.0);
    float s2 = _BH_Tear2.w * step(0.0001, _BH_Tear2.y) * step(abs(abs(v - _BH_Tear2.x) * hpx - _BH_Tear2.y * hpx * 0.5), 1.0);
    seam = saturate(s0 + s1 + s2) * lerp(1.0, 0.4, calm);
    return m;
}

// A ghost pass moves its geometry out of the picture when there is nothing to show.
bool bh_ghost_off()
{
    return _BH_Fx.x <= 0.0 || _BH_Chroma.x < 0.5;
}

#endif
