using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>Temporary Terrain holes under the rift. Mesh maps enter above impact height. Only edited cells are saved,
    /// and restored before resize or close; no terrain asset is written to disk.</summary>
    internal sealed class HellPortalGround
    {
        private sealed class Cut
        {
            public TerrainData Data;
            public int X, Z;
            public bool[,] Before;
        }
        private readonly List<Cut> cuts = new List<Cut>();
        public void Open(Vector3 center, float radius)
        {
            Close();
            try
            {
                foreach (Terrain terrain in Terrain.activeTerrains)
                {
                    TerrainData data = terrain.terrainData;
                    if (data == null || data.holesResolution == 0) continue;
                    Vector3 local = center - terrain.transform.position;
                    // A return rift high in the pocket dimension must never cut the world below it.
                    if (Mathf.Abs(terrain.SampleHeight(center) + terrain.transform.position.y - center.y) > 40f) continue;
                    float r = radius * .78f;
                    int resolution = data.holesResolution;
                    int x = Mathf.Clamp(Mathf.FloorToInt((local.x - r) / data.size.x * resolution), 0, resolution);
                    int z = Mathf.Clamp(Mathf.FloorToInt((local.z - r) / data.size.z * resolution), 0, resolution);
                    int maxX = Mathf.Clamp(Mathf.CeilToInt((local.x + r) / data.size.x * resolution), 0, resolution);
                    int maxZ = Mathf.Clamp(Mathf.CeilToInt((local.z + r) / data.size.z * resolution), 0, resolution);
                    int width = maxX - x, height = maxZ - z;
                    if (width <= 0 || height <= 0) continue;
                    bool[,] before = data.GetHoles(x, z, width, height);
                    var after = (bool[,])before.Clone();
                    for (int row = 0; row < height; row++)
                        for (int col = 0; col < width; col++)
                        {
                            float dx = (x + col + .5f) * data.size.x / resolution - local.x;
                            float dz = (z + row + .5f) * data.size.z / resolution - local.z;
                            if (dx * dx + dz * dz < r * r) after[row, col] = false;
                        }
                    cuts.Add(new Cut { Data = data, X = x, Z = z, Before = before });
                    data.SetHoles(x, z, after);
                }
            }
            catch (System.Exception e) { Plugin.Logger.LogWarning("[Spooktober] terrain rift: " + e.Message); }
        }
        public void Close()
        {
            foreach (Cut cut in cuts)
                if (cut.Data != null)
                    try { cut.Data.SetHoles(cut.X, cut.Z, cut.Before); }
                    catch (System.Exception e) { Plugin.Logger.LogWarning("[Spooktober] restore terrain rift: " + e.Message); }
            cuts.Clear();
        }
    }
}
