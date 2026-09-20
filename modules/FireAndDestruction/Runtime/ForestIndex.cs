using System;
using System.Collections;
using System.Collections.Generic;
using BoscaliSummer.Core;
#if !NET8_0_OR_GREATER
using NuclearOption.Effects;
using UnityEngine;
#endif

namespace BoscaliSummer.Fire
{
#if NET8_0_OR_GREATER
    internal struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }
#endif

    internal sealed class ForestIndex
    {
        internal struct CellSpan
        {
            public int Offset;
            public int Count;
        }

        private readonly Dictionary<long, CellSpan> occupied = new Dictionary<long, CellSpan>();
        private Vector2[] points = Array.Empty<Vector2>();
        private float cellSize;

        public bool Ready { get; private set; }
        public int PositionCount { get; private set; }

#if !NET8_0_OR_GREATER
        public IEnumerator Rebuild(float requestedCellSize)
        {
            Ready = false;
            PositionCount = 0;
            occupied.Clear();
            points = Array.Empty<Vector2>();
            cellSize = Math.Max(8f, requestedCellSize);

            TreeRenderer[] renderers = Resources.FindObjectsOfTypeAll<TreeRenderer>();
            var validData = new List<byte[]>();
            int totalCount = 0;
            for (int r = 0; r < renderers.Length; r++)
            {
                TreeRenderer renderer = renderers[r];
                if (renderer == null || renderer.PositionData == null || !renderer.gameObject.scene.IsValid()) continue;
                byte[] data = renderer.PositionData.bytes;
                if (data == null || data.Length < 12) continue;
                validData.Add(data);
                totalCount += data.Length / 12;
            }

            if (totalCount == 0)
            {
                Ready = true;
                yield break;
            }

            points = new Vector2[totalCount];
            long[] keys = new long[totalCount];
            int writeIdx = 0;
            int sinceYield = 0;

            for (int d = 0; d < validData.Count; d++)
            {
                byte[] data = validData[d];
                for (int offset = 0; offset + 11 < data.Length; offset += 12)
                {
                    float x = BitConverter.ToSingle(data, offset);
                    float z = BitConverter.ToSingle(data, offset + 8);
                    long key = Deterministic.CellKey(x, z, cellSize);
                    keys[writeIdx] = key;
                    points[writeIdx] = new Vector2(x, z);
                    writeIdx++;

                    if (++sinceYield >= 200000)
                    {
                        sinceYield = 0;
                        yield return null;
                    }
                }
            }

            validData = null;
            yield return null;

            IndexSortedKeys(keys, writeIdx);
            keys = null;
            Plugin.Logger.LogInfo($"Forest index ready: {PositionCount} procedural tree positions in {occupied.Count} cells.");
        }
#endif

        internal void BuildFromPoints(IReadOnlyList<Vector2> pointList, float requestedCellSize)
        {
            Ready = false;
            PositionCount = 0;
            occupied.Clear();
            points = Array.Empty<Vector2>();
            cellSize = Math.Max(8f, requestedCellSize);

            if (pointList == null || pointList.Count == 0)
            {
                Ready = true;
                return;
            }

            int totalCount = pointList.Count;
            points = new Vector2[totalCount];
            long[] keys = new long[totalCount];

            for (int i = 0; i < totalCount; i++)
            {
                Vector2 pt = pointList[i];
                keys[i] = Deterministic.CellKey(pt.x, pt.y, cellSize);
                points[i] = pt;
            }

            IndexSortedKeys(keys, totalCount);
        }

        private void IndexSortedKeys(long[] keys, int count)
        {
            occupied.Clear();
            if (count == 0)
            {
                PositionCount = 0;
                Ready = true;
                return;
            }

            Array.Sort(keys, points, 0, count);

            int spanStart = 0;
            while (spanStart < count)
            {
                long currentKey = keys[spanStart];
                int spanEnd = spanStart + 1;
                while (spanEnd < count && keys[spanEnd] == currentKey)
                {
                    spanEnd++;
                }
                occupied[currentKey] = new CellSpan { Offset = spanStart, Count = spanEnd - spanStart };
                spanStart = spanEnd;
            }

            PositionCount = count;
            Ready = true;
        }

        public bool Contains(float px, float pz)
        {
            if (!Ready) return false;
            const float hitRadius = 18f;
            const float hitRadiusSq = hitRadius * hitRadius;
            int cx = (int)Math.Floor(px / cellSize);
            int cz = (int)Math.Floor(pz / cellSize);
            for (int x = cx - 1; x <= cx + 1; x++)
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                long key = ((long)x << 32) ^ (uint)z;
                if (!occupied.TryGetValue(key, out CellSpan span)) continue;
                int end = span.Offset + span.Count;
                for (int i = span.Offset; i < end; i++)
                {
                    float dx = points[i].x - px;
                    float dz = points[i].y - pz;
                    if (dx * dx + dz * dz <= hitRadiusSq) return true;
                }
            }
            return false;
        }

        public bool Contains(Vector2 position) => Contains(position.x, position.y);

#if !NET8_0_OR_GREATER
        public bool Contains(GlobalPosition position) => Contains(position.x, position.z);
#endif
    }
}
