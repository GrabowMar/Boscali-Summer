using System;
using NOAvionics;

namespace BoscaliSummer.Modules.Progression.Runtime
{
    /// <summary>
    /// Draws an <see cref="EmblemDesign"/> from signed distances: every edge is anti-aliased
    /// analytically, so one design is equally clean as a 128 px card sprite and as a 256 px tail
    /// marking. Pure arithmetic, no engine types; the test suite renders it directly.
    /// Space is x right, y up, both -1..1; a distance is negative inside its shape.
    /// </summary>
    internal static class EmblemRaster
    {
        // Rim bands, measured inward from the outer edge: keyline, rim in the ink tincture, inner keyline.
        private const float RimKey = 0.030f, RimInk = 0.088f, RimInner = 0.112f;
        private const float Keyline = 0.022f;
        private const float Far = 1e6f;

        /// <summary>Where a shape carries its charge, top stars and base line.</summary>
        private readonly struct Layout
        {
            public readonly float Cy, Scale, StarY, StarSpread, BaseY;
            public Layout(float cy, float scale, float starY, float starSpread, float baseY)
            { Cy = cy; Scale = scale; StarY = starY; StarSpread = starSpread; BaseY = baseY; }
        }

        private static readonly Layout[] Layouts =
        {
            new Layout(0.08f, 0.50f, 0.58f, 0.40f, -0.50f),  // SHIELD
            new Layout(0.00f, 0.54f, 0.58f, 0.32f, -0.60f),  // ROUNDEL
            new Layout(0.24f, 0.36f, 0.52f, 0.44f, -0.30f),  // DELTA
            new Layout(0.18f, 0.44f, 0.64f, 0.32f, -0.30f),  // BANNER
            new Layout(0.00f, 0.52f, 0.56f, 0.34f, -0.58f),  // HEX
            new Layout(0.00f, 0.40f, 0.46f, 0.17f, -0.50f),  // DIAMOND
            new Layout(0.00f, 0.54f, 0.60f, 0.40f, -0.62f),  // PATCH
            new Layout(-0.04f, 0.50f, 0.54f, 0.34f, -0.64f), // ARCH
        };

        /// <summary>
        /// Fills <paramref name="rgba"/> (size x size x 4, straight alpha, bottom row first) with the design.
        /// <paramref name="shaded"/> adds the soft top light of an enamel badge; paint on metal stays flat.
        /// </summary>
        public static void Render(EmblemDesign design, EmblemFinish finish, int size, bool shaded, byte[] rgba)
        {
            if (rgba == null || size <= 0 || rgba.Length < size * size * 4) throw new ArgumentException("emblem buffer too small");
            float px = 2f / size;
            Layout layout = Layouts[design.Shape];
            bool numbered = design.Number > 0, bars = design.Accent == EmblemDesign.AccentBars;
            float cy = layout.Cy, scale = layout.Scale;
            // The charge makes room for whatever shares the field with it.
            if (numbered || bars) { cy += 0.20f * scale; scale *= 0.84f; }
            if (design.Accent == EmblemDesign.AccentStars) { cy -= 0.14f * scale; scale *= 0.86f; }
            if (design.Accent == EmblemDesign.AccentLaurel) scale *= 0.80f;
            float baseY = layout.BaseY + (design.Accent == EmblemDesign.AccentRing ? 0.11f : 0f);

            Rgba field = EmblemDesign.Paint(design.Field, finish);
            Rgba second = EmblemDesign.Paint(design.Second, finish);
            Rgba ink = EmblemDesign.Paint(design.Ink, finish);
            Rgba key = finish == EmblemFinish.LowVis ? new Rgba(0.22f, 0.22f, 0.22f) : new Rgba(0.05f, 0.05f, 0.06f);

            int o = 0;
            for (int row = 0; row < size; row++)
            {
                float y = (row + 0.5f) * px - 1f;
                for (int col = 0; col < size; col++, o += 4)
                {
                    float x = (col + 0.5f) * px - 1f;
                    float edge = Shape(design.Shape, x, y);
                    float alpha = Cover(edge, px);
                    if (alpha <= 0f)
                    {
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = rgba[o + 3] = 0;
                        continue;
                    }

                    float r = field.R, g = field.G, b = field.B;
                    if (design.Division != 0)
                        Mix(ref r, ref g, ref b, second, Cover(-Division(design.Division, x, y - layout.Cy), px));

                    // Everything in the ink tincture is one shape, so it shares one keyline.
                    float mark = Far;
                    float qx = x / scale, qy = (y - cy) / scale;
                    if (qx > -1.2f && qx < 1.2f && qy > -1.2f && qy < 1.2f)
                        mark = Charge(design.Charge, qx, qy) * scale;
                    mark = Math.Min(mark, Accent(design, layout, x, y, cy, scale, edge));
                    if (numbered) mark = Math.Min(mark, Digits(design.Number, x, y - baseY));
                    Mix(ref r, ref g, ref b, key, Cover(mark - Keyline, px));
                    Mix(ref r, ref g, ref b, ink, Cover(mark, px));

                    Mix(ref r, ref g, ref b, key, Cover(-(edge + RimInner), px));
                    Mix(ref r, ref g, ref b, ink, Cover(-(edge + RimInk), px));
                    Mix(ref r, ref g, ref b, key, Cover(-(edge + RimKey), px));

                    if (shaded)
                    {
                        float light = 1f + 0.10f * y;
                        r *= light; g *= light; b *= light;
                    }
                    rgba[o] = Byte(r); rgba[o + 1] = Byte(g); rgba[o + 2] = Byte(b); rgba[o + 3] = Byte(alpha);
                }
            }
        }

