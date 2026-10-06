using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Runtime
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

        // One unit scan shared by every faction's field: each Read used to walk the
        // whole registry itself, so eight factions paid eight scans per window. Entries
        // keep the owner's instance id, never a live reference.
        private struct SharedObservation
        {
            public float X;
            public float Z;
            public float Weight;
            public int OwnerId;
        }

        private const int SharedScanCap = 4096;
        private static readonly SharedObservation[] sharedScan = new SharedObservation[SharedScanCap];
        private static int sharedScanCount;
        private static float sharedScanStamp = -1f;

        private static void ScanShared(float now)
        {
            if (sharedScanStamp >= 0f && now - sharedScanStamp < 0.5f) return;
            sharedScanStamp = now;
            sharedScanCount = 0;
            List<Unit> units = UnitRegistry.allUnits;
            int count = units != null ? Math.Min(units.Count, SharedScanCap) : 0;
            for (int i = 0; i < count; i++)
            {
                if (!MissionMapCompatibilityEngine.TryGetGroundObservation(units[i], out Vector3 p,
                    out float weight, out FactionHQ owner) || owner == null) continue;
                sharedScan[sharedScanCount++] = new SharedObservation
                    { X = p.x, Z = p.z, Weight = weight, OwnerId = owner.GetInstanceID() };
            }
        }

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
            ScanShared(now);
            int hqId = hq.GetInstanceID();
            for (int i = 0; i < sharedScanCount; i++)
            {
                SharedObservation shared = sharedScan[i];
                bool hostile = shared.OwnerId != hqId;
                field.Observations.Add(new Observation { X = shared.X, Z = shared.Z, Weight = shared.Weight, Hostile = hostile });
                snapshot = HashObservation(snapshot, shared.X, shared.Z, hostile, field.Grid.CellSize);
            }

            // COREControl elapsed policy: a 5 s cap at host cadence, and an unchanged
            // snapshot preserves its interval instead of consuming it, re-evaluating at
            // 1 Hz while the field is still converging.
            bool snapshotChanged = !field.Evaluated || snapshot != field.Snapshot;
            if (!ControlFieldCore.TryElapsed(now, field.Updated, field.Evaluated, snapshotChanged,
                out float elapsed, out float consumed))
                return field.Grid;
            field.Updated = consumed;

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

        public void ResetForScene()
        {
            fields.Clear();
            sharedScanStamp = -1f;
        }
        private void OnDestroy() => ResetForScene();
    }
}
