using System;

namespace BoscaliSummer.Modules.FireAndDestruction.Domain
{
    /// <summary>
    /// Torn-hole math shared with the building-gallery shader. A card keeps the real
    /// wall and covers only the mouth: opaque inside the hole and on its lip, empty outside.
    /// </summary>
    internal static class BreachShape
    {
        internal struct Sample
        {
            public float Scorch;
            public float Hole;
            public float Soot;
            public float Heat;
            public float Bars;
        }

        internal static Sample Blast(float u, float v, float seed)
        {
            float scorch = Breach(u, v, seed, out float hole, out float soot, out float heat);
            return new Sample
            {
                Scorch = scorch,
                Hole = hole,
                Soot = soot,
                Heat = heat,
                Bars = hole > 0.22f ? Rebar(u, v, seed) : 0f,
            };
        }

        internal static Sample Bay(float u, float v, float seed)
        {
            Ruin(u, v, seed, out float scorch, out float hole, out float soot, out float heat);
            return new Sample
            {
                Scorch = scorch,
                Hole = hole,
                Soot = soot,
                Heat = heat,
                Bars = hole > 0.22f ? Rebar(u * 0.8f, v * 0.55f - 0.05f, seed) : 0f,
            };
        }

        /// <summary>u,v are the gallery window, about -1..1. Alpha 0 is untouched wall.</summary>
        internal static void Texel(float u, float v, float seed, bool bay, out byte r, out byte g, out byte b, out byte a)
        {
            Sample s = bay ? Bay(u, v, seed) : Blast(u, v, seed);
            float lip = Sat((s.Scorch - s.Hole) * 1.6f) * Sat(s.Hole * 5f + 0.2f);
            float alpha = Sat(s.Hole + lip * 0.85f);
            if (alpha < 0.42f)
            {
                r = g = b = a = 0;
                return;
            }

            float grain = Hash21(Floor(v * 6f), Floor(u * 4f + seed));
            float br = Lerp(0.34f, 0.52f, grain);
            float bg = Lerp(0.18f, 0.28f, grain);
            float bb = Lerp(0.12f, 0.16f, grain);
            float fr = Lerp(0.40f, 0.52f, s.Heat);
            float fg = Lerp(0.34f, 0.32f, s.Heat);
            float fb = Lerp(0.28f, 0.22f, s.Heat);
            float rim = Sat(1f - s.Hole);
            float cr = Lerp(fr, br, rim);
            float cg = Lerp(fg, bg, rim);
            float cb = Lerp(fb, bb, rim);
            float soot = bay ? s.Soot * 0.65f : s.Soot;
            cr *= Lerp(1f, 0.28f, soot);
            cg *= Lerp(1f, 0.24f, soot);
            cb *= Lerp(1f, 0.20f, soot);

            float inside = Sat((s.Hole - 0.35f) * 3.5f);
            float ir = Lerp(bay ? 0.04f : 0.07f, 0.36f, s.Bars);
            float ig = Lerp(bay ? 0.03f : 0.05f, 0.28f, s.Bars);
            float ib = Lerp(bay ? 0.025f : 0.04f, 0.22f, s.Bars);
            cr = Lerp(cr, ir, inside);
            cg = Lerp(cg, ig, inside);
            cb = Lerp(cb, ib, inside);
            cr += s.Heat * 0.75f;
            cg += s.Heat * 0.18f;
            cb += s.Heat * 0.03f;

            r = ToByte(cr);
            g = ToByte(cg);
            b = ToByte(cb);
            a = ToByte(alpha);
        }

        private static float Breach(float u, float v, float seed, out float hole, out float soot, out float heat)
        {
            float side = Step(0.5f, seed) * 2f - 1f;
            float driftX = (Hash21(seed, 1.1f) - 0.5f) * 0.10f;
            float driftY = (Hash21(seed, 1.2f) - 0.5f) * 0.10f;
            float s1 = Hash21(seed, 2.4f);
            float s2 = Hash21(seed, 2.5f);
            float main = Blob(u, v, driftX + side * 0.02f, driftY, 0.30f + 0.08f * s1, 0.36f + 0.10f * s2, seed, 1f);
            float flank = Blob(u, v, driftX + side * 0.16f, driftY - 0.16f, 0.22f, 0.20f, seed, 2f);
            float chip = Blob(u, v, driftX - side * 0.12f, driftY + 0.16f, 0.16f, 0.18f, seed, 3f);
            float inside = Max(main, Max(flank, chip));
            hole = Sat(inside * 5.5f);
            float scorch = Max(Sat((inside + 0.38f) * 3f), hole);
            float pcX = u - driftX;
            float pcY = v - driftY;
            float ang = Atan2(pcX, pcY + 1e-4f);
            float a0 = (seed - 0.5f) * 2.4f;
            float crack = Max(Radial(ang, a0, inside, 0.55f), Radial(ang, a0 + 2.05f, inside, 0.42f));
            crack = Max(crack, Radial(ang, a0 - 1.65f, inside, 0.48f));
            float ring = Sat(1f - Abs(inside + 0.16f) * 7f);
            ring *= Sat(pcY + 0.05f) * Sat(0.8f - Abs(ang) * 0.35f);
            float above = Sat(pcY - 0.10f);
            float plumeW = 0.07f + above * 0.18f;
            float plume = Sat((plumeW - Abs(pcX - side * 0.03f)) * 6f);
            plume *= Sat(1.05f - above * 1.3f);
            plume *= Lerp(0.4f, 1f, Hash21(Floor(pcY * 8f + seed * 3f), seed * 2.2f));
            soot = Sat(plume * 0.8f + crack + ring * 0.85f);
            float low = Sat(-v * 1.8f) * Sat(v + 0.55f);
            float lip = Sat(1f - Abs(inside) * 4f);
            heat = hole * low * (0.7f + 0.3f * lip);
            return scorch;
        }