        // ---- Outer shapes ----------------------------------------------------------------

        private static readonly float[] DeltaPoints = { -0.84f, 0.72f, 0.84f, 0.72f, 0f, -0.84f };
        private static readonly float[] BannerPoints = { -0.66f, 0.88f, 0.66f, 0.88f, 0.66f, -0.88f, 0f, -0.50f, -0.66f, -0.88f };
        private static readonly float[] HexPoints =
            { 0f, 0.90f, 0.779f, 0.45f, 0.779f, -0.45f, 0f, -0.90f, -0.779f, -0.45f, -0.779f, 0.45f };
        private static readonly float[] DiamondPoints = { 0f, 0.90f, 0.78f, 0f, 0f, -0.90f, -0.78f, 0f };

        private static float Shape(int shape, float x, float y)
        {
            switch (shape)
            {
                case 1: return Len(x, y) - 0.93f;
                case 2: return Poly(DeltaPoints, x, y) - 0.10f;
                case 3: return Poly(BannerPoints, x, y) - 0.04f;
                case 4: return Poly(HexPoints, x, y) - 0.04f;
                case 5: return Poly(DiamondPoints, x, y) - 0.06f;
                case 6: return RoundBox(x, y, 0.86f, 0.86f, 0.22f);
                case 7: return Math.Max(y > 0.12f ? Len(x, y - 0.12f) - 0.82f : Math.Abs(x) - 0.82f, -0.90f - y);
                default: return Shield(x, y);
            }
        }

        /// <summary>Heater shield: straight flanks down to the shoulder, then two arcs meeting at the foot.</summary>
        private static float Shield(float x, float y)
        {
            const float half = 0.80f, top = 0.88f, shoulder = 0.18f, arcX = 0.384f, arc = 1.184f;
            float ax = Math.Abs(x);
            float flank = y > shoulder ? ax - half : Len(ax + arcX, y - shoulder) - arc;
            return Math.Max(flank, y - top);
        }

        // ---- Field divisions: positive where the second tincture goes -----------------------

        private static float Division(int division, float x, float y)
        {
            switch (division)
            {
                case 1: return x;
                case 2: return -y;
                case 3: return -(x + y) * 0.7071f;
                case 4: return Math.Min(Math.Abs(x), Math.Abs(y)) * (x * y < 0f ? 1f : -1f);
                case 5: return (0.22f - 0.85f * Math.Abs(x) - y) * 0.762f;
                case 6: return y - 0.36f;
                case 7: return Stripe(y + 1f, 0.36f);
                case 8:
                {
                    const float sector = (float)(Math.PI / 6.0);
                    float angle = (float)Math.Atan2(y, x) + (float)Math.PI;
                    float turn = angle / sector, cell = (float)Math.Floor(turn), into = turn - cell;
                    float span = Math.Min(into, 1f - into) * sector * Len(x, y);
                    return ((int)cell & 1) == 1 ? span : -span;
                }
                case 9:
                {
                    const float cell = 0.42f;
                    float cx = (float)Math.Floor(x / cell), cyl = (float)Math.Floor(y / cell);
                    float fx = x / cell - cx, fy = y / cell - cyl;
                    float span = Math.Min(Math.Min(fx, 1f - fx), Math.Min(fy, 1f - fy)) * cell;
                    return (((int)cx + (int)cyl) & 1) != 0 ? span : -span;
                }
                default: return -Far;
            }
        }

