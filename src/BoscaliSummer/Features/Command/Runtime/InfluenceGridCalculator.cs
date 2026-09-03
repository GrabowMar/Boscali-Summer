using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Runtime
{
    internal sealed class InfluenceGridCalculator
    {
        public readonly int Resolution;
        public readonly float WorldSize;
        private readonly float[] friendlyGrid;
        private readonly float[] hostileGrid;
        private readonly float[] radarGrid;
        private readonly float[] reconGrid;
        private readonly Color32[] pixels;

        public InfluenceGridCalculator(int resolution = 64, float worldSize = 81920f)
        {
            Resolution = Mathf.Clamp(resolution, 32, 128);
            WorldSize = worldSize > 0f ? worldSize : 81920f;
            int total = Resolution * Resolution;
            friendlyGrid = new float[total];
            hostileGrid = new float[total];
            radarGrid = new float[total];
            reconGrid = new float[total];
            pixels = new Color32[total];
        }

        public struct InfluenceSource
        {
            public float X;
            public float Z;
            public float Radius;
            public float Weight;
            public bool IsHostile;

            public InfluenceSource(float x, float z, float radius, float weight, bool isHostile)
            {
                X = x;
                Z = z;
                Radius = Mathf.Max(radius, 100f);
                Weight = weight;
                IsHostile = isHostile;
            }
        }

        public struct RadarSource
        {
            public float X;
            public float Z;
            public float Range;
            public bool IsHostile;
            public bool IsTracking;

            public RadarSource(float x, float z, float range, bool isHostile, bool isTracking)
            {
                X = x;
                Z = z;
                Range = Mathf.Max(range, 500f);
                IsHostile = isHostile;
                IsTracking = isTracking;
            }
        }

        public void Clear()
        {
            Array.Clear(friendlyGrid, 0, friendlyGrid.Length);
            Array.Clear(hostileGrid, 0, hostileGrid.Length);
            Array.Clear(radarGrid, 0, radarGrid.Length);
            Array.Clear(reconGrid, 0, reconGrid.Length);
        }

        public void AddInfluence(IList<InfluenceSource> sources)
        {
            if (sources == null) return;
            float halfWorld = WorldSize * 0.5f;
            float cellSize = WorldSize / Resolution;

            for (int s = 0; s < sources.Count; s++)
            {
                InfluenceSource src = sources[s];
                // Map world coords [-halfWorld, halfWorld] to cell indices [0, Resolution - 1]
                int centerCol = Mathf.FloorToInt((src.X + halfWorld) / cellSize);
                int centerRow = Mathf.FloorToInt((src.Z + halfWorld) / cellSize);
                int cellRadius = Mathf.CeilToInt(src.Radius / cellSize);

                int minCol = Mathf.Max(0, centerCol - cellRadius);
                int maxCol = Mathf.Min(Resolution - 1, centerCol + cellRadius);
                int minRow = Mathf.Max(0, centerRow - cellRadius);
                int maxRow = Mathf.Min(Resolution - 1, centerRow + cellRadius);

                float radiusSq = src.Radius * src.Radius;
                float[] targetGrid = src.IsHostile ? hostileGrid : friendlyGrid;

                for (int r = minRow; r <= maxRow; r++)
                {
                    float cellZ = (r + 0.5f) * cellSize - halfWorld;
                    float dz = cellZ - src.Z;
                    float dzSq = dz * dz;

                    int rowOffset = r * Resolution;
                    for (int c = minCol; c <= maxCol; c++)
                    {
                        float cellX = (c + 0.5f) * cellSize - halfWorld;
                        float dx = cellX - src.X;
                        float distSq = dx * dx + dzSq;

                        if (distSq < radiusSq)
                        {
                            float falloff = 1f - Mathf.Sqrt(distSq) / src.Radius;
                            targetGrid[rowOffset + c] += src.Weight * falloff;
                        }
                    }
                }
            }
        }

        public void AddRadars(IList<RadarSource> radars)
        {
            if (radars == null) return;
            float halfWorld = WorldSize * 0.5f;
            float cellSize = WorldSize / Resolution;

            for (int s = 0; s < radars.Count; s++)
            {
                RadarSource rad = radars[s];
                int centerCol = Mathf.FloorToInt((rad.X + halfWorld) / cellSize);
                int centerRow = Mathf.FloorToInt((rad.Z + halfWorld) / cellSize);
                int cellRadius = Mathf.CeilToInt(rad.Range / cellSize);

                int minCol = Mathf.Max(0, centerCol - cellRadius);
                int maxCol = Mathf.Min(Resolution - 1, centerCol + cellRadius);
                int minRow = Mathf.Max(0, centerRow - cellRadius);
                int maxRow = Mathf.Min(Resolution - 1, centerRow + cellRadius);

                float rangeSq = rad.Range * rad.Range;
                for (int r = minRow; r <= maxRow; r++)
                {
                    float cellZ = (r + 0.5f) * cellSize - halfWorld;
                    float dz = cellZ - rad.Z;
                    float dzSq = dz * dz;

                    int rowOffset = r * Resolution;
                    for (int c = minCol; c <= maxCol; c++)
                    {
                        float cellX = (c + 0.5f) * cellSize - halfWorld;
                        float dx = cellX - rad.X;
                        float distSq = dx * dx + dzSq;

                        if (distSq < rangeSq)
                        {
                            float ring = Mathf.Sqrt(distSq) / rad.Range;
                            // Encode friendly radar as positive, hostile radar as negative
                            float val = (rad.IsHostile ? -1f : 1f) * (0.3f + 0.7f * ring);
                            radarGrid[rowOffset + c] = val;
                        }
                    }
                }
            }
        }

        public Color32[] BakeTexture(
            bool showFrontlines, bool showRadar, bool showRecon, float globalAlpha = 0.45f)
        {
            int total = Resolution * Resolution;
            byte alphaByte = (byte)Mathf.Clamp(Mathf.RoundToInt(globalAlpha * 255f), 0, 255);

            for (int i = 0; i < total; i++)
            {
                byte r = 0, g = 0, b = 0, a = 0;

                // 1. Frontlines & Area of Control
                if (showFrontlines)
                {
                    float f = friendlyGrid[i];
                    float h = hostileGrid[i];
                    float diff = f - h;
                    float sum = f + h;

                    if (sum > 0.05f)
                    {
                        // Frontline is where friendly and hostile influence intersect closely
                        float balance = Mathf.Abs(diff) / sum;
                        if (balance < 0.25f && sum > 0.3f)
                        {
                            // Contested frontline ribbon: vibrant gold/amber hazard line
                            r = 255; g = 200; b = 40;
                            a = (byte)Mathf.Clamp(Mathf.RoundToInt(alphaByte * 1.5f), 0, 255);
                        }
                        else if (diff > 0f)
                        {
                            // Friendly territory: Tactical Blue / Cyan tint
                            float intensity = Mathf.Clamp01(diff * 0.5f);
                            r = (byte)(15 * intensity);
                            g = (byte)(110 * intensity);
                            b = (byte)(235 * intensity);
                            a = (byte)(alphaByte * (0.35f + 0.65f * intensity));
                        }
                        else
                        {
                            // Hostile territory: Tactical Red tint
                            float intensity = Mathf.Clamp01(-diff * 0.5f);
                            r = (byte)(230 * intensity);
                            g = (byte)(35 * intensity);
                            b = (byte)(35 * intensity);
                            a = (byte)(alphaByte * (0.35f + 0.65f * intensity));
                        }
                    }
                }

                // 2. Radar & SAM Envelopes (Additive / Overlaid)
                if (showRadar)
                {
                    float rad = radarGrid[i];
                    if (rad > 0.05f)
                    {
                        // Friendly Radar: Soft emerald/cyan ring
                        b = (byte)Mathf.Max(b, 180);
                        g = (byte)Mathf.Max(g, 220);
                        a = (byte)Mathf.Max(a, (byte)(alphaByte * 0.7f));
                    }
                    else if (rad < -0.05f)
                    {
                        // Hostile SAM threat bubble: Intense warning red/amber ring
                        r = (byte)Mathf.Max(r, 240);
                        g = (byte)Mathf.Max(g, 60);
                        a = (byte)Mathf.Max(a, (byte)(alphaByte * 0.85f));
                    }
                }

                pixels[i] = new Color32(r, g, b, a);
            }

            return pixels;
        }
    }
}
