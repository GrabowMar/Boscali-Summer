using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The class silhouettes of the stores side (the prototype's <c>takeover/stores.js</c> SHAPES): one line-art outline per <see cref="StoreClass"/> in a unit space
    /// of u along the weapon (-1 tail, 1 nose) and v across it (+-1 = half height), drawn small on the station rail and, for a store the game has no picture of, in HERO's art box.
    /// A DETAIL line (barrel groove, seam) is left out at rail size, a RIB (the pod's tubes) is not.
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

        /// <summary>
        /// The silhouette of <paramref name="cls"/> centred on (<paramref name="cx"/>, <paramref name="cy"/>), <paramref name="len"/> long and +-<paramref name="half"/> high: its outline
        /// in <paramref name="tone"/> (dashed with <paramref name="dashed"/>: a spent store), <paramref name="mini"/> without the detail lines (the rail's).
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
    }
}