        private static float Stripe(float along, float pitch)
        {
            float turn = along / pitch, cell = (float)Math.Floor(turn), into = turn - cell;
            float span = Math.Min(into, 1f - into) * pitch;
            return ((int)cell & 1) == 1 ? span : -span;
        }

        // ---- Charges, in a -1..1 box of their own ----------------------------------------------

        private static readonly float[] BoltPoints =
            { 0.25f, 0.98f, -0.55f, -0.02f, -0.05f, -0.02f, -0.30f, -0.98f, 0.55f, 0.20f, 0.08f, 0.20f };
        private static readonly float[] WingPoints =
        {
            0.28f, 0.33f, 0.98f, 0.46f, 0.94f, 0.25f, 0.78f, 0.21f, 0.78f, 0.05f,
            0.60f, 0.03f, 0.60f, -0.13f, 0.40f, -0.15f, 0.40f, -0.30f, 0.28f, -0.34f,
        };
        private static readonly float[] CrossBarPoints =
            { 0f, 1.00f, 0.17f, 0.82f, 0.17f, -0.82f, 0f, -1.00f, -0.17f, -0.82f, -0.17f, 0.82f };
        private static readonly float[] DartPoints = Mirror(new[]
        {
            0f, 1.00f, 0.07f, 0.70f, 0.12f, 0.30f, 0.20f, 0.10f, 0.95f, -0.50f, 0.95f, -0.66f,
            0.24f, -0.62f, 0.22f, -0.74f, 0.42f, -0.86f, 0.42f, -0.96f, 0.08f, -0.90f, 0f, -0.96f,
        });
        private static readonly float[] BladePoints = { 0f, 1.00f, 0.12f, 0.80f, 0.12f, -0.40f, -0.12f, -0.40f, -0.12f, 0.80f };
        private static readonly float[] SpadeHeadPoints = { 0f, 0.98f, 0.70f, 0.02f, -0.70f, 0.02f };
        private static readonly float[] SpadeStemPoints = { 0f, -0.20f, 0.30f, -0.96f, -0.30f, -0.96f };
        private static readonly float[] SkullNosePoints = { 0f, -0.01f, 0.09f, -0.19f, -0.09f, -0.19f };
        private static readonly float[] SunRayPoints = { 0.56f, 0.13f, 0.98f, 0f, 0.56f, -0.13f };
        private static readonly float[] TridentTipPoints =
            { 0f, 1.00f, 0.15f, 0.66f, 0.065f, 0.66f, 0.065f, 0.40f, -0.065f, 0.40f, -0.065f, 0.66f, -0.15f, 0.66f };
        private static readonly float[] TridentBarbPoints = { 0.46f, 0.98f, 0.60f, 0.66f, 0.32f, 0.66f };
        private static readonly float[] CrownPoints =
        {
            -0.70f, -0.50f, -0.86f, 0.46f, -0.40f, 0.02f, 0f, 0.70f, 0.40f, 0.02f, 0.86f, 0.46f, 0.70f, -0.50f,
        };
        private static readonly float[] ArrowHeadPoints = { 0f, 1.00f, 0.62f, 0.20f, -0.62f, 0.20f };
        private static readonly float[] ArrowFletchPoints = { 0.04f, -0.46f, 0.40f, -0.72f, 0.40f, -0.98f, 0.04f, -0.74f };
        private static readonly float[] AnchorFlukePoints = { 0.38f, -0.16f, 0.86f, -0.16f, 0.62f, 0.20f };

