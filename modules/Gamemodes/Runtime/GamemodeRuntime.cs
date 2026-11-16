using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Gamemodes.Domain;
using BoscaliSummer.Modules.Gamemodes.Networking;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using NuclearOption.SavedMission.Outcomes;
using UnityEngine;

namespace BoscaliSummer.Modules.Gamemodes.Runtime
{
    /// <summary>Mission-owned host state. Clients only receive its bounded presentation snapshot.</summary>
    internal sealed class GamemodeRuntime : MonoBehaviour, ISceneService, IMissionDashboardView
    {
        private sealed class Load
        {
            public Aircraft Aircraft;
            public Player Pilot;
            public int Team, Leg;
            public Airbase Destination;
            public readonly Dictionary<UnitDefinition, int> Payloads = new Dictionary<UnitDefinition, int>();
        }
        private sealed class Delivery
        {
            public Container Unit;
            public Player Pilot;
            public int Team, Leg;
            public string Manifest;
            public Airbase Destination;
            public float Settled = -1f;
        }

        private readonly Dictionary<Aircraft, Load> loads = new Dictionary<Aircraft, Load>();
        private readonly List<Delivery> deliveries = new List<Delivery>();
        private readonly List<Aircraft> deadLoads = new List<Aircraft>();
        private readonly Dictionary<Airbase, FactionHQ> originalOwners = new Dictionary<Airbase, FactionHQ>();
        private readonly FactionHQ[] teams = new FactionHQ[2];
        private readonly Airbase[] destinations = new Airbase[2];
        private Airbase source;
        private Mission mission;
        private GamemodeMatch match;
        private GamemodeNet network;
        private float nextTick;
        private int appliedLeg = -1;
        private GamemodePhase appliedPhase;
        private bool resultSent;
        private string problem;

        public static GamemodeRuntime Active { get; private set; }
        public static GameModeKind Kind => GamemodeRules.DetectMode(MissionManager.CurrentMission?.missionSettings?.description);
        public static bool Competitive => Kind == GameModeKind.Fuzzball || Kind == GameModeKind.Heavyweight;
        public static bool CanFly => !Competitive || Active?.match?.Phase == GamemodePhase.Running;
        public GamemodeStatus Current { get; private set; }
        public void Configure(GamemodeNet net) { network = net; Active = this; }
        public void Apply(GamemodeStatus status) => Current = status;

        public bool TryRead(out MissionDashboardStatus status)
        {
            status = default;
            GameModeKind kind = Kind;
            if (!MissionManager.IsRunning || kind == GameModeKind.None) return false;
            GamemodeStatus report = Current;
            bool ready = report.Mode == kind;
            bool competitive = kind != GameModeKind.WarBox;
            string phase = !ready ? "WAITING FOR HOST" : report.Phase == GamemodePhase.Finished ?
                report.Winner < 0 ? "DRAW" : (report.Winner == 0 ? "BDF" : "PALA") + " WINS" :
                report.Phase == GamemodePhase.Intermission ? "ROLE SWAP" :
                kind == GameModeKind.WarBox ? "CAMPAIGN" : kind == GameModeKind.Heavyweight ? "LEG " + report.Leg : "IN PROGRESS";
            string detail = kind == GameModeKind.WarBox ? "SUSTAINED WAR / DYNAMIC OPERATIONS" :
                kind == GameModeKind.Fuzzball ? "FIRST TO " + GamemodeRules.FuzzballKillTarget + " KILLS" : "TWO LEGS / ROLES SWAP";
            if (ready && kind == GameModeKind.Heavyweight && report.Phase == GamemodePhase.Running)
                detail = (report.TransportingTeam == 0 ? "BDF" : "PALA") + " HAUL / " + report.Cargo + " CRATES LOADED";
            status = new MissionDashboardStatus
            {
                Name = kind == GameModeKind.WarBox ? "WARBOX" : kind == GameModeKind.Fuzzball ? "FUZZBALL" : "HEAVYWEIGHT",
                Phase = phase, Ready = ready, HasScores = ready && competitive, HasTimer = ready && competitive,
                RemainingSeconds = ready ? report.Remaining : 0f,
                Score0 = ready ? report.Score0 : 0, Score1 = ready ? report.Score1 : 0,
                Objective = ready && kind == GameModeKind.Heavyweight && report.Phase == GamemodePhase.Running
                    ? "Haul cargo from Maris to your assigned field. Release it on the ground: 1 point per crate. Interceptors deny deliveries."
                    : !string.IsNullOrEmpty(report.Instruction) ? report.Instruction : "Waiting for a fresh authoritative mission report.",
                Route = ready ? report.Route ?? "" : "", Detail = detail
            };
            return true;
        }

