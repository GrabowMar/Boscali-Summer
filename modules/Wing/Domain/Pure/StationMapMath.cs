using System;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>LOADOUT's station map (spec 2026-10-04 §SUPPLY × LOADOUT): where each station's box sits on a generic top view. A
    /// station with two or more pylons is a wing pair (one box a side, mirrored); the others sit on the centreline from nose to tail.
    /// Offsets are pixels from the map's centre, x to the right, y down (nose at the top).</summary>
    internal static class StationMapMath
    {
        /// <summary>One box: the station it stands for and where.</summary>
        public struct Spot
        {
            public int Station;
            public float X, Y;
        }

        /// <summary>The boxes for <paramref name="pylons"/> (pylon count per station). A pair makes two boxes (right first, then its
        /// mirror), so the result is at most <c>2 × pylons.Length</c> long. Boxes closer than <paramref name="minDist"/> are pushed
        /// aft until clear, within the map's half height.</summary>
        public static int Spots(int[] pylons, float halfW, float halfH, float minDist, Spot[] into)
        {
            if (pylons == null || into == null) return 0;
            int wings = 0, centre = 0;
            foreach (int p in pylons)
                if (p >= 2) wings++;
                else centre++;
            int n = 0, k = 0, j = 0;
            for (int s = 0; s < pylons.Length && n < into.Length; s++)
            {
                if (pylons[s] >= 2)
                {
                    float t = wings == 1 ? 0.5f : k / (float)(wings - 1);
                    k++;
                    float x = halfW * (0.30f + 0.62f * t), y = halfH * (0.02f + 0.26f * t);
                    y = Clear(into, n, x, y, halfH, minDist);
                    if (n + 1 >= into.Length) break;
                    into[n++] = new Spot { Station = s, X = x, Y = y };
                    into[n++] = new Spot { Station = s, X = -x, Y = y };
                }
                else
                {
                    float t = centre == 1 ? 0.5f : j / (float)(centre - 1);
                    j++;
                    float y = Clear(into, n, 0f, halfH * (-0.68f + 1.5f * t), halfH, minDist);
                    into[n++] = new Spot { Station = s, X = 0f, Y = y };
                }
            }
            return n;
        }

        private static float Clear(Spot[] placed, int count, float x, float y, float halfH, float minDist)
        {
            for (int guard = 0; guard < 64; guard++)
            {
                bool clash = false;
                for (int i = 0; i < count && !clash; i++)
                {
                    float dx = placed[i].X - x, dy = placed[i].Y - y;
                    clash = dx * dx + dy * dy < minDist * minDist;
                }
                if (!clash || y + minDist * 0.5f > halfH) return Math.Min(y, halfH);
                y += minDist * 0.5f;
            }
            return Math.Min(y, halfH);
        }
    }
}