        private static float Charge(int charge, float x, float y)
        {
            float ax = Math.Abs(x), ay = Math.Abs(y);
            switch (charge)
            {
                case 0: return Star(x, y, 0.98f, 0.44f);
                case 1: return Poly(BoltPoints, x, y);
                case 2:
                {
                    float hub = Len(x, y);
                    return Math.Min(Poly(WingPoints, ax, y), Math.Max(hub - 0.33f, 0.035f - Math.Abs(hub - 0.20f)));
                }
                case 3: return Math.Min(Poly(CrossBarPoints, x, y), Poly(CrossBarPoints, y, x));
                case 4: return Math.Min(Chevron(ax, y, 0.78f), Chevron(ax, y, 0.14f));
                case 5: return Poly(DartPoints, x, y);
                case 6:
                    return Math.Min(Math.Min(Poly(BladePoints, x, y), Box(x, y + 0.36f, 0.48f, 0.07f)),
                        Math.Min(Box(x, y + 0.62f, 0.07f, 0.22f), Len(x, y + 0.88f) - 0.12f));
                case 7:
                    return Math.Min(Math.Min(Len(ax - 0.34f, y + 0.10f) - 0.40f, Poly(SpadeHeadPoints, x, y)),
                        Poly(SpadeStemPoints, x, y));
                case 8:
                {
                    y += 0.07f;
                    float bone = Math.Min(Len(x, y - 0.22f) - 0.62f, RoundBox(x, y + 0.44f, 0.36f, 0.26f, 0.10f));
                    float holes = Math.Min(Math.Min(Len(ax - 0.25f, y - 0.14f) - 0.17f, Poly(SkullNosePoints, x, y)),
                        Math.Min(Box(x, y + 0.60f, 0.024f, 0.12f), Box(ax - 0.17f, y + 0.60f, 0.024f, 0.12f)));
                    return Math.Max(bone, -holes);
                }
                case 9: return Math.Max(Len(x + 0.06f, y) - 0.88f, -(Len(x - 0.26f, y - 0.10f) - 0.72f));
                case 10:
                {
                    const float sector = (float)(Math.PI / 4.0);
                    float angle = (float)Math.Atan2(y, x) + (float)Math.PI;
                    float local = angle - sector * (float)Math.Floor(angle / sector) - sector * 0.5f;
                    float radius = Len(x, y);
                    return Math.Min(radius - 0.44f,
                        Poly(SunRayPoints, radius * (float)Math.Cos(local), radius * (float)Math.Sin(local)));
                }
                case 11:
                {
                    float fork = Math.Max(Math.Abs(Len(x, y - 0.36f) - 0.46f) - 0.065f, y - 0.42f);
                    float tines = Math.Min(Box(ax - 0.46f, y - 0.50f, 0.065f, 0.20f), Poly(TridentBarbPoints, ax, y));
                    return Math.Min(Math.Min(Box(x, y + 0.25f, 0.065f, 0.75f), Poly(TridentTipPoints, x, y)),
                        Math.Min(fork, tines));
                }
                case 12:
                    return Math.Min(Math.Min(Poly(CrownPoints, x, y), Box(x, y + 0.56f, 0.70f, 0.13f)),
                        Math.Min(Len(ax - 0.86f, y - 0.54f) - 0.10f, Len(x, y - 0.80f) - 0.11f));
                case 13:
                    return Math.Min(Math.Min(Poly(ArrowHeadPoints, x, y), Box(x, y + 0.25f, 0.09f, 0.55f)),
                        Poly(ArrowFletchPoints, ax, y));
                case 14:
                    return Math.Min(Math.Min(Math.Abs(Len(x, y) - 0.68f) - 0.06f, Len(x, y) - 0.10f),
                        Math.Min(Box(x, ay - 0.72f, 0.055f, 0.26f), Box(ax - 0.72f, y, 0.26f, 0.055f)));
                default:
                {
                    float arms = Math.Max(Math.Abs(Len(x, y + 0.18f) - 0.60f) - 0.065f, y + 0.12f);
                    float ring = Math.Abs(Len(x, y - 0.84f) - 0.15f) - 0.035f;
                    return Math.Min(Math.Min(Box(x, y + 0.04f, 0.065f, 0.76f), Box(x, y - 0.48f, 0.36f, 0.055f)),
                        Math.Min(Math.Min(arms, ring), Poly(AnchorFlukePoints, ax, y)));
                }
            }
        }

