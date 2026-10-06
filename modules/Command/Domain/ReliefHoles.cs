using System.Collections.Generic;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The game's terrain tiles are cut out under city ground meshes, so a baked heightfield
    /// reads those cut-outs as sea-level water: pits with sheer walls in the tilted relief.
    /// A water region that never reaches the sheet edge and whose shore stands clear of the
    /// sea is such a cut-out; it is filled smoothly from its rim. Real lakes and lagoons keep
    /// a shore at sea level and stay water.
    /// </summary>
    internal static class ReliefHoles
    {
        private const int Relaxations = 256;

        /// <summary>Fills raised enclosed holes in place; returns the number of cells filled.</summary>
        internal static int FillRaised(float[] heights, bool[] land, int side, float seaLevel,
            float minimumRimRise)
        {
            if (heights == null || land == null || side < 3 ||
                heights.Length != side * side || land.Length != heights.Length) return 0;
            var visited = new bool[heights.Length];
            var region = new List<int>(1024);
            var pending = new Stack<int>(1024);
            var rim = new List<float>(1024);
            int filled = 0;
            for (int start = 0; start < heights.Length; start++)
            {
                if (land[start] || visited[start]) continue;
                region.Clear();
                rim.Clear();
                bool open = false;
                visited[start] = true;
                pending.Push(start);
                while (pending.Count > 0)
                {
                    int at = pending.Pop();
                    region.Add(at);
                    int x = at % side, z = at / side;
                    if (x == 0 || z == 0 || x == side - 1 || z == side - 1) open = true;
                    Visit(x > 0 ? at - 1 : -1);
                    Visit(x < side - 1 ? at + 1 : -1);
                    Visit(z > 0 ? at - side : -1);
                    Visit(z < side - 1 ? at + side : -1);
                }
                if (open || rim.Count == 0) continue;
                rim.Sort();
                float shore = rim[rim.Count / 2];
                if (shore - seaLevel < minimumRimRise) continue;
                foreach (int at in region)
                {
                    heights[at] = shore;
                    land[at] = true;
                }
                // Gauss-Seidel relaxation towards the rim: a smooth floor, never a new peak.
                for (int pass = 0; pass < Relaxations; pass++)
                foreach (int at in region)
                    heights[at] = (heights[at - 1] + heights[at + 1] +
                        heights[at - side] + heights[at + side]) * .25f;
                filled += region.Count;
            }
            return filled;

            void Visit(int next)
            {
                if (next < 0) return;
                if (land[next])
                {
                    rim.Add(heights[next]);
                    return;
                }
                if (visited[next]) return;
                visited[next] = true;
                pending.Push(next);
            }
        }
    }
}
