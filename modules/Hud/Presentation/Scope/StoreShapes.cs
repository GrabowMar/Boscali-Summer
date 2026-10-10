using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The class silhouettes of the STORES HEADER (the prototype's <c>takeover/stores.js</c> SHAPES): one line-art outline per <see cref="StoreClass"/> in a unit space
    /// of u along the weapon (-1 tail, 1 nose) and v across it (+-1 = half height), drawn big in the weapon cell (filled to the share of rounds left) and small on the
    /// station rail's pips. A part is closed (an outline that can take a fill) or open; a DETAIL line (barrel groove, seam) is left out at pip size, a RIB (the pod's tubes) is not.
    /// </summary>
    internal static class StoreShapes
    {
        private enum Kind : byte { Body, Detail, Rib }

        private readonly struct Part
        {
            public readonly float[] Pts;
            public readonly bool Closed;
            public readonly Kind Kind;
            public Part(Kind kind, bool closed, params float[] pts) { Kind = kind; Closed = closed; Pts = pts; }
            public int N => Pts.Length / 2;
        }

        private static Part B(params float[] p) => new Part(Kind.Body, true, p);
        private static Part F(params float[] p) => new Part(Kind.Body, false, p);      // a fin or a line that closes nothing
        private static Part D(params float[] p) => new Part(Kind.Detail, false, p);
        private static Part R(float u) => new Part(Kind.Rib, false, u, -0.8f, u, 0.8f);

        private static readonly Part[][] Shapes =
        {
            // GUN: breech, receiver, barrel cluster and muzzle with its grooves
            new[]
            {
                B(-1f, -0.55f, -0.9f, -0.78f, -0.58f, -0.78f, -0.58f, 0.78f, -0.9f, 0.78f, -1f, 0.55f), B(-0.58f, -0.52f, -0.3f, -0.52f, -0.3f, 0.52f, -0.58f, 0.52f),
                B(-0.3f, -0.42f, 0.62f, -0.42f, 0.62f, 0.42f, -0.3f, 0.42f), B(0.62f, -0.28f, 1f, -0.28f, 1f, 0.28f, 0.62f, 0.28f),
                D(-0.3f, -0.2f, 0.62f, -0.2f), D(-0.3f, 0f, 0.62f, 0f), D(-0.3f, 0.2f, 0.62f, 0.2f), D(0.05f, -0.42f, 0.05f, 0.42f), D(0.38f, -0.42f, 0.38f, 0.42f),
                D(-0.8f, -0.78f, -0.8f, 0.78f), D(0.8f, -0.28f, 0.8f, 0.28f),
            },
            // AAM: slim body, tail fins, small mid fins
            new[]
            {
                B(-1f, -0.2f, 0.45f, -0.2f, 1f, 0f, 0.45f, 0.2f, -1f, 0.2f), B(-1f, 0.2f, -0.78f, 0.2f, -1f, 0.9f), B(-1f, -0.2f, -0.78f, -0.2f, -1f, -0.9f),
                B(0.12f, 0.2f, 0.32f, 0.2f, 0.16f, 0.58f), B(0.12f, -0.2f, 0.32f, -0.2f, 0.16f, -0.58f), D(0.62f, -0.16f, 0.62f, 0.16f), D(-0.45f, -0.2f, -0.45f, 0.2f),
            },
            // AGM: mid fins wider and longer than the AAM's (a diamond) so the two read apart at pip size
            new[]
            {
                B(-1f, -0.28f, 0.5f, -0.28f, 1f, -0.1f, 1f, 0.1f, 0.5f, 0.28f, -1f, 0.28f), B(-0.3f, 0.28f, 0.26f, 0.28f, -0.02f, 1f), B(-0.3f, -0.28f, 0.26f, -0.28f, -0.02f, -1f),
                B(-1f, 0.28f, -0.8f, 0.28f, -1f, 0.65f), B(-1f, -0.28f, -0.8f, -0.28f, -1f, -0.65f), D(0.72f, -0.2f, 0.72f, 0.2f), D(-0.55f, -0.28f, -0.55f, 0.28f),
            },
            // BOMB: fat body, tail ring, strakes
            new[]
            {
                B(-0.7f, -0.42f, 0.3f, -0.46f, 0.78f, -0.28f, 1f, 0f, 0.78f, 0.28f, 0.3f, 0.46f, -0.7f, 0.42f), B(-0.7f, -0.42f, -0.9f, -0.26f, -0.9f, 0.26f, -0.7f, 0.42f),
                B(-1f, -0.95f, -0.8f, -0.95f, -0.8f, 0.95f, -1f, 0.95f), B(0.5f, 0.46f, 0.66f, 0.46f, 0.58f, 0.8f), B(0.5f, -0.46f, 0.66f, -0.46f, 0.58f, -0.8f), D(0.1f, -0.46f, 0.1f, 0.46f),
            },
            // POD: blunt tapered nose and three tube lines that survive pip size
            new[]
            {
                B(-1f, -0.8f, 0.45f, -0.8f, 1f, -0.28f, 1f, 0.28f, 0.45f, 0.8f, -1f, 0.8f), D(-1f, 0f, 0.45f, 0f), R(-0.62f), R(-0.24f), R(0.14f),
            },
            // RAIL: capacitor bank and the twin rails
            new[]
            {
                B(-1f, -0.85f, -0.68f, -0.85f, -0.68f, 0.85f, -1f, 0.85f), B(-0.68f, -0.8f, 1f, -0.8f, 1f, -0.3f, -0.68f, -0.3f), B(-0.68f, 0.3f, 1f, 0.3f, 1f, 0.8f, -0.68f, 0.8f),
                D(-0.4f, -0.3f, -0.4f, 0.3f), D(-0.1f, -0.3f, -0.1f, 0.3f), D(0.2f, -0.3f, 0.2f, 0.3f), D(0.5f, -0.3f, 0.5f, 0.3f), D(0.8f, -0.3f, 0.8f, 0.3f),
            },
            // KIT: a pod's cross-section drawn as a winged shape
            new[]
            {
                B(1f, 0f, 0.5f, -0.1f, -0.3f, -0.9f, -0.62f, -0.86f, -0.42f, -0.12f, -1f, -0.12f, -1f, 0.12f, -0.42f, 0.12f, -0.62f, 0.86f, -0.3f, 0.9f, 0.5f, 0.1f),
                D(0.1f, 0f, -0.5f, 0f),
            },
        };

        /// <summary>The weapon cell's silhouette size: length and half height, by class.</summary>
        public static void CellSize(StoreClass c, out float length, out float half)
        {
            switch (c)
            {
                case StoreClass.Gun: length = 136f; half = 14f; break;
                case StoreClass.Aam: length = 136f; half = 15f; break;
                case StoreClass.Agm: length = 116f; half = 15f; break;
                case StoreClass.Bomb: length = 104f; half = 15f; break;
                case StoreClass.Pod: length = 94f; half = 14f; break;
                case StoreClass.Rail: length = 136f; half = 14f; break;
                default: length = 90f; half = 15f; break;
            }
        }

        /// <summary>
        /// The silhouette of <paramref name="cls"/> centred on (<paramref name="cx"/>, <paramref name="cy"/>), <paramref name="len"/> long and +-<paramref name="half"/> high: its outline
        /// in <paramref name="tone"/> (dashed with <paramref name="dashed"/>: an empty store), <paramref name="mini"/> without the detail lines (a pip).
        /// </summary>
        public static void Draw(ScopeCanvas g, StoreClass cls, float cx, float cy, float len, float half, ScopeTone tone, float alpha = 1f, bool mini = false, bool dashed = false)
        {
            Part[] parts = Shapes[(int)cls];
            float hx = len / 2f;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Kind == Kind.Detail && mini) continue;
                int n = g.LoadXY(parts[i].Pts, parts[i].N, cx, cy, hx, half);
                g.Path(n, tone, alpha, 1f, parts[i].Closed, dashed ? 2f : 0f, dashed ? 2f : 0f);
            }
        }

        /// <summary>The silhouette's solid parts filled in <paramref name="tone"/> at <paramref name="alpha"/>, only left of the board x of <paramref name="xCut"/> (a share of the weapon: the rounds left).</summary>
        public static void Fill(ScopeCanvas g, StoreClass cls, float cx, float cy, float len, float half, ScopeTone tone, float alpha, float xCut, float[] scratch)
        {
            Part[] parts = Shapes[(int)cls];
            float hx = len / 2f;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!parts[i].Closed) continue;
                int n = parts[i].N;
                for (int k = 0; k < n; k++)
                {
                    scratch[2 * k] = Mathf.Round(cx + parts[i].Pts[2 * k] * hx - 0.5f) + 0.5f;
                    scratch[2 * k + 1] = Mathf.Round(cy + parts[i].Pts[2 * k + 1] * half - 0.5f) + 0.5f;
                }
                n = ClipLeft(scratch, n, xCut);
                if (n < 3) continue;
                g.FillPoly(g.Poly(scratch, n), tone, alpha);
            }
        }

        /// <summary>Sutherland-Hodgman against the half plane x &lt;= <paramref name="xCut"/>; the polygon is x, y pairs in <paramref name="p"/> and the clipped one replaces it (twice the points at most).</summary>
        private static int ClipLeft(float[] p, int n, float xCut)
        {
            float[] o = Clip;
            int m = 0;
            for (int i = 0; i < n && m < o.Length / 2 - 2; i++)
            {
                int j = (i + 1) % n;
                float ax = p[2 * i], ay = p[2 * i + 1], bx = p[2 * j], by = p[2 * j + 1];
                bool ain = ax <= xCut, bin = bx <= xCut;
                if (ain) { o[2 * m] = ax; o[2 * m + 1] = ay; m++; }
                if (ain != bin)
                {
                    float k = (xCut - ax) / (bx - ax);
                    o[2 * m] = xCut; o[2 * m + 1] = ay + (by - ay) * k; m++;
                }
            }
            for (int i = 0; i < 2 * m; i++) p[i] = o[i];
            return m;
        }

        private static readonly float[] Clip = new float[64];
    }
}