        /// <summary>One upward chevron whose peak sits at <paramref name="apex"/>; <paramref name="ax"/> is |x|.</summary>
        private static float Chevron(float ax, float y, float apex)
        {
            const float slope = 0.70f, thick = 0.34f;
            float along = y - (apex - slope * ax);
            return Math.Max((Math.Abs(along + thick * 0.5f) - thick * 0.5f) * 0.819f, ax - 0.88f);
        }

        // ---- Accents and the squadron number ------------------------------------------------------

        private const int Leaves = 6;
        private static readonly float[] LeafCos = new float[Leaves], LeafSin = new float[Leaves], LeafAxisCos = new float[Leaves], LeafAxisSin = new float[Leaves];

        static EmblemRaster()
        {
            for (int k = 0; k < Leaves; k++)
            {
                double at = (-64.0 + 24.0 * k) * Math.PI / 180.0, axis = at + (90.0 - 24.0) * Math.PI / 180.0;
                LeafCos[k] = (float)Math.Cos(at); LeafSin[k] = (float)Math.Sin(at);
                LeafAxisCos[k] = (float)Math.Cos(axis); LeafAxisSin[k] = (float)Math.Sin(axis);
            }
        }

        private static float Accent(EmblemDesign design, Layout layout, float x, float y, float cy, float scale, float edge)
        {
            float ax = Math.Abs(x);
            switch (design.Accent)
            {
                case EmblemDesign.AccentRing:
                    return Math.Abs(edge + 0.175f) - 0.020f;
                case EmblemDesign.AccentStars:
                    return Math.Min(Star(x, y - layout.StarY, 0.095f, 0.44f),
                        Star(ax - layout.StarSpread, y - layout.StarY + 0.06f, 0.095f, 0.44f));
                case EmblemDesign.AccentBars:
                    if (design.Number == 0)
                        return Math.Min(Box(x, y - layout.BaseY - 0.05f, 0.27f, 0.027f), Box(x, y - layout.BaseY + 0.05f, 0.17f, 0.027f));
                    float reach = (design.Number >= 10 ? DigitWidth + DigitGap * 0.5f : DigitWidth * 0.5f) + 0.15f;
                    return Box(ax - reach, y - layout.BaseY, 0.075f, 0.027f);
                case EmblemDesign.AccentLaurel:
                {
                    float radius = scale * 1.30f, length = scale * 0.25f, width = scale * 0.085f, best = Far;
                    for (int k = design.Number > 0 ? 1 : 0; k < Leaves; k++)
                    {
                        float lx = ax - radius * LeafCos[k], ly = y - cy - radius * LeafSin[k];
                        float u = lx * LeafAxisCos[k] + ly * LeafAxisSin[k], v = -lx * LeafAxisSin[k] + ly * LeafAxisCos[k];
                        best = Math.Min(best, (Len(u / length, v / width) - 1f) * width);
                    }
                    return best;
                }
                default: return Far;
            }
        }

        // Seven-segment numerals drawn as round-capped strokes: a stencil that stays whole at any size.
        private const float DigitWidth = 0.125f, DigitHeight = 0.270f, DigitGap = 0.080f, DigitStroke = 0.026f;
        private static readonly byte[] Segments = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

        /// <summary>The number centred on the origin: one or two digits.</summary>
        private static float Digits(int number, float x, float y)
        {
            if (Math.Abs(x) > 0.40f || Math.Abs(y) > 0.24f) return Far;
            if (number < 10) return Digit(number, x, y);
            float shift = (DigitWidth + DigitGap) * 0.5f;
            return Math.Min(Digit(number / 10, x + shift, y), Digit(number % 10, x - shift, y));
        }

        private static float Digit(int digit, float x, float y)
        {
            float w = DigitWidth * 0.5f, h = DigitHeight * 0.5f, best = Far;
            int lit = Segments[digit];
            if ((lit & 0x01) != 0) best = Math.Min(best, Stroke(x, y, -w, h, w, h));
            if ((lit & 0x02) != 0) best = Math.Min(best, Stroke(x, y, w, h, w, 0f));
            if ((lit & 0x04) != 0) best = Math.Min(best, Stroke(x, y, w, 0f, w, -h));
            if ((lit & 0x08) != 0) best = Math.Min(best, Stroke(x, y, -w, -h, w, -h));
            if ((lit & 0x10) != 0) best = Math.Min(best, Stroke(x, y, -w, 0f, -w, -h));
            if ((lit & 0x20) != 0) best = Math.Min(best, Stroke(x, y, -w, h, -w, 0f));
            if ((lit & 0x40) != 0) best = Math.Min(best, Stroke(x, y, -w, 0f, w, 0f));
            return best - DigitStroke;
        }

