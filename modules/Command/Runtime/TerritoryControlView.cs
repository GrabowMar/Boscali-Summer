using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Runtime
{
    internal sealed class TerritoryControlView : MonoBehaviour, ISceneService, ITerritoryIngress
    {
        private sealed class Field
        {
            internal TacticalSectorGrid Grid;
            internal float Updated = -1;
            internal ulong Snapshot;
            internal bool Evaluated;
            internal readonly List<Observation> Observations = new List<Observation>(4096);
        }

        private struct Observation
        {
            public float X;
            public float Z;
            public float Weight;
            public bool Hostile;
        }

        private readonly Dictionary<FactionHQ, Field> fields = new Dictionary<FactionHQ, Field>();
        private MissionMapCompatibilityEngine compatibility;
        private float cellSize;
        internal void Configure(MissionMapCompatibilityEngine adapter, float cellSizeMetres)
        { compatibility = adapter; cellSize = cellSizeMetres; }

        internal TacticalSectorGrid Read(FactionHQ hq)
        {
            var map = NetworkSceneSingleton<LevelInfo>.i?.LoadedMapSettings;
            if (hq == null || map == null || FactionRegistry.airbaseLookup == null) return null;
            if (!fields.TryGetValue(hq, out Field field))
            {
                if (fields.Count >= 8) return null;
                // Aligned through the map's own grid offset: one cell is one base-grid square.
                field = new Field { Grid = new TacticalSectorGrid(cellSize, map.MapSize.x, map.MapSize.y, map.OffsetX, map.OffsetY) };
                fields.Add(hq, field);
            }
            float now = Time.timeSinceLevelLoad;
            if (field.Updated >= 0 && now - field.Updated < 0.5f) return field.Grid;

            // Snapshot pass first: nodes plus cell-quantized ground observations. When
            // neither moved, the previous field still describes the front, so the whole
            // evaluation — 16k-cell smoothing, pocket sweeps, contour, cluster tree and
            // the texture bake — is skipped. Quantizing to the grid cell is deliberate:
            // a vehicle shifting inside its kilometre square cannot change the front.
            compatibility.ReconcileMissionNodes(field.Grid, hq);
            ulong snapshot = field.Grid.ComputeNodeHash();
            field.Observations.Clear();
            var units = UnitRegistry.allUnits;
            int count = units != null ? Math.Min(units.Count, 4096) : 0;
            for (int i = 0; i < count; i++)
            {
                if (!MissionMapCompatibilityEngine.TryGetGroundObservation(units[i], hq,
                    out Vector3 p, out float weight, out bool hostile)) continue;
                field.Observations.Add(new Observation { X = p.x, Z = p.z, Weight = weight, Hostile = hostile });
                snapshot = HashObservation(snapshot, p.x, p.z, hostile, field.Grid.CellSize);
            }

            float elapsed = field.Evaluated ? Math.Min(0.5f, Math.Max(0f, now - field.Updated)) : 0f;
            field.Updated = now;
            if (field.Evaluated && snapshot == field.Snapshot) return field.Grid;

            field.Snapshot = snapshot;
            field.Grid.Clear();
            compatibility.ReconcileMissionNodes(field.Grid, hq);
            for (int i = 0; i < field.Observations.Count; i++)
            {
                Observation observation = field.Observations[i];
                field.Grid.AddTroopPresence(observation.X, observation.Z, observation.Weight, observation.Hostile);
            }
            field.Grid.EvaluateSectors(elapsed);
            field.Evaluated = true;
            return field.Grid;
        }

        private static ulong HashObservation(ulong hash, float x, float z, bool hostile, float gridCellSize)
        {
            float cell = gridCellSize > 0f ? gridCellSize : TacticalSectorGrid.DefaultCellSize;
            long cellX = (long)Math.Floor(x / cell);
            long cellZ = (long)Math.Floor(z / cell);
            hash = (hash ^ (uint)cellX) * 1099511628211UL;
            hash = (hash ^ (uint)cellZ) * 1099511628211UL;
            hash = (hash ^ (hostile ? 1u : 0u)) * 1099511628211UL;
            return hash;
        }

        public bool TryNearestEdge(int factionId, float playerX, float playerZ, out float x, out float z)
        {
            x = z = 0;
            int checkedHqs = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (++checkedHqs > 8) break;
                if (hq == null || hq.GetInstanceID() != factionId) continue;
                TacticalSectorGrid grid = Read(hq);
                return grid != null && grid.TryNearestControlledEdge(playerX, playerZ, out x, out z);
            }
            return false;
        }
        private TacticalSectorGrid ReadFaction(int factionId)
        {
            int count = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (++count > 8) break;
                if (hq != null && hq.GetInstanceID() == factionId) return Read(hq);
            }
            return null;
        }

        public int CopyFrontlineTraces(int factionId, FrontlineTracePoint[] points, int[] lengths, float[] pressure)
            => ReadFaction(factionId)?.CopyFrontlineTraces(points, lengths, pressure) ?? 0;

        public bool TryGetHoldStrength(int factionId, float x, float z, out float hold)
        {
            hold = 0f;
            TacticalSectorGrid grid = ReadFaction(factionId);
            if (grid == null || !grid.WorldToCell(x, z, out int c, out int r)) return false;
            hold = grid.GetSectorHoldStrength(c, r);
            return !float.IsNaN(hold) && !float.IsInfinity(hold);
        }

        public void ResetForScene() => fields.Clear();
        private void OnDestroy() => ResetForScene();
    }
}