        private static void Ruin(float u, float v, float seed, out float scorch, out float hole, out float soot, out float heat)
        {
            float side = Step(0.5f, seed) * 2f - 1f;
            float col = Floor(u * 4f + seed * 1.5f);
            float top = 0.58f + Hash21(col, seed * 2.4f) * 0.28f;
            top += (Hash21(col, seed * 6.2f) - 0.5f) * 0.1f;
            top += Sat(side * u + 0.25f) * 0.14f;
            float inside = top - v;
            float jamb = 0.8f + (Hash21(Floor(v * 2.2f + seed), seed * 3.3f) - 0.5f) * 0.2f;
            inside = Min(inside, jamb - Abs(u - side * 0.05f));
            inside += (Hash21(Floor(u * 8f + seed * 4f), seed) - 0.5f) * 0.07f;
            inside *= Sat((v + 0.05f) * 16f);
            hole = Sat(inside * 7f);
            scorch = Sat((inside + 0.1f) * 4.2f);
            scorch = Max(scorch, hole);
            soot = Sat((top + 0.06f - v) * 2.2f) * Sat(0.72f - Abs(u));
            heat = hole * Sat(0.05f - v) * 0.2f;
        }

        private static float Rebar(float u, float v, float seed)
        {
            float side = Step(0.5f, seed) * 2f - 1f;
            float yA = Lerp(-0.02f, 0.14f, Hash21(seed, 2.8f));
            float yB = yA - Lerp(0.16f, 0.28f, Hash21(seed, 5.5f));
            float a = Bar(u, v, yA, 0.07f, -0.26f, 0.22f, seed);
            float b = Bar(u, v, yB, 0.05f, -0.18f, 0.16f, seed + 1.3f);
            float stub = Stirrup(u, v, side * 0.10f, -0.22f, 0.20f, seed + 2.6f);
            return Sat(a + b + stub);
        }

        private static float Blob(float u, float v, float cx, float cy, float rx, float ry, float seed, float k)
        {
            float qx = (u - cx) / Math.Max(rx, 0.04f);
            float qy = (v - cy) / Math.Max(ry, 0.04f);
            float ang = Atan2(qx, qy);
            float sec = Floor(ang * 1.6f + seed * 9f + k);
            float jag = 0.62f + 0.45f * Hash21(sec, seed * 5f + k);
            jag -= Step(0.78f, Hash21(sec, seed * 2f + k * 3f)) * 0.28f;
            return jag - (qx * qx + qy * qy);
        }

        private static float Radial(float ang, float dir, float inside, float reach)
        {
            float d = ang - dir;
            float stroke = Sat(1f - Abs(Sin(d)) * 5f);
            float fwd = Sat(Cos(d));
            return stroke * fwd * Sat(0.06f - inside) * Sat(inside + reach);
        }

        private static float Bar(float px, float py, float y0, float sag, float x0, float x1, float seed)
        {
            float lo = Min(x0, x1);
            float hi = Max(x0, x1);
            float span = Math.Max(hi - lo, 0.05f);
            float t = (px - lo) / span;
            float on = Sat(Min(t, 1f - t) * 8f);
            float y = y0 - sag * (4f * Sat(t) * Sat(1f - t));
            y += (Hash21(Floor(t * 3f + seed * 5f), seed * 8f) - 0.5f) * 0.03f;
            return Sat((0.026f - Abs(py - y)) * 32f) * on;
        }

        private static float Stirrup(float px, float py, float x0, float y0, float y1, float seed)
        {
            float span = Math.Max(y1 - y0, 0.05f);
            float t = (py - y0) / span;
            float on = Sat(Min(t, 1f - t) * 8f);
            float x = x0 + Sin(t * 3.1f + seed * 6f) * 0.035f;
            x += (Hash21(Floor(t * 3f), seed * 4f) - 0.5f) * 0.03f;
            return Sat((0.018f - Abs(px - x)) * 40f) * on;
        }

        private static float Hash21(float x, float y)
        {
            x = Frac(x * 123.34f);
            y = Frac(y * 345.45f);
            float dot = x * (x + 34.345f) + y * (y + 34.345f);
            x += dot;
            y += dot;
            return Frac(x * y);
        }

        private static byte ToByte(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f + 0.5f);
        }

        private static float Floor(float v) => (float)Math.Floor(v);
        private static float Frac(float v) => v - Floor(v);
        private static float Sat(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Step(float edge, float x) => x >= edge ? 1f : 0f;
        private static float Abs(float v) => Math.Abs(v);
        private static float Min(float a, float b) => a < b ? a : b;
        private static float Max(float a, float b) => a > b ? a : b;
        private static float Sin(float v) => (float)Math.Sin(v);
        private static float Cos(float v) => (float)Math.Cos(v);
        private static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
    }
}