        // ---- Distance primitives ------------------------------------------------------------------

        private static float Len(float x, float y) => (float)Math.Sqrt(x * x + y * y);

        private static float Box(float x, float y, float halfX, float halfY)
        {
            float dx = Math.Abs(x) - halfX, dy = Math.Abs(y) - halfY;
            return Len(Math.Max(dx, 0f), Math.Max(dy, 0f)) + Math.Min(Math.Max(dx, dy), 0f);
        }

        private static float RoundBox(float x, float y, float halfX, float halfY, float radius) =>
            Box(x, y, halfX - radius, halfY - radius) - radius;

        private static float Stroke(float x, float y, float ax, float ay, float bx, float by)
        {
            float ex = bx - ax, ey = by - ay, wx = x - ax, wy = y - ay;
            float t = Clamp01((wx * ex + wy * ey) / (ex * ex + ey * ey));
            return Len(wx - ex * t, wy - ey * t);
        }

        /// <summary>Exact distance to a closed polygon given as x,y pairs (either winding).</summary>
        private static float Poly(float[] points, float x, float y)
        {
            int count = points.Length / 2;
            float wx0 = x - points[0], wy0 = y - points[1];
            float best = wx0 * wx0 + wy0 * wy0, sign = 1f;
            for (int i = 0, j = count - 1; i < count; j = i, i++)
            {
                float ix = points[i * 2], iy = points[i * 2 + 1], jx = points[j * 2], jy = points[j * 2 + 1];
                float ex = jx - ix, ey = jy - iy, wx = x - ix, wy = y - iy;
                float t = Clamp01((wx * ex + wy * ey) / (ex * ex + ey * ey));
                float bx = wx - ex * t, by = wy - ey * t;
                best = Math.Min(best, bx * bx + by * by);
                bool above = y >= iy, below = y < jy, left = ex * wy > ey * wx;
                if ((above && below && left) || (!above && !below && !left)) sign = -sign;
            }
            return sign * (float)Math.Sqrt(best);
        }

        /// <summary>Five-pointed star of outer radius <paramref name="radius"/>; <paramref name="inner"/> is the notch as a share of it.</summary>
        private static float Star(float x, float y, float radius, float inner)
        {
            const float kx = 0.809016994f, ky = -0.587785252f;
            x = Math.Abs(x);
            float fold = Math.Max(kx * x + ky * y, 0f);
            x -= 2f * fold * kx; y -= 2f * fold * ky;
            fold = Math.Max(-kx * x + ky * y, 0f);
            x += 2f * fold * kx; y -= 2f * fold * ky;
            x = Math.Abs(x);
            y -= radius;
            float bx = inner * -ky, by = inner * kx - 1f;
            float h = Math.Min(Math.Max((x * bx + y * by) / (bx * bx + by * by), 0f), radius);
            return Len(x - bx * h, y - by * h) * Math.Sign(y * bx - x * by);
        }

        /// <summary>Completes a half outline (axis point, down the right side, axis point) with its mirror image.</summary>
        private static float[] Mirror(float[] half)
        {
            int count = half.Length / 2;
            var full = new float[(count * 2 - 2) * 2];
            Array.Copy(half, full, half.Length);
            for (int i = count - 2, o = half.Length; i >= 1; i--, o += 2)
            {
                full[o] = -half[i * 2];
                full[o + 1] = half[i * 2 + 1];
            }
            return full;
        }

        private static float Cover(float distance, float pixel) => Clamp01(0.5f - distance / pixel);

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        private static void Mix(ref float r, ref float g, ref float b, Rgba paint, float amount)
        {
            if (amount <= 0f) return;
            r += (paint.R - r) * amount;
            g += (paint.G - g) * amount;
            b += (paint.B - b) * amount;
        }

        private static byte Byte(float value) => (byte)(Clamp01(value) * 255f + 0.5f);
    }
}