        public void ResetForScene()
        {
            mission = null; match = null; source = null; problem = null;
            teams[0] = teams[1] = null; destinations[0] = destinations[1] = null;
            loads.Clear(); deliveries.Clear(); originalOwners.Clear();
            appliedLeg = -1; resultSent = false; nextTick = 0; Current = default;
            network?.ResetScene();
            FuzzballFlight.Reset();
        }

        private void OnDestroy() { ResetForScene(); if (Active == this) Active = null; }

        private void Update()
        {
            if (!MissionManager.IsRunning || Kind == GameModeKind.None)
            {
                if (mission != null) ResetForScene();
                return;
            }
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;
            if (!GameAccess.IsServer() || !MissionTimeSource.TryNow(out float now)) return;
            if (!ReferenceEquals(mission, MissionManager.CurrentMission))
            {
                ResetForScene();
                mission = MissionManager.CurrentMission;
            }
            if (match == null)
            {
                teams[0] = FactionRegistry.HqFromName("Boscali");
                teams[1] = FactionRegistry.HqFromName("Primeva");
                if (teams[0] == null || teams[1] == null) return;
                if (Kind == GameModeKind.Heavyweight && !FindRoute()) return;
                match = new GamemodeMatch(Kind, now);
            }
            match.Tick(now);
            ApplyTransitions();
            if (Kind == GameModeKind.Fuzzball) FuzzballFlight.Enforce();
            else if (Kind == GameModeKind.Heavyweight && match.Phase == GamemodePhase.Running)
            {
                FuzzballFlight.GrantAircraft();
                ObserveLoads();
                ObserveDeliveries(now);
            }
            if (Competitive)
                for (int i = 0; i < teams.Length; i++) if (teams[i] != null) teams[i].NetworkfactionScore = match.Score(i);
            GameManager.GetLocalPlayer<Player>(out Player local);
            Apply(Snapshot(local));
        }

        private bool FindRoute()
        {
            var bases = FactionRegistry.airbaseLookup;
            if (!bases.TryGetValue("Maris Airport", out source) ||
                !bases.TryGetValue("K92 Highway Strip", out destinations[0]) ||
                !bases.TryGetValue("South Boscali General Aviation", out destinations[1]))
            {
                problem = "Heavyweight route unavailable: required airfields missing. Restart with the installed Escalation map.";
                return false;
            }
            foreach (Airbase airbase in bases.Values)
                if (airbase != null && !originalOwners.ContainsKey(airbase)) originalOwners.Add(airbase, airbase.CurrentHQ);
            problem = null;
            return true;
        }

        private int Team(FactionHQ hq) => hq == null ? -1 : hq == teams[0] ? 0 : hq == teams[1] ? 1 : -1;
        private static string Field(Airbase airbase) => airbase?.SavedAirbase?.UniqueName ?? "";
        private static bool Near(Unit unit, Airbase airbase, float radius) => unit != null && airbase?.center != null &&
            Vector2.Distance(new Vector2(unit.transform.position.x, unit.transform.position.z),
                new Vector2(airbase.center.position.x, airbase.center.position.z)) <= radius;

        private void ApplyTransitions()
        {
            if (match == null || appliedLeg == match.Leg && appliedPhase == match.Phase) return;
            bool first = appliedLeg < 0;
            appliedLeg = match.Leg; appliedPhase = match.Phase;
            if (Kind == GameModeKind.Fuzzball && match.Phase == GamemodePhase.Finished) ClearSorties();
            if (Kind == GameModeKind.Heavyweight)
            {
                if (!first) ClearSorties();
                if (match.Phase == GamemodePhase.Running)
                {
                    FactionHQ transport = teams[match.TransportingTeam], attack = teams[1 - match.TransportingTeam];
                    foreach (var pair in originalOwners)
                    {
                        Airbase airbase = pair.Key;
                        if (airbase == null) continue;
                        // The same physical source and destinations change hands, preserving route distance and terrain.
                        FactionHQ owner = airbase == source || airbase == destinations[0] || airbase == destinations[1] ? transport :
                            pair.Value == null ? null : attack;
                        ((ICapturable)airbase).Capture.ForceCapture(owner);
                        ((ICapturable)airbase).Capture.SetCapturable(false);
                    }
                }
            }
            if (match.Phase != GamemodePhase.Finished || resultSent) return;
            resultSent = true;
            // Native has Victory/Defeat only. A tied match remains an explicit draw on our result panel.
            if (match.Winner >= 0) teams[match.Winner]?.DeclareEndGame(EndType.Victory);
        }

