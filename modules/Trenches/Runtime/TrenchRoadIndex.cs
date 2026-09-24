using System;
using System.Collections.Generic;
using BoscaliSummer.Core;
using RoadPathfinding;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Cached distance-to-road query over the game's road network, so the ditch sidesteps
    /// the surface and the anti-tank team watches crossings. Road polylines are densified
    /// to 16m samples in a 32m cell index (same shape as the fire module's forest index,
    /// over game data this module reads itself); the index rebuilds when the network
    /// changes and stays empty where there is no road network.
    /// </summary>
    internal static class TrenchRoadIndex
    {
        private const float CellSize = 32f;
        private const float SampleStep = 16f;
        private const int MaximumRoads = 2048;
        private const int MaximumSamples = 8192;
        private const int MaximumRings = 10; // 320m of watch distance, then the query stops

        private struct CellSpan
        {
            public int Offset;
            public int Count;
        }

        private static readonly Dictionary<long, CellSpan> occupied = new Dictionary<long, CellSpan>();
        private static Vector2[] samples = Array.Empty<Vector2>();
        private static RoadNetwork source;
        private static int sourceRoads;
        private static int sourcePoints;

        public static bool Ready { get; private set; }

        public static void ResetForScene()
        {
            occupied.Clear();
            samples = Array.Empty<Vector2>();
            source = null;
            sourceRoads = sourcePoints = 0;
            Ready = false;
        }

        /// <summary>Rebuilds the index when the network is new or changed; fails closed.</summary>
        public static bool EnsureBuilt()
        {
            RoadNetwork network = NetworkSceneSingleton<LevelInfo>.i?.roadNetwork;
            if (network?.roads == null || network.roads.Count == 0 ||
                network.roads.Count > MaximumRoads) return false;
            int points = 0;
            for (int r = 0; r < network.roads.Count && points <= MaximumSamples; r++)
                points += network.roads[r]?.points?.Count ?? 0;
            if (Ready && ReferenceEquals(source, network) && sourceRoads == network.roads.Count &&
                sourcePoints == points) return true;
            Rebuild(network);
            return Ready;
        }

        /// <summary>
        /// Distance to the nearest road sample within <paramref name="maxDistance"/>: false
        /// where the index is empty or no road reaches that far. Ring-capped, so a 300m
        /// watch query never scans the whole theater.
        /// </summary>
        public static bool TryDistance(float x, float z, float maxDistance, out float distance)
        {
            distance = float.NaN;
            if (!Ready || !(maxDistance > 0f)) return false;
            int rings = Math.Min(MaximumRings, (int)Math.Ceiling(maxDistance / CellSize));
            int cx = (int)Math.Floor(x / CellSize), cz = (int)Math.Floor(z / CellSize);
            float bestSq = maxDistance * maxDistance;
            bool found = false;
            for (int gx = cx - rings; gx <= cx + rings; gx++)
            for (int gz = cz - rings; gz <= cz + rings; gz++)
            {
                long key = ((long)gx << 32) ^ (uint)gz;
                if (!occupied.TryGetValue(key, out CellSpan span)) continue;
                int end = span.Offset + span.Count;
                for (int i = span.Offset; i < end; i++)
                {
                    float dx = samples[i].x - x, dz = samples[i].y - z;
                    float sq = dx * dx + dz * dz;
                    if (sq >= bestSq) continue;
                    bestSq = sq;
                    found = true;
                }
            }
            if (!found) return false;
            distance = (float)Math.Sqrt(bestSq);
            return true;
        }

        private static void Rebuild(RoadNetwork network)
        {
            occupied.Clear();
            var points = new List<Vector2>(1024);
            int roads = Math.Min(network.roads.Count, MaximumRoads);
            for (int r = 0; r < roads && points.Count < MaximumSamples; r++)
            {
                Road road = network.roads[r];
                if (road?.points == null || road.points.Count < 2) continue;
                for (int i = 1; i < road.points.Count && points.Count < MaximumSamples; i++)
                {
                    GlobalPosition a = road.points[i - 1], b = road.points[i];
                    float dx = b.x - a.x, dz = b.z - a.z;
                    float length = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (!(length > 0.01f)) continue;
                    int steps = Math.Min((int)Math.Ceiling(length / SampleStep),
                        MaximumSamples - points.Count);
                    for (int s = 0; s < steps && points.Count < MaximumSamples; s++)
                    {
                        float k = (float)s / steps;
                        points.Add(new Vector2(a.x + dx * k, a.z + dz * k));
                    }
                }
            }
            samples = points.ToArray();
            long[] keys = new long[samples.Length];
            for (int i = 0; i < samples.Length; i++)
                keys[i] = Deterministic.CellKey(samples[i].x, samples[i].y, CellSize);
            Array.Sort(keys, samples, 0, samples.Length);
            int spanStart = 0;
            while (spanStart < samples.Length)
            {
                long current = keys[spanStart];
                int spanEnd = spanStart + 1;
                while (spanEnd < samples.Length && keys[spanEnd] == current) spanEnd++;
                occupied[current] = new CellSpan { Offset = spanStart, Count = spanEnd - spanStart };
                spanStart = spanEnd;
            }
            source = network;
            sourceRoads = network.roads.Count;
            sourcePoints = 0;
            for (int r = 0; r < network.roads.Count && sourcePoints <= MaximumSamples; r++)
                sourcePoints += network.roads[r]?.points?.Count ?? 0;
            Ready = samples.Length > 0;
        }
    }
}
