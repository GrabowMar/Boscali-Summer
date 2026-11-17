#if !PUBLIC_RELEASE
using System;
using System.Linq;
using BoscaliSummer.Core.Game;
using Newtonsoft.Json;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Read-only peer observations and explicit sandbox fixtures for the actual SA-74N network messages.</summary>
    public static class Sa74PeerProof
    {
        private const string BuildingName = "SA74PeerDestructionFixture";
        private static Building watchedBuilding;
        private static uint buildingId, buildingNetId;
        private static bool buildingSeen, buildingDisabled;
        private static GlobalPosition TriggerPosition()
        {
            float far = Mathf.Max(400000f, Sa74Fallout.ShockRadius * 3f);
            return new GlobalPosition(far, 200f, far);
        }
        private static bool Server => NetworkManagerNuclearOption.i?.Server?.Active == true;
        private static void RequirePeer()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")) &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                throw new InvalidOperationException("SA-74N proof requires a nomodkit sandbox");
        }
        private static void RequireHost()
        {
            RequirePeer();
            if (!Server) throw new InvalidOperationException("SA-74N mutations require a nomodkit loopback host");
        }

        public static string Snapshot()
        {
            RequirePeer();
            Sa74Store.Resolve();
            WeaponMount mount = RuntimeStores.Mount(Sa74Store.MountKey);
            MissileDefinition definition = GameAccess.EncyclopediaOrNull?.missiles.FirstOrDefault(m => m != null && m.jsonKey == Sa74Store.Key);
            string[] pairs = Sa74Store.EligibleAircraft.OrderBy(k => k, StringComparer.Ordinal).Select(k =>
            {
                Sa74Store.Pair(k, out Sa74Store.CargoPair pair);
                return k + ":" + pair.Lead + "," + pair.Partner;
            }).ToArray();
            return JsonConvert.SerializeObject(new
            {
                server = Server, ready = mount != null && definition != null && pairs.Length > 0,
                nativeMission = MissionManager.CurrentMission?.Name ?? "",
                prefabHash = definition?.unitPrefab?.GetComponent<Mirage.NetworkIdentity>()?.PrefabHash ?? 0,
                missileIndex = definition != null ? ((INetworkDefinition)definition).LookupIndex ?? -1 : -1,
                mountIndex = mount != null ? ((INetworkDefinition)mount).LookupIndex ?? -1 : -1,
                pairs, active = Sa74Fallout.Active, events = Sa74Fallout.Events, epoch = Sa74Fallout.Epoch,
                sites = Sa74Fallout.Sites.OrderBy(s => s.Id).Select(s => new
                {
                    id = s.Id, owner = s.Owner,
                    position = new[] { s.Position.AsVector3().x, s.Position.AsVector3().y, s.Position.AsVector3().z },
                    age = Time.time - s.Started
                }).ToArray(),
                fallout = Sa74Fallout.Instance?.Snapshot()
            });
        }

        public static string Trigger()
        {
            RequireHost();
            // Keep the pressure front outside either peer's transport fixtures even on larger theaters.
            Sa74Fallout.Detonate(TriggerPosition().ToLocalPosition(), null);
            return Snapshot();
        }

        public static string PrepareDestructionFixture()
        {
            RequireHost();
            if (FindBuilding() != null || buildingSeen) throw new InvalidOperationException("SA-74N peer building fixture already exists");
            BuildingDefinition definition = GameAccess.EncyclopediaOrNull?.buildings
                .Where(d => d != null && d.unitPrefab != null && d.unitPrefab.GetComponent<Building>() != null &&
                    d.unitPrefab.GetComponent<Hangar>() == null && d.unitPrefab.GetComponent<Factory>() == null)
                .OrderBy(d => d.jsonKey, StringComparer.Ordinal).FirstOrDefault();
            if (definition == null) throw new InvalidOperationException("No native building prefab for SA-74N peer proof");
            GlobalPosition position = TriggerPosition() + Vector3.right * 1000f;
            Building building = NetworkSceneSingleton<Spawner>.i.SpawnBuilding(definition.unitPrefab, position,
                Quaternion.identity, FactionRegistry.GetSortedHQs().FirstOrDefault(), null, BuildingName, false, null);
            if (building == null) throw new InvalidOperationException("Native peer building spawn failed");
            return BuildingSnapshot();
        }

        private static Building FindBuilding() => UnitRegistry.persistentUnitLookup.Values.Select(p => p.unit)
            .OfType<Building>().FirstOrDefault(b => b != null && b.NetworkUniqueName == BuildingName);

        private static void ObserveBuildingDisabled(Unit unit) => buildingDisabled = true;

        public static string BuildingSnapshot()
        {
            RequirePeer();
            Building building = FindBuilding();
            if (building != null && building != watchedBuilding)
            {
                watchedBuilding = building;
                buildingSeen = true;
                buildingId = building.persistentID.Id;
                buildingNetId = building.Identity.NetId;
                // Some native prefabs despawn at the end of collapse; retain the native disable event after that.
                building.onDisableUnit += ObserveBuildingDisabled;
            }
            if (building != null) buildingDisabled |= building.disabled;
            return JsonConvert.SerializeObject(new
            {
                server = Server, name = BuildingName, seen = buildingSeen, present = building != null,
                persistentId = buildingId, netId = buildingNetId, disabled = buildingDisabled,
                standing = building != null && !building.disabled, hasAuthority = building != null && building.HasAuthority,
                definition = building != null ? building.definition.jsonKey : "",
                position = building != null ? new[] { building.GlobalPosition().x, building.GlobalPosition().y, building.GlobalPosition().z } : null,
                triggerPosition = new[] { TriggerPosition().x, TriggerPosition().y, TriggerPosition().z },
                radius = Sa74Fallout.ShockRadius, destruction = Sa74Destruction.Snapshot()
            });
        }

        public static string TryDisableBuildingFromClient()
        {
            RequirePeer();
            if (Server) throw new InvalidOperationException("Run the building authority probe on the remote client");
            Building building = FindBuilding() ?? throw new InvalidOperationException("Prepare and observe the native building first");
            if (building.disabled) throw new InvalidOperationException("Probe the building before detonating");
            building.DisableUnit(); // A non-owning remote has no native disable command authority.
            return BuildingSnapshot();
        }

        public static string TryClientDetonation()
        {
            RequirePeer();
            if (Server) throw new InvalidOperationException("Run the authority probe on the remote client");
            Sa74Fallout.Detonate(new GlobalPosition(-100000f, 200f, -100000f).ToLocalPosition(), null);
            return Snapshot();
        }

        public static string ForgetClientState()
        {
            RequirePeer();
            if (Server) throw new InvalidOperationException("Clear only the remote client's fixture state");
            Sa74Fallout.Instance?.Clear();
            return Snapshot();
        }

        public static string ResetHostState()
        {
            RequireHost();
            Sa74Fallout.Instance?.ResetForScene();
            return Snapshot();
        }
    }
}
#endif