        private void ClearSorties()
        {
            loads.Clear(); deliveries.Clear();
            var spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) return;
            // The native active list drops disabled units; include their still-present wrecks too.
            var units = new List<Unit>();
            foreach (PersistentUnit persistent in UnitRegistry.persistentUnitLookup.Values)
                if (persistent.unit != null) units.Add(persistent.unit);
            foreach (Unit unit in units)
                if (unit != null && (unit is Aircraft || unit is Missile || unit is Container || unit is PilotDismounted))
                {
                    if (unit is Aircraft aircraft && aircraft.Player?.Aircraft == aircraft) aircraft.Player.RemoveAircraft(aircraft);
                    spawner.ServerObjectManager.Destroy(unit.gameObject);
                }
        }

        /// <summary>A source load is actual mounted container cargo on a landed, player-owned aircraft.</summary>
        private void ObserveLoads()
        {
            deadLoads.Clear();
            foreach (var pair in loads)
                if (pair.Key == null || pair.Key.disabled || pair.Value.Pilot == null ||
                    pair.Key.Player != pair.Value.Pilot || Team(pair.Value.Pilot.HQ) != pair.Value.Team) deadLoads.Add(pair.Key);
            foreach (Aircraft aircraft in deadLoads) loads.Remove(aircraft);
            foreach (Aircraft aircraft in UnitRegistry.allAircraft)
            {
                if (loads.Count >= 256) break;
                if (aircraft == null || aircraft.disabled || aircraft.Player == null ||
                    Team(aircraft.NetworkHQ) != match.TransportingTeam || aircraft.Player.HQ != aircraft.NetworkHQ ||
                    !aircraft.IsLanded() || !Near(aircraft, source, 2200f)) continue;
                var load = new Load { Aircraft = aircraft, Pilot = aircraft.Player, Team = match.TransportingTeam, Leg = match.Leg,
                    Destination = destinations[(int)((GamemodeRules.HeavyweightLegSeconds - match.RemainingSeconds) / 300) % destinations.Length] };
                foreach (WeaponStation station in aircraft.weaponStations)
                    foreach (Weapon weapon in station.Weapons)
                        if (weapon is MountedCargo cargo && cargo.GetAmmoTotal() > 0 && cargo.cargo?.unitPrefab?.GetComponent<Container>() != null)
                        {
                            load.Payloads.TryGetValue(cargo.cargo, out int count);
                            load.Payloads[cargo.cargo] = count + 1;
                        }
                // An empty airframe may still be completing its native loadout; try it again next tick.
                if (load.Payloads.Count > 0) loads[aircraft] = load;
                else loads.Remove(aircraft);
            }
        }

        public void CargoReleased(Unit cargo, Unit owner, Player pilot)
        {
            if (!GameAccess.IsServer() || Kind != GameModeKind.Heavyweight || match == null ||
                !(cargo is Container container) || !(owner is Aircraft aircraft) || deliveries.Count >= 512 ||
                !loads.TryGetValue(aircraft, out Load load) || load.Leg != match.Leg || load.Pilot != pilot ||
                pilot == null || pilot.HQ != aircraft.NetworkHQ || Team(pilot.HQ) != load.Team ||
                !load.Payloads.TryGetValue(cargo.definition, out int remaining) || remaining <= 0) return;
            load.Payloads[cargo.definition] = remaining - 1;
            string id = match.Leg + ":" + container.persistentID;
            if (!match.TryLoadCargo(MissionTimeSource.Now(), id, load.Team, Field(source), Field(load.Destination), 1)) return;
            deliveries.Add(new Delivery { Unit = container, Pilot = pilot, Team = load.Team, Leg = match.Leg,
                Manifest = id, Destination = load.Destination });
        }

