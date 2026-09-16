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
            field.Grid.Clear();
            compatibility.ReconcileMissionNodes(field.Grid, hq);
            var units = UnitRegistry.allUnits;
            for (int i = 0; units != null && i < Math.Min(units.Count, 4096); i++)
            {
                if (MissionMapCompatibilityEngine.TryGetGroundObservation(units[i], hq,
                    out Vector3 p, out float weight, out bool hostile))
                    field.Grid.AddTroopPresence(p.x, p.z, weight, hostile);
            }
            // No accelerated capture from a long gap without observations.
            field.Grid.EvaluateSectors(field.Updated < 0 ? 0 : Math.Min(0.5f, Math.Max(0, now - field.Updated)));
            field.Updated = now;
            return field.Grid;
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

        public bool OwnsPosition(int factionId, float x, float z)
        {
            TacticalSectorGrid grid = ReadFaction(factionId);
            return grid != null && grid.WorldToCell(x, z, out int c, out int r) &&
                grid.GetSectorControl(c, r) == SectorControl.Friendly;
        }

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
