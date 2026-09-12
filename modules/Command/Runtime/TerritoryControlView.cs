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
        private int resolution;
        internal void Configure(MissionMapCompatibilityEngine adapter, int cells)
        { compatibility = adapter; resolution = cells; }

        internal TacticalSectorGrid Read(FactionHQ hq)
        {
            var map = NetworkSceneSingleton<LevelInfo>.i?.LoadedMapSettings;
            if (hq == null || map == null || FactionRegistry.airbaseLookup == null) return null;
            if (!fields.TryGetValue(hq, out Field field))
            {
                if (fields.Count >= 8) return null;
                field = new Field { Grid = new TacticalSectorGrid(resolution, map.MapSize.x, map.MapSize.y) };
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
        public void ResetForScene() => fields.Clear();
        private void OnDestroy() => ResetForScene();
    }
}
