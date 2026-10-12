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
        private const float RimKey = 0.036f, RimInk = 0.102f, RimInner = 0.132f;
        private const float Keyline = 0.030f;
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
            new Layout(0.10f, 0.55f, 0.54f, 0.40f, -0.50f),  // SHIELD
            new Layout(0.00f, 0.60f, 0.58f, 0.32f, -0.60f),  // ROUNDEL
            new Layout(0.26f, 0.41f, 0.56f, 0.46f, -0.32f),  // DELTA
            new Layout(0.18f, 0.49f, 0.64f, 0.32f, -0.30f),  // BANNER
            new Layout(0.00f, 0.58f, 0.56f, 0.34f, -0.58f),  // HEX
            new Layout(0.00f, 0.45f, 0.46f, 0.17f, -0.50f),  // DIAMOND
            new Layout(0.00f, 0.60f, 0.60f, 0.40f, -0.62f),  // PATCH
            new Layout(-0.08f, 0.54f, 0.48f, 0.26f, -0.66f), // ARCH
            new Layout(0.06f, 0.45f, 0.40f, 0.36f, -0.40f),  // ARROWHEAD
            new Layout(0.00f, 0.50f, 0.47f, 0.27f, -0.48f),  // SAWBLADE
            new Layout(0.12f, 0.43f, 0.66f, 0.20f, -0.56f),  // COFFIN
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

            // A dark mark is its own outline; keying it would fill the detail cut into it.
            bool outlined = EmblemDesign.Luminance(EmblemDesign.Paint(design.Ink, EmblemFinish.Colour)) >= 0.25f;
            // Traced nose art is three tones. A light ink is the skin and its shade is darker; a dark ink is the
            // shade itself (hair, cloth, metal) and the skin is lifted out of it.
            bool toned = EmblemMarks.Toned(design.Charge);
            // Its own line work does the outlining, so the keyline round it is fine and the drawing a little larger.
            float outline = toned ? Keyline * 0.4f : Keyline;
            if (toned) scale *= 1.12f;
            Rgba shade = outlined
                ? new Rgba(ink.R * 0.58f + key.R * 0.42f, ink.G * 0.58f + key.G * 0.42f, ink.B * 0.58f + key.B * 0.42f)
                : ink;
            Rgba skin = new Rgba(ink.R * 0.45f + 0.50f, ink.G * 0.45f + 0.50f, ink.B * 0.45f + 0.50f);
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
                    float mark = Far, figure = Far, shadeEdge = Far, lineEdge = Far;
                    float qx = x / scale, qy = (y - cy) / scale;
                    if (qx > -1.2f && qx < 1.2f && qy > -1.2f && qy < 1.2f)
                    {
                        mark = EmblemMarks.Distance(design.Charge, qx, qy) * scale;
                        if (toned && mark < px)
                        {
                            figure = mark;
                            EmblemMarks.Tones(design.Charge, qx, qy, out shadeEdge, out lineEdge);
                            shadeEdge = Math.Max(shadeEdge * scale, mark); lineEdge = Math.Max(lineEdge * scale, mark);
                        }
                    }
                    mark = Math.Min(mark, Accent(design, layout, x, y, cy, scale, edge));
                    if (numbered) mark = Math.Min(mark, Digits(design.Number, x, y - baseY));
                    if (outlined) Mix(ref r, ref g, ref b, key, Cover(mark - outline, px));
                    Mix(ref r, ref g, ref b, ink, Cover(mark, px));
                    if (toned)
                    {
                        if (!outlined) Mix(ref r, ref g, ref b, skin, Cover(figure, px));
                        Mix(ref r, ref g, ref b, shade, Cover(shadeEdge, px));
                        Mix(ref r, ref g, ref b, key, Cover(lineEdge, px));
                    }

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

        // Corners are cut, not rounded: a badge is stencilled on metal, not embroidered.
        private const float Corner = 0.012f;
        private static readonly float[] ShieldPoints = Mirror(new[]
            { 0f, 0.92f, 0.42f, 0.80f, 0.84f, 0.92f, 0.80f, 0.06f, 0.44f, -0.58f, 0f, -0.94f });
        private static readonly float[] DeltaPoints = { -0.93f, 0.82f, 0.93f, 0.82f, 0f, -0.93f };
        private static readonly float[] BannerPoints = { -0.69f, 0.91f, 0.69f, 0.91f, 0.69f, -0.91f, 0f, -0.52f, -0.69f, -0.91f };
        private static readonly float[] HexPoints =
            { 0f, 0.93f, 0.805f, 0.465f, 0.805f, -0.465f, 0f, -0.93f, -0.805f, -0.465f, -0.805f, 0.465f };
        private static readonly float[] DiamondPoints = { 0f, 0.95f, 0.83f, 0f, 0f, -0.95f, -0.83f, 0f };
        private static readonly float[] PatchPoints = Mirror(new[]
            { 0f, 0.88f, 0.64f, 0.88f, 0.88f, 0.64f, 0.88f, -0.64f, 0.64f, -0.88f, 0f, -0.88f });
        private static readonly float[] ArrowheadPoints = Mirror(new[]
            { 0f, 0.66f, 0.86f, 0.93f, 0.70f, 0.10f, 0f, -0.95f });
        private static readonly float[] CoffinPoints = Mirror(new[]
            { 0f, 0.93f, 0.44f, 0.93f, 0.72f, 0.42f, 0.42f, -0.93f, 0f, -0.93f });

        private static float Shape(int shape, float x, float y)
        {
            switch (shape)
            {
                case 1: return Len(x, y) - 0.93f;
                case 2: return Poly(DeltaPoints, x, y) - Corner;
                case 3: return Poly(BannerPoints, x, y) - Corner;
                case 4: return Poly(HexPoints, x, y) - Corner;
                case 5: return Poly(DiamondPoints, x, y) - Corner;
                case 6: return Poly(PatchPoints, x, y) - Corner;
                // A bullet: straight flanks into a pointed arch.
                case 7: return Math.Max(y > -0.20f ? Len(Math.Abs(x) + 0.355f, y + 0.20f) - 1.175f : Math.Abs(x) - 0.82f, -0.90f - y);
                case 8: return Poly(ArrowheadPoints, x, y) - Corner;
                case 9:
                {
                    // Twelve raked teeth; the distance is approximate, which a one-pixel edge never shows.
                    const float tooth = (float)(Math.PI / 6.0);
                    float turn = ((float)Math.Atan2(y, x) + (float)Math.PI) / tooth, disc = Len(x, y) - 0.77f;
                    // Inside the disc the rim runs round; the teeth are all keyline, standing on it.
                    if (disc < 0f) return disc - RimKey * 0.5f;
                    return Math.Max((disc - 0.20f * (turn - (float)Math.Floor(turn))) * 0.85f, -RimKey * 0.5f);
                }
                case 10: return Poly(CoffinPoints, x, y) - Corner;
                default: return Poly(ShieldPoints, x, y) - Corner;
            }
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
                    return Math.Abs(edge + 0.195f) - 0.022f;
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
