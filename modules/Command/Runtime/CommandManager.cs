using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Networking;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Runtime
{
    internal sealed class CommandManager : MonoBehaviour, ISceneService, IFactionMoraleView, ITheaterCensusSource
    {
        public static CommandManager Active { get; internal set; }

        /// <summary>How often the bag the ticker reads is refreshed (unscaled seconds). Cheap: it copies a published census.</summary>
        private const float CensusSeconds = 1f;

        /// <summary>How often a host without a Living Front snapshot measures its own faction (unscaled seconds): three passes over the unit registries.</summary>
        private const float LocalMeasureSeconds = 10f;

        private IOperationOutcomeSource operationOutcomes;
        private IActiveEventsView activeEvents;
        private ITheaterWarHost warHost;
        private ITheaterWarView warView;
        private FactionMoraleNet moraleNet;
        private TerritoryControlView territory;
        private float nextCensus, nextLocalMeasure;
        private TheaterCensus localCensus = TheaterCensus.Unmeasured;

        /// <summary>What <see cref="ApplyCensus"/> last applied. The Station Board reads it for SITREP when the war view has no snapshot, so the panel causes no scan of its own.</summary>
        internal TheaterCensus LastCensus { get; private set; } = TheaterCensus.Unmeasured;

        /// <summary>Unscaled time at which a measured census was last applied (not when it was measured: a stalled staff republishes nothing, so the snapshot census can be older); -1 while none.</summary>
        internal float LastCensusAt { get; private set; } = -1f;

        private void Update()
        {
            ResolveSources();
            TickCensus(Time.unscaledTime);
        }

        /// <summary>Re-points the three morale subscriptions when a service appears or changes. Never returns early: the census tick runs after it.</summary>
        private void ResolveSources()
        {
            ModuleServices.TryGet(out IOperationOutcomeSource source);
            if (!ReferenceEquals(source, operationOutcomes))
            {
                if (operationOutcomes != null) operationOutcomes.MoraleAwarded -= OnOperationMorale;
                operationOutcomes = source;
                if (operationOutcomes != null) operationOutcomes.MoraleAwarded += OnOperationMorale;
            }
            ModuleServices.TryGet(out IActiveEventsView events);
            if (!ReferenceEquals(events, activeEvents))
            {
                if (activeEvents != null) activeEvents.MoraleAwarded -= OnOperationMorale;
                activeEvents = events;
                if (activeEvents != null) activeEvents.MoraleAwarded += OnOperationMorale;
            }
            ModuleServices.TryGet(out ITheaterWarHost host);
            if (!ReferenceEquals(host, warHost))
            {
                if (warHost != null) warHost.MoraleAwarded -= OnOperationMorale;
                warHost = host;
                if (warHost != null) warHost.MoraleAwarded += OnOperationMorale;
            }
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

        public void Configure(FactionMoraleNet network, TerritoryControlView control)
        {
            moraleNet = network;
            territory = control;
            Active = this;
            Plugin.Logger?.LogInfo("[COM] Faction Morale ready: host-owned, 0–100; new contract rewards scale from 0.8x to 1.1x.");
        }

        public void ResetForScene()
        {
            if (operationOutcomes != null) operationOutcomes.MoraleAwarded -= OnOperationMorale;
            operationOutcomes = null;
            if (activeEvents != null) activeEvents.MoraleAwarded -= OnOperationMorale;
            activeEvents = null;
            if (warHost != null) warHost.MoraleAwarded -= OnOperationMorale;
            warHost = null;
            warView = null;
            nextCensus = nextLocalMeasure = 0f;
            localCensus = TheaterCensus.Unmeasured;
            LastCensus = TheaterCensus.Unmeasured;
            LastCensusAt = -1f;
            TheaterState.Reset();
            Morale.Reset();
            moraleNet?.ResetScene();
            emitterCache.Clear();
        }

        private void OnDestroy()
        {
            if (operationOutcomes != null) operationOutcomes.MoraleAwarded -= OnOperationMorale;
            if (activeEvents != null) activeEvents.MoraleAwarded -= OnOperationMorale;
            if (warHost != null) warHost.MoraleAwarded -= OnOperationMorale;
            moraleNet?.ResetScene();
            if (Active == this) Active = null;
        }

        /// <summary>
        /// The one writer of the theater bag. The war snapshot's census wins (the host measured it for this faction, and a client receives it);
        /// a host without a snapshot, for example with TheaterOps off, measures its own faction; anything else is not measured and reads as such.
        /// </summary>
        private void TickCensus(float now)
        {
            if (now < nextCensus) return;
            nextCensus = now + CensusSeconds;

            if (warView == null) ModuleServices.TryGet(out warView);
            TheaterWarSnapshot snapshot = warView != null ? warView.Snapshot : null;
            if (snapshot != null && snapshot.Census.Known)
            {
                ApplyCensus(snapshot.Census, now);
                return;
            }
            if (GameAccess.IsServer() && GameAccess.TryGetLocalFaction(out FactionHQ hq) && hq != null)
            {
                if (now >= nextLocalMeasure)
                {
                    // A world that has not populated yet is not measured: retry next tick rather than hold a calm reading for 10 s.
                    nextLocalMeasure = TryMeasure(hq, out localCensus) ? now + LocalMeasureSeconds : 0f;
                }
                ApplyCensus(localCensus, now);
                return;
            }
            ApplyCensus(TheaterCensus.Unmeasured, now);
        }

        /// <summary>
        /// Remembers the census (the Station Board reads it) and writes it into the bag the news ticker reads; see <see cref="CensusBag"/>.
        /// </summary>
        internal void ApplyCensus(in TheaterCensus c, float now)
        {
            LastCensus = c;
            LastCensusAt = c.Known ? now : -1f;

            CensusBag.Apply(TheaterState, c);
        }

        /// <summary>
        /// Host only: counts one faction's side of the war from the registries. Aircraft are split into airborne and parked, hostile units count only
        /// while the faction tracks them (<see cref="HostileContact"/>, the same fresh window the engine's contact scan uses), and the sector, front and
        /// contested-base figures come from that faction's own control field. Anything that could not be established stays -1.
        /// </summary>
        public bool TryMeasure(FactionHQ hq, out TheaterCensus census)
        {
            census = TheaterCensus.Unmeasured;
            if (hq == null || !GameAccess.IsServer()) return false;

            List<Unit> allUnits = UnitRegistry.allUnits;
            if (allUnits == null || allUnits.Count == 0) return false;

            float now = Time.timeSinceLevelLoad;
            TheaterCensus c = TheaterCensus.Unmeasured;
            bool pilots = GameAccess.AiPilotCombatAvailable;
            SortieTally sorties = default;
            int owned = 0;

            IReadOnlyList<Aircraft> allAircraft = UnitRegistry.allAircraft;
            if (allAircraft != null)
            {
                int airborne = 0, parked = 0, hostile = 0;
                for (int i = 0; i < allAircraft.Count; i++)
                {
                    Aircraft ac = allAircraft[i];
                    if (ac == null || ac.disabled) continue;
                    bool landed = ac.IsLanded();
                    if (ac.NetworkHQ == hq)
                    {
                        if (landed) parked++;
                        else
                        {
                            airborne++;
                            if (pilots) TallySortie(ac, ref sorties);
                        }
                    }
                    else if (!landed && HostileContact.TryRead(hq, ac, now, out _)) hostile++;
                }
                owned += airborne + parked;
                c.AirFriendly = Count(airborne);
                c.AirParked = Count(parked);
                c.AirHostileTracked = Count(hostile);
            }

            int groundOwn = 0, groundHostile = 0, shipsOwn = 0, shipsHostile = 0;
            for (int i = 0; i < allUnits.Count; i++)
            {
                Unit u = allUnits[i];
                if (u == null || u.disabled) continue;
                bool ground = u is GroundVehicle;
                if (!ground && !(u is Ship)) continue;
                if (u.NetworkHQ == hq)
                {
                    if (ground) groundOwn++; else shipsOwn++;
                }
                else if (HostileContact.TryRead(hq, u, now, out _))
                {
                    if (ground) groundHostile++; else shipsHostile++;
                }
            }
            owned += groundOwn + shipsOwn;
            c.GroundFriendly = Count(groundOwn);
            c.GroundHostileTracked = Count(groundHostile);
            c.ShipsFriendly = Count(shipsOwn);
            c.ShipsHostileTracked = Count(shipsHostile);

            // Every airbase in the theater, not only the ones this HQ already holds: the faction's own list can only ever be friendly.
            if (FactionRegistry.airbaseLookup != null)
            {
                int own = 0, theirs = 0, neutral = 0;
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    // A carrier holds no ground and belongs on no territorial tally.
                    if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed()) continue;
                    if (airbase.CurrentHQ == null) neutral++;
                    else if (airbase.CurrentHQ == hq) own++;
                    else theirs++;
                }
                owned += own;
                c.BasesFriendly = Count(own);
                c.BasesHostile = Count(theirs);
                c.BasesNeutral = Count(neutral);
            }

            // A faction that owns no aircraft, vehicle, ship or base has nothing to measure: at mission start the units are not spawned and the bases not yet assigned,
            // and "nothing to count" must not read as a calm theater (threat 5). It stays unmeasured until the faction exists.
            if (owned == 0) return false;

            // Which of our bases are being argued over is a property of the field, not of the airbase list: a friendly base with hostile presence in its cell.
            // A field with every cell neutral has been fed nothing yet (no base reconciled, no unit seen), which is "no data" and not a calm front: the figures stay -1.
            TacticalSectorGrid grid = territory != null ? territory.Read(hq) : null;
            if (grid != null && grid.NeutralSectorCount < grid.TotalSectors)
            {
                IReadOnlyList<TacticalSectorGrid.TacticalNode> nodes = grid.GetNodes();
                int contested = 0;
                for (int i = 0; i < nodes.Count; i++)
                {
                    TacticalSectorGrid.TacticalNode node = nodes[i];
                    if (node.IsAirbase && node.IsContested && node.Faction == SectorControl.Friendly) contested++;
                }
                c.BasesContested = Count(contested);
                c.SectorsFriendly = Count(grid.FriendlySectorCount);
                c.SectorsHostile = Count(grid.HostileSectorCount);
                c.SectorsContested = Count(grid.ContestedSectorCount);
                c.SectorsNeutral = Count(grid.NeutralSectorCount);
                c.FrontMetres = grid.FrontlineLengthMetres;
            }

            // Emitters on the friendly network. This is a radar count, and the panel says so.
            if (GameAccess.HqSensorsAvailable)
            {
                List<Radar> radars = GameAccess.GetHqRadars(hq);
                if (radars != null) c.Radars = Count(radars.Count);
            }

            if (pilots)
            {
                c.SortieCap = Count(sorties.Cap); c.SortieSead = Count(sorties.Sead); c.SortieCas = Count(sorties.Cas);
                c.SortieStrike = Count(sorties.Strike); c.SortieTransit = Count(sorties.Transit);
            }

            c.Known = true;
            c.Threat = ThreatLevelRules.Level(c, out TheaterThreatWhy why);
            c.ThreatWhy = why;
            census = c;
            return true;
        }

        private static short Count(int n) => (short)Math.Min(n, short.MaxValue);

        /// <summary>
        /// Add one airborne friendly aircraft to the sortie tally, by what its AI pilot is pointed at. A parked jet is never tallied: it is
        /// on the apron, not on a sortie. The local player's jet is excluded by identity as well as by pilot, so a seat flown by a script counts no sortie either.
        ///
        /// <para>Player-flown aircraft are excluded because a sortie board reports what the
        /// theater is doing on its own; and Wing Command's recruited wing is excluded
        /// because those aircraft are under the player's orders, not the theater's. Reading
        /// them here would double-count the same jets on two different consoles.</para>
        /// </summary>
        private void TallySortie(Aircraft aircraft, ref SortieTally tally)
        {
            Pilot[] pilots = aircraft != null ? aircraft.pilots : null;
            if (pilots == null || pilots.Length == 0) return;

            Pilot pilot = pilots[0];
            if (pilot == null || pilot.dead || pilot.playerControlled || GameManager.IsLocalAircraft(aircraft)) return;
            if (IsPublishedWingMember(aircraft)) return;

            var combat = pilot.currentState as AIPilotCombatModes;
            SortieTarget target = combat != null
                ? KindOf(GameAccess.GetAiCurrentTarget(combat))
                : SortieTarget.None;

            tally.Add(SortieClassifier.Classify(target));
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
    }
}
