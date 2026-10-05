using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>
    /// Greedy label declutter shared by the C2 maps: the nearest free spot (inside the map) to the wanted one, tried in a fixed order (nearest offset first, ties by the
    /// order below), so two map labels never share pixels. When nothing is free the wanted spot is kept (the map is simply full). Allocation-free after the first call.
    /// </summary>
    internal static class MapDeclutter
    {
        private static readonly Vector2[] Offsets = Build();

        private static Vector2[] Build()
        {
            var list = new List<Vector2>(160);
            for (int ix = -6; ix <= 6; ix++)
                for (int iy = -4; iy <= 4; iy++) list.Add(new Vector2(ix * 38f, iy * 20f));
            list.Sort((a, b) =>
            {
                int c = a.sqrMagnitude.CompareTo(b.sqrMagnitude);
                if (c != 0) return c;
                c = Mathf.Abs(a.x).CompareTo(Mathf.Abs(b.x));
                return c != 0 ? c : a.y.CompareTo(b.y);
            });
            return list.ToArray();
        }

        /// <summary>Returns the top-left of a free <paramref name="w"/> x <paramref name="h"/> box near (x, y) and records it in <paramref name="placed"/>.</summary>
        public static Vector2 Free(List<Rect> placed, float x, float y, float w, float h, float mapW, float mapH)
        {
            Vector2 first = new Vector2(Mathf.Clamp(x, 0f, Mathf.Max(0f, mapW - w)), Mathf.Clamp(y, 0f, Mathf.Max(0f, mapH - h)));
            for (int i = 0; i < Offsets.Length; i++)
            {
                float nx = Mathf.Clamp(x + Offsets[i].x, 0f, Mathf.Max(0f, mapW - w)), ny = Mathf.Clamp(y + Offsets[i].y, 0f, Mathf.Max(0f, mapH - h));
                var r = new Rect(nx - 1f, ny - 1f, w + 2f, h + 2f);
                bool hit = false;
                for (int k = 0; k < placed.Count && !hit; k++) hit = placed[k].Overlaps(r);
                if (hit) continue;
                placed.Add(r);
                return new Vector2(nx, ny);
            }
            placed.Add(new Rect(first.x, first.y, w, h));
            return first;
        }
    }
}
