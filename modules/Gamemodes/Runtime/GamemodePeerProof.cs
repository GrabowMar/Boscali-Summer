#if !PUBLIC_RELEASE
using System;
using System.Linq;
using BoscaliSummer.Modules.Gamemodes.Domain;
using BoscaliSummer.Modules.Gamemodes.Networking;
using Newtonsoft.Json;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Gamemodes.Runtime
{
    public static class GamemodePeerProof
    {
        private const string AttackerName = "GamemodePeerAttacker", TargetName = "GamemodePeerTarget";
        private static bool Server => NetworkManagerNuclearOption.i?.Server?.Active == true;
        private static void RequirePeer()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")) &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                throw new InvalidOperationException("Gamemode proof requires a nomodkit sandbox");
        }
        private static void RequireHost()
        {
            RequirePeer();
            if (!Server) throw new InvalidOperationException("Gamemode mutations require a nomodkit loopback host");
            if (GamemodeRuntime.Kind != GameModeKind.Fuzzball || !GamemodeRuntime.CanFly)
                throw new InvalidOperationException("A running Fuzzball match is required");
        }
        private static Aircraft Find(string name) => UnitRegistry.persistentUnitLookup.Values.Select(p => p.unit).OfType<Aircraft>().FirstOrDefault(
            a => a != null && a.NetworkUniqueName == name);
        public static string SpawnFixture()
        {
            RequireHost();
            if (Find(AttackerName) != null || Find(TargetName) != null)
                throw new InvalidOperationException("Gamemode fixture already exists");
            Spawn(AttackerName, "Boscali", 0f);
            Spawn(TargetName, "Primeva", 2000f);
            return Snapshot();
        }
        private static void Spawn(string name, string faction, float x)
        {
            FactionHQ hq = FactionRegistry.HqFromName(faction);
            AircraftDefinition definition = hq?.AircraftSupply.Keys.FirstOrDefault(d =>
                FuzzballFlight.Allowed(hq, d) && d.aircraftParameters?.loadouts?.Count > 1);
            if (definition == null) throw new InvalidOperationException("No native fixture aircraft for " + faction);
            Aircraft aircraft = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null, definition.unitPrefab,
                definition.aircraftParameters.loadouts[1], 1f, default,
                new GlobalPosition(x, 4000f, -40000f), Quaternion.identity, Vector3.forward * 180f,
                null, hq, name, .5f, .5f);
            if (aircraft == null) throw new InvalidOperationException("Native fixture spawn failed");
        }
        public static string TryDisableFromClient()
        {
            RequirePeer();
            if (Server) throw new InvalidOperationException("Run the authority probe on the client");
            Aircraft target = Find(TargetName) ?? throw new InvalidOperationException("Spawn fixture first");
            // Native DisableUnit sends no server command for a non-owning client.
            target.DisableUnit();
            return Snapshot();
        }
        public static string CreditEnemyKill()
        {
            RequireHost();
            Aircraft attacker = Find(AttackerName) ?? throw new InvalidOperationException("Spawn fixture first");
            Aircraft target = Find(TargetName) ?? throw new InvalidOperationException("Spawn fixture first");
            if (target.disabled) throw new InvalidOperationException("Fixture target was already disabled");
            GamemodeStatus before = GamemodeRuntime.Active.Snapshot();
            target.RecordDamage(attacker.persistentID, 1000f);
            target.DisableUnit(); // Native ReportKilled and outgoing RPC feed mode credit.
            GamemodeStatus after = GamemodeRuntime.Active.Snapshot();
            return JsonConvert.SerializeObject(new { before0 = before.Score0, before1 = before.Score1,
                after0 = after.Score0, after1 = after.Score1, disabled = target.disabled,
                creditedOne = after.Score0 == before.Score0 + 1 && after.Score1 == before.Score1 });
        }
        public static string RepeatKillReport()
        {
            RequireHost();
            Aircraft target = Find(TargetName) ?? throw new InvalidOperationException("Native target expired");
            if (!target.disabled) throw new InvalidOperationException("Credit the native kill first");
            GamemodeStatus before = GamemodeRuntime.Active.Snapshot();
            target.ReportKilled();
            GamemodeStatus after = GamemodeRuntime.Active.Snapshot();
            return JsonConvert.SerializeObject(new { before0 = before.Score0, before1 = before.Score1,
                after0 = after.Score0, after1 = after.Score1,
                unchanged = after.Score0 == before.Score0 && after.Score1 == before.Score1 });
        }
        public static string Snapshot()
        {
            RequirePeer();
            GameManager.GetLocalPlayer<Player>(out Player local);
            GamemodeRuntime runtime = GamemodeRuntime.Active;
            GamemodeStatus status = runtime == null ? default : Server ? runtime.Snapshot(local) : runtime.Current;
            return JsonConvert.SerializeObject(new
            {
                server = Server, nativeMode = GamemodeRuntime.Kind.ToString(),
                nativeMission = MissionManager.CurrentMission?.Name ?? "",
                source = Server ? "authoritative" : "received_custom_snapshot",
                mode = status.Mode.ToString(), phase = status.Phase.ToString(), leg = status.Leg,
                score0 = status.Score0, score1 = status.Score1, winner = status.Winner,
                remaining = status.Remaining, route = status.Route ?? "", instruction = status.Instruction ?? "",
                surface = UnitRegistry.persistentUnitLookup.Values.Count(p => p.unit != null && FuzzballFlight.Surface(p.unit)),
                air = UnitRegistry.allAircraft.Count(a => a != null && !a.disabled),
                nativeScore0 = FactionRegistry.HqFromName("Boscali")?.factionScore,
                nativeScore1 = FactionRegistry.HqFromName("Primeva")?.factionScore,
                attacker = AircraftState(Find(AttackerName)), target = AircraftState(Find(TargetName))
            });
        }
        private static object AircraftState(Aircraft aircraft) => aircraft == null ? null : new
        {
            netId = aircraft.Identity.NetId, id = aircraft.persistentID.ToString(),
            faction = aircraft.NetworkHQ?.faction?.factionName ?? "", disabled = aircraft.disabled,
            authority = aircraft.HasAuthority, server = aircraft.IsServer
        };
    }
}
#endif
