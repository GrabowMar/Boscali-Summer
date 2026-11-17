#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>Native networked buildings straddling the old/new blast edges; artificial ages test the supplement.</summary>
    public static class Sa74DestructionAutomation
    {
        private static Building near, distant, outside;
        private static bool prepared;
        private static GlobalPosition origin;
        private static float distantDistance, outsideDistance;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                !GameAccess.IsServer() || GameManager.gameState != GameState.SinglePlayer)
                return Failure("Sa74DestructionAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            string action = Text(args, "action") ?? "status";
            if (action == "fixture") Prepare();
            if (action == "detonate")
            {
                if (!prepared) Prepare();
                Sa74Fallout.Detonate(origin.ToLocalPosition(), null);
            }
            if (action == "age")
                foreach (Sa74Fallout.Site site in Sa74Fallout.Sites)
                    site.Started = Time.time - Mathf.Clamp(Number(args, "seconds", 300f), 0f, 600f);
            if (action == "verifyForest") Sa74Destruction.RequestVerification();
            Dictionary<string, object> result = Sa74Destruction.Snapshot();
            result["fixtureReady"] = prepared;
            result["nearCollapsed"] = prepared && (near == null || near.disabled);
            result["distantCollapsed"] = prepared && (distant == null || distant.disabled);
            result["outsideStanding"] = prepared && outside != null && !outside.disabled;
            result["distantDistance"] = distantDistance;
            result["outsideDistance"] = outsideDistance;
            result["shockRadius"] = Sa74Fallout.ShockRadius;
            result["frontRadius"] = Sa74Fallout.Sites.Count > 0 ? Sa74Fallout.FrontRadius(Time.time - Sa74Fallout.Sites[0].Started) : 0f;
            return result;
        }

        private static void Prepare()
        {
            if (prepared) throw new InvalidOperationException("SA-74N native destruction fixture already prepared");
            BuildingDefinition definition = GameAccess.EncyclopediaOrNull?.buildings
                .Where(d => d != null && d.unitPrefab != null && d.unitPrefab.GetComponent<Building>() != null &&
                    d.unitPrefab.GetComponent<Hangar>() == null && d.unitPrefab.GetComponent<Factory>() == null)
                .OrderBy(d => d.jsonKey, StringComparer.Ordinal).FirstOrDefault();
            if (definition == null) throw new InvalidOperationException("No native building prefab for destruction fixture");
            Airbase field = UnityEngine.Object.FindObjectsOfType<Airbase>()
                .Where(a => a.center != null && a.center.position.y > Datum.LocalSeaY + 5f)
                .OrderByDescending(a => a.center.position.y).FirstOrDefault();
            Vector3 at = field != null ? field.center.position : new Vector3(-20000f, Datum.LocalSeaY + 10f, -20000f);
            origin = at.ToGlobalPosition();
            distantDistance = Mathf.Min(65000f, Sa74Fallout.ShockRadius - 5000f);
            outsideDistance = Sa74Fallout.ShockRadius + 5000f;
            near = Spawn(definition, "SA74_DestructionNear", 1000f);
            distant = Spawn(definition, "SA74_DestructionDistant", distantDistance);
            outside = Spawn(definition, "SA74_DestructionOutside", outsideDistance);
            if (near == null || distant == null || outside == null) throw new InvalidOperationException("Native building spawn failed");
            prepared = true;
        }

        private static Building Spawn(BuildingDefinition definition, string name, float distance)
        {
            Vector3 at = origin.ToLocalPosition() + Vector3.right * distance;
            at.y = Datum.LocalSeaY + 10f;
            if (Physics.Raycast(at + Vector3.up * 12000f, Vector3.down, out RaycastHit hit, 24000f,
                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)) at.y = hit.point.y;
            return NetworkSceneSingleton<Spawner>.i.SpawnBuilding(definition.unitPrefab, at.ToGlobalPosition(),
                Quaternion.identity, FactionRegistry.HqFromName("Boscali"), null, name, false, null);
        }
    }
}
#endif
