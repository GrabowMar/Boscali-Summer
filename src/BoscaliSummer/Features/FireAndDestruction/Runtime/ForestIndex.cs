using System;
using System.Collections;
using System.Collections.Generic;
using BoscaliSummer.Core;
using NuclearOption.Effects;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    internal sealed class ForestIndex
    {
        private readonly Dictionary<long, List<Vector2>> occupied = new Dictionary<long, List<Vector2>>();
        private float cellSize;

        public bool Ready { get; private set; }
        public int PositionCount { get; private set; }

        public IEnumerator Rebuild(float requestedCellSize)
        {
            Ready = false;
            PositionCount = 0;
            occupied.Clear();
            cellSize = Mathf.Max(8f, requestedCellSize);

            TreeRenderer[] renderers = Resources.FindObjectsOfTypeAll<TreeRenderer>();
            int sinceYield = 0;
            for (int r = 0; r < renderers.Length; r++)
            {
                TreeRenderer renderer = renderers[r];
                if (renderer == null || renderer.PositionData == null || !renderer.gameObject.scene.IsValid()) continue;
                byte[] data = renderer.PositionData.bytes;
                for (int offset = 0; offset + 11 < data.Length; offset += 12)
                {
                    float x = BitConverter.ToSingle(data, offset);
                    float z = BitConverter.ToSingle(data, offset + 8);
                    long key = Deterministic.CellKey(x, z, cellSize);
                    if (!occupied.TryGetValue(key, out List<Vector2> points))
                    {
                        points = new List<Vector2>(16);
                        occupied.Add(key, points);
                    }
                    points.Add(new Vector2(x, z));
                    PositionCount++;
                    if (++sinceYield >= 200000)
                    {
                        sinceYield = 0;
                        yield return null;
                    }
                }
            }

            Ready = true;
            Plugin.Logger.LogInfo($"Forest index ready: {PositionCount} procedural tree positions in {occupied.Count} cells.");
        }

        public bool Contains(GlobalPosition position)
        {
            if (!Ready) return false;
            const float hitRadius = 18f;
            const float hitRadiusSq = hitRadius * hitRadius;
            int cx = Mathf.FloorToInt(position.x / cellSize);
            int cz = Mathf.FloorToInt(position.z / cellSize);
            for (int x = cx - 1; x <= cx + 1; x++)
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                long key = ((long)x << 32) ^ (uint)z;
                if (!occupied.TryGetValue(key, out List<Vector2> points)) continue;
                for (int i = 0; i < points.Count; i++)
                {
                    float dx = points[i].x - position.x;
                    float dz = points[i].y - position.z;
                    if (dx * dx + dz * dz <= hitRadiusSq) return true;
                }
            }
            return false;
        }

    }
}
