namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>A rectangle in screen pixels, X and Y the lower-left corner with Y up (no UnityEngine, so it links into the tests).</summary>
    internal struct RectF
    {
        public float X, Y, W, H;

        public RectF(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }

        /// <summary>True when the two rectangles share area; rectangles that only touch along an edge do not overlap.</summary>
        public bool Overlaps(in RectF o) => X < o.X + o.W && o.X < X + W && Y < o.Y + o.H && o.Y < Y + H;

        /// <summary>True when o lies entirely inside this rectangle (edges may touch).</summary>
        public bool Contains(in RectF o) => o.X >= X && o.Y >= Y && o.X + o.W <= X + W && o.Y + o.H <= Y + H;
    }

    /// <summary>First-fit placement of tag plates around their anchors. A plate never covers the icon square of any anchor.</summary>
    internal static class StrTagPlacer
    {
        /// <summary>Half the native airbase hit square (MfdTerrainRelief uses 34 px) plus a 3 px margin: the near corner of a plate is this far from its anchor on both axes.</summary>
        public const float IconClear = 20f;
        /// <summary>Four diagonals at twelve distances, tried in order: north-east, north-west, south-east, south-west, nearest ring first. A dense cluster of wide plates needs
        /// the outer rings: eight places were all taken there and the plate was left on top of another (the pre-repair placer took the overlap).</summary>
        public const int Candidates = 48;

        private const float FarRing = 14f;

        /// <summary>Candidate rectangle index 0..7 for a plate of size w by h around an anchor.</summary>
        public static RectF Candidate(int index, float ax, float ay, float w, float h)
        {
            float d = IconClear + (index >> 2) * FarRing;
            float x = (index & 1) == 0 ? ax + d : ax - d - w;
            float y = (index & 2) == 0 ? ay + d : ay - d - h;
            return new RectF(x, y, w, h);
        }

        /// <summary>
        /// Places n plates; placed[i] is where plate i goes. A candidate is taken when it lies inside the viewport, meets no plate
        /// placed before it and meets the icon square of no anchor (its own or another's). With no free candidate the first
        /// diagonal is used, pulled into the viewport, and the overlap is accepted (documented ceiling). A plate with a
        /// non-finite anchor or size gets an empty rectangle and no tag should be drawn for it.
        /// </summary>
        public static void Place(in RectF viewport, float[] ax, float[] ay, float[] w, float[] h, int n, RectF[] placed)
        {
            if (ax == null || ay == null || w == null || h == null || placed == null) return;
            n = System.Math.Min(n, System.Math.Min(System.Math.Min(ax.Length, ay.Length), System.Math.Min(System.Math.Min(w.Length, h.Length), placed.Length)));

            for (int i = 0; i < n; i++)
            {
                if (!float.IsFinite(ax[i]) || !float.IsFinite(ay[i]) || !float.IsFinite(w[i]) || !float.IsFinite(h[i])) { placed[i] = default; continue; }

                bool done = false;
                for (int c = 0; c < Candidates && !done; c++)
                {
                    RectF r = Candidate(c, ax[i], ay[i], w[i], h[i]);
                    if (!viewport.Contains(r) || HitsAnIcon(r, ax, ay, n) || HitsAPlate(r, placed, i)) continue;
                    placed[i] = r;
                    done = true;
                }
                if (!done) placed[i] = Pulled(viewport, Candidate(0, ax[i], ay[i], w[i], h[i]));
            }
        }

        private static bool HitsAnIcon(in RectF r, float[] ax, float[] ay, int n)
        {
            for (int j = 0; j < n; j++)
            {
                var icon = new RectF(ax[j] - IconClear, ay[j] - IconClear, 2f * IconClear, 2f * IconClear);
                if (r.Overlaps(icon)) return true;   // a NaN anchor makes every comparison false, so it blocks nothing
            }
            return false;
        }

        private static bool HitsAPlate(in RectF r, RectF[] placed, int count)
        {
            for (int j = 0; j < count; j++)
                if (r.Overlaps(placed[j])) return true;
            return false;
        }

        private static RectF Pulled(in RectF vp, RectF r)
        {
            r.X = System.Math.Max(vp.X, System.Math.Min(r.X, vp.X + vp.W - r.W));
            r.Y = System.Math.Max(vp.Y, System.Math.Min(r.Y, vp.Y + vp.H - r.H));
            return r;
        }
    }
}
