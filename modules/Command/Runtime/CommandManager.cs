using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Runtime
{
    internal sealed class CommandManager : MonoBehaviour, ISceneService, IFactionMoraleView
    {
        public static CommandManager Active { get; internal set; }

        private ManualLogSource logger;
        private IOperationOutcomeSource operationOutcomes;
        private IActiveEventsView activeEvents;
        private FactionMoraleNet moraleNet;

        private void Update()
        {
            ModServices.TryGet(out IOperationOutcomeSource source);
            if (!ReferenceEquals(source, operationOutcomes))
            {
                if (operationOutcomes != null) operationOutcomes.MoraleAwarded -= OnOperationMorale;
                operationOutcomes = source;
                if (operationOutcomes != null) operationOutcomes.MoraleAwarded += OnOperationMorale;
            }
            ModServices.TryGet(out IActiveEventsView events);
            if (ReferenceEquals(events, activeEvents)) return;
            if (activeEvents != null) activeEvents.MoraleAwarded -= OnOperationMorale;
            activeEvents = events;
            if (activeEvents != null) activeEvents.MoraleAwarded += OnOperationMorale;
        }

        private void OnOperationMorale(int faction, float delta)
        {
            if (GameAccess.IsServer() && Morale.TryGet(faction, out float value))
                Morale.TrySet(faction, Mathf.Clamp(value + delta, 0f, 100f));
        }

        public bool TryGetContractMultiplier(int factionId, out float multiplier)
        {
            multiplier = 1f;
            if (!GameAccess.IsServer() || !Morale.TryGet(factionId, out float morale)) return false;
            multiplier = FactionMoraleState.ContractMultiplier(morale);
            return true;
        }

        public readonly TacticalTheaterState TheaterState = new TacticalTheaterState();
        internal readonly FactionMoraleState Morale = new FactionMoraleState();

        /// <summary>Whether a surface target carries a radar, remembered per unit instance.</summary>
        private readonly Dictionary<int, bool> emitterCache = new Dictionary<int, bool>(64);

        private const int EmitterCacheLimit = 512;

        internal bool TryGetRemoteMorale(string factionName, out float morale)
        {
            morale = 0f;
            return moraleNet != null && moraleNet.TryGet(factionName, out morale);
        }

        public void Configure(ManualLogSource log, FactionMoraleNet network)
        {
            logger = log;
            moraleNet = network;
            Active = this;
            logger?.LogInfo("[COM] Faction Morale ready: host-owned, 0–100; new contract rewards scale from 0.8x to 1.1x.");
        }

        public void ResetForScene()
        {
            TheaterState.Reset();
            Morale.Reset();
            moraleNet?.ResetScene();
            emitterCache.Clear();
        }

        private void OnDestroy()
        {
            if (operationOutcomes != null) operationOutcomes.MoraleAwarded -= OnOperationMorale;
            if (activeEvents != null) activeEvents.MoraleAwarded -= OnOperationMorale;
            moraleNet?.ResetScene();
            if (Active == this) Active = null;
        }

        public void SyncSectorTelemetry(TacticalSectorGrid grid)
        {
            if (grid == null) return;
            TheaterState.FriendlySectorCount = grid.FriendlySectorCount;
            TheaterState.HostileSectorCount = grid.HostileSectorCount;
            TheaterState.ContestedSectorCount = grid.ContestedSectorCount;
            TheaterState.NeutralSectorCount = grid.NeutralSectorCount;
            TheaterState.TerritoryControlRatio = grid.TerritoryControlRatio;
            TheaterState.ActiveClashesCount = grid.ActiveClashesCount;
            TheaterState.TotalNodesCount = grid.TotalNodesCount;
            TheaterState.TotalSectorCount = grid.TotalSectors;
            TheaterState.FrontlineSegmentCount = grid.FrontlineSegmentCount;
            TheaterState.FrontlineLengthMetres = grid.FrontlineLengthMetres;

            // Which bases are being argued over is a property of the field, not of the
            // airbase list: an airbase is contested when the ground around it is.
            int contested = 0;
            IReadOnlyList<TacticalSectorGrid.TacticalNode> nodes = grid.GetNodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].IsAirbase && nodes[i].IsContested) contested++;
            }
            TheaterState.ContestedAirbaseCount = contested;
        }


        /// <summary>
        /// Count every airbase in the theater, not just the ones this HQ already holds.
        ///
        /// <para><c>FactionHQ.GetAirbases()</c> is the faction's own list, so classifying it
        /// by owner can only ever produce friendly bases — the enemy count read zero for as
        /// long as it was on screen. The fixed catalogue is the same source the sector grid
        /// reconciles nodes from, so the two boards now agree.</para>
        /// </summary>
        private void CountAirbases(FactionHQ localHq)
        {
            if (FactionRegistry.airbaseLookup == null) return;

            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                // A carrier holds no ground and belongs on no territorial tally.
                if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed()) continue;

                if (airbase.CurrentHQ == null) TheaterState.NeutralAirbaseCount++;
                else if (airbase.CurrentHQ == localHq) TheaterState.FriendlyAirbaseCount++;
                else TheaterState.HostileAirbaseCount++;
            }
        }

        /// <summary>
        /// Add one friendly aircraft to the sortie board, by what its AI pilot is pointed at.
        ///
        /// <para>Player-flown aircraft are excluded because a sortie board reports what the
        /// theater is doing on its own; and Wing Command's recruited wing is excluded
        /// because those aircraft are under the player's orders, not the theater's. Reading
        /// them here would double-count the same jets on two different consoles.</para>
        /// </summary>
        private void TallySortie(Aircraft aircraft)
        {
            if (!GameAccess.AiPilotCombatAvailable) return;

            Pilot[] pilots = aircraft != null ? aircraft.pilots : null;
            if (pilots == null || pilots.Length == 0) return;

            Pilot pilot = pilots[0];
            if (pilot == null || pilot.dead || pilot.playerControlled) return;
            if (IsPublishedWingMember(aircraft)) return;

            var combat = pilot.currentState as AIPilotCombatModes;
            SortieTarget target = combat != null
                ? KindOf(GameAccess.GetAiCurrentTarget(combat))
                : SortieTarget.None;

            TheaterState.Sorties.Add(SortieClassifier.Classify(target));
        }

        /// <summary>
        /// Whether this aircraft is a live Wing Command wingman. Absent Wing Command this is
        /// always false and every AI jet counts, which is correct.
        /// </summary>
        private static bool IsPublishedWingMember(Aircraft aircraft)
        {
            return aircraft != null && WingLink.IsWingMember(aircraft.persistentID.GetHashCode());
        }

        /// <summary>
        /// What a target is, in the terms the classifier understands. An emitter is called
        /// out separately because hunting one is a different sortie from bombing it.
        ///
        /// <para>Deciding whether a surface target emits means looking for a radar on it,
        /// which is a component search — so the answer is remembered per unit. Targets do
        /// not grow radars mid-mission, and without the cache this would re-walk a
        /// hierarchy for every tasked aircraft on every refresh.</para>
        /// </summary>
        private SortieTarget KindOf(Unit target)
        {
            if (target == null || target.disabled) return SortieTarget.None;
            if (target is Aircraft) return SortieTarget.Aircraft;
            if (target is Ship) return SortieTarget.Ship;
            if (target is PilotDismounted) return SortieTarget.Infantry;
            if (!(target is GroundVehicle || target is Building)) return SortieTarget.None;

            int id = target.GetInstanceID();
            if (emitterCache.TryGetValue(id, out bool emits))
                return emits ? SortieTarget.Emitter
                     : target is GroundVehicle ? SortieTarget.Vehicle : SortieTarget.Structure;

            emits = target.GetComponentInChildren<Radar>(true) != null;
            if (emitterCache.Count < EmitterCacheLimit) emitterCache[id] = emits;

            return emits ? SortieTarget.Emitter
                 : target is GroundVehicle ? SortieTarget.Vehicle : SortieTarget.Structure;
        }

        public void UpdateTelemetry(FactionHQ localHq)
        {
            if (localHq == null) return;

            TheaterState.FriendlyAircraftCount = 0;
            TheaterState.HostileAircraftCount = 0;
            TheaterState.FriendlyAirbaseCount = 0;
            TheaterState.HostileAirbaseCount = 0;
            TheaterState.NeutralAirbaseCount = 0;
            TheaterState.ContestedAirbaseCount = 0;
            TheaterState.FriendlyRadarCount = 0;
            TheaterState.FriendlyGroundUnitsCount = 0;
            TheaterState.HostileGroundUnitsCount = 0;
            TheaterState.Sorties.Reset();

            IReadOnlyList<Aircraft> allAircraft = UnitRegistry.allAircraft;
            if (allAircraft != null)
            {
                for (int i = 0; i < allAircraft.Count; i++)
                {
                    Aircraft ac = allAircraft[i];
                    if (ac == null || ac.disabled) continue;
                    if (ac.NetworkHQ == localHq)
                    {
                        TheaterState.FriendlyAircraftCount++;
                        TallySortie(ac);
                    }
                    else if (localHq.IsTargetBeingTracked(ac)) TheaterState.HostileAircraftCount++;
                }
            }

            int totalAir = TheaterState.FriendlyAircraftCount + TheaterState.HostileAircraftCount;
            TheaterState.AirSuperiorityRatio = totalAir > 0
                ? (float)TheaterState.FriendlyAircraftCount / totalAir
                : float.NaN;

            CountAirbases(localHq);

            // Ground & Naval Forces
            List<Unit> allUnits = UnitRegistry.allUnits;
            if (allUnits != null)
            {
                for (int i = 0; i < allUnits.Count; i++)
                {
                    Unit u = allUnits[i];
                    if (u == null || u.disabled) continue;
                    if (u is GroundVehicle || u is Ship)
                    {
                        if (u.NetworkHQ == localHq) TheaterState.FriendlyGroundUnitsCount++;
                        else if (localHq.IsTargetBeingTracked(u)) TheaterState.HostileGroundUnitsCount++;
                    }
                }
            }

            // Emitters on the friendly network. This is a radar count, and the panel says so.
            if (GameAccess.HqSensorsAvailable)
            {
                List<Radar> radars = GameAccess.GetHqRadars(localHq);
                if (radars != null) TheaterState.FriendlyRadarCount = radars.Count;
            }

            // Defcon & Early Warning status
            if (TheaterState.HostileAircraftCount > 0 && (TheaterState.AirSuperiorityRatio < 0.35f || TheaterState.HostileAircraftCount > TheaterState.FriendlyAircraftCount * 2))
            {
                TheaterState.DefconLevel = 1;
                TheaterState.PrimaryThreatDescription = "AIR DEFENSE ALERT";
                TheaterState.ActiveThreatWarning = "RED ALERT: HEAVY AIR THREAT DETECTED";
            }
            else if (TheaterState.ContestedSectorCount > 0)
            {
                TheaterState.DefconLevel = 2;
                TheaterState.PrimaryThreatDescription = "ACTIVE GROUND BATTLE";
                TheaterState.ActiveThreatWarning = "AMBER ALERT: " + TheaterState.ContestedSectorCount + " CONTESTED SECTORS IN CONFLICT";
            }
            else if (TheaterState.AirSuperiorityRatio > 0.65f && TheaterState.FriendlyAirbaseCount >= TheaterState.HostileAirbaseCount)
            {
                TheaterState.DefconLevel = 4;
                TheaterState.PrimaryThreatDescription = "AIR DOMINANCE";
                TheaterState.ActiveThreatWarning = "AIR DOMINANCE ESTABLISHED";
            }
            else
            {
                TheaterState.DefconLevel = 3;
                TheaterState.PrimaryThreatDescription = "CONTESTED THEATER";
                TheaterState.ActiveThreatWarning = "AIRSPACE NOMINAL / PATROLS ACTIVE";
            }
        }
    }
}