        private void ObserveDeliveries(float now)
        {
            for (int i = deliveries.Count - 1; i >= 0; i--)
            {
                Delivery item = deliveries[i];
                Container cargo = item.Unit;
                if (cargo == null || cargo.disabled || item.Pilot == null || Team(item.Pilot.HQ) != item.Team ||
                    Team(cargo.NetworkHQ) != item.Team || item.Leg != match.Leg)
                { match.DiscardCargo(now, item.Manifest); deliveries.RemoveAt(i); continue; }
                bool settled = !cargo.IsSlung() && cargo.rb != null && cargo.rb.velocity.sqrMagnitude < 1f &&
                    Near(cargo, item.Destination, 800f) && Physics.Raycast(cargo.transform.position + Vector3.up,
                        Vector3.down, 6f, (int)PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
                item.Settled = settled ? (item.Settled < 0 ? now : item.Settled) : -1f;
                if (item.Settled < 0 || now - item.Settled < 5f) continue;
                if (match.TryDeliverCargo(now, item.Manifest, "delivered:" + item.Manifest, item.Team, Field(item.Destination), 1))
                {
                    // The recipient consumes the physical crate, so it cannot be slung back and scored again.
                    NetworkSceneSingleton<Spawner>.i.ServerObjectManager.Destroy(cargo.gameObject);
                    deliveries.RemoveAt(i);
                }
            }
        }

        public void Kill(PersistentID killer, PersistentID victim)
        {
            if (!GameAccess.IsServer() || Kind != GameModeKind.Fuzzball || match == null ||
                !UnitRegistry.TryGetPersistentUnit(killer, out PersistentUnit attacking) ||
                !UnitRegistry.TryGetPersistentUnit(victim, out PersistentUnit killed)) return;
            match.TryCreditKill(MissionTimeSource.Now(), victim.ToString(), Team(attacking.GetHQ()), Team(killed.GetHQ()),
                killed.definition is AircraftDefinition);
        }

        public bool AllowsHeavyweightSpawn(Airbase at, Player player)
        {
            if (Kind != GameModeKind.Heavyweight) return true;
            if (match == null || match.Phase != GamemodePhase.Running || player == null) return false;
            int team = Team(player.HQ);
            return team >= 0 && (team == match.TransportingTeam ? at == source : at != source && at != destinations[0] && at != destinations[1]);
        }

        public GamemodeStatus Snapshot(Player player = null)
        {
            if (!GameAccess.IsServer() || Kind == GameModeKind.None || !ReferenceEquals(mission, MissionManager.CurrentMission)) return default;
            var status = new GamemodeStatus { Winner = -1, TransportingTeam = -1,
                Instruction = problem ?? "Waiting for mission initialization." };
            if (match == null) return status;
            status.Mode = Kind;
            status.Phase = match.Phase; status.Leg = match.Leg; status.TransportingTeam = match.TransportingTeam;
            status.Score0 = match.Score(0); status.Score1 = match.Score(1); status.Winner = match.Winner;
            status.Remaining = double.IsInfinity(match.RemainingSeconds) ? 0 : (float)match.RemainingSeconds;
            status.Route = "";
            status.Instruction = Kind == GameModeKind.WarBox ?
                "Sustained war. Brief at MIS; choose a role, complete contracts, return and resupply. Native strategic victory." :
                Kind == GameModeKind.Fuzzball ? "Enemy plane kills only. First to 75, or highest score after 20 minutes. Unlimited airborne respawns." :
                "Load container cargo at Maris. Fly to the assigned field and release it on the ground; 1 point per delivered crate. Interceptors score by denying deliveries.";
            if (Kind == GameModeKind.Heavyweight)
            {
                Airbase destination = destinations[(int)((GamemodeRules.HeavyweightLegSeconds - Math.Min(GamemodeRules.HeavyweightLegSeconds, match.RemainingSeconds)) / 300) % 2];
                if (player?.Aircraft != null && loads.TryGetValue(player.Aircraft, out Load load))
                {
                    destination = load.Destination;
                    foreach (int count in load.Payloads.Values) status.Cargo += count;
                }
                status.Route = Field(source) + " -> " + Field(destination);
                if (match.Phase == GamemodePhase.Intermission) status.Instruction = "HALFTIME. Scores retained; aircraft and cargo cleared. Roles swap for the same route and equal time.";
            }
            if (match.Phase == GamemodePhase.Finished) status.Instruction = match.Winner < 0 ? "DRAW. Match complete. Host: restart or return through the pause menu." :
                (match.Winner == 0 ? "BDF" : "PALA") + " WINS. Match complete.";
            return status;
        }
    }
}
