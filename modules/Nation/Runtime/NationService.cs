using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Nation.Configuration;
using BoscaliSummer.Modules.Nation.Domain;
using BoscaliSummer.Modules.Nation.Networking;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Nation.Runtime
{
    /// <summary>
    /// The nation layer's owner. Host: one <see cref="NationBook"/> and one <see cref="PoliticsBook"/> per faction over the vanilla economy
    /// (the game's factory buildings, its stock dictionaries and faction funds), ticked once a second; the staff runs every faction every 30 s.
    /// Client: a mirror of the local faction's snapshot. Screens read <see cref="INationView"/>, the convoy owners <see cref="INationModifiers"/>.
    /// </summary>
    internal sealed partial class NationService : MonoBehaviour, ISceneService, INationView, INationModifiers
    {
        private static readonly string[] Missions = { "Escalation", "Terminal Control", "Domination", "WarBox" };
        private static readonly float[] SiteRings = { 1500f, 2000f, 2600f };
        private const int SiteBearings = 12;
        private const int MissionPlants = 12;
        private const string PlantName = "Boscali_Nation_Plant_";

        private sealed class PlantRef
        {
            public Building Unit;
            public Factory Factory;
            public float Baseline;
        }

        private sealed class Side
        {
            public FactionHQ Owner;
            public readonly NationBook Book = new NationBook();
            public readonly PoliticsBook Politics = new PoliticsBook();
            public readonly List<UnitDefinition> Rows = new List<UnitDefinition>(NationRules.MaxStockRows);
            public readonly int[] Stock = new int[NationRules.MaxStockRows], Target = new int[NationRules.MaxStockRows];
            public readonly bool[] Air = new bool[NationRules.MaxStockRows];
            /// <summary>Vehicles made and not yet sent: a convoy draws from here (the game's own vehicle stock deploys at once, so it cannot hold them).</summary>
            public readonly int[] Reserve = new int[NationRules.MaxStockRows];
            public readonly Dictionary<int, PlantRef> Plants = new Dictionary<int, PlantRef>(NationRules.MaxPlants);
            public readonly NationView View = new NationView();
            public int FreeBuilds;
            public Vector3 Site;      // GLOBAL metres, valid while Book.Building
            public Quaternion SiteRotation = Quaternion.identity;
            public float Charged;
            public float LastFunds = -1f, NextIncome;
            public NationOutcome LastRefusal;
            public int Initial = -1;
            /// <summary>GENERAL OFFENSIVE's objective while it runs (taking it pays war support back).</summary>
            public Airbase OffensiveAt;
            /// <summary>The airbase this side lost last (COUNTERATTACK).</summary>
            public Airbase LastLost;
            /// <summary>The enemy plant our SABOTAGE stopped (destroying it pays), until when.</summary>
            public int SabotagedPlant;
            public float SabotagedUntil;
        }

        private static NationService live;
        private readonly Dictionary<FactionHQ, Side> sides = new Dictionary<FactionHQ, Side>(NationRules.MaxFactions);
        private readonly HashSet<Factory> managed = new HashSet<Factory>();
        private readonly List<NationEvent> events = new List<NationEvent>(8);
        private readonly List<KeyValuePair<UnitDefinition, int>> seed = new List<KeyValuePair<UnitDefinition, int>>(32);
        private readonly List<int> gone = new List<int>(4);
        private readonly NationView mirror = new NationView();
        private NationSettings settings;
        private NationNet net;
        private PhasedGate clock, staffGate, adoptGate;
        private BuildingDefinition factoryDefinition;
        private bool factorySearched, mirrorReady, layerOn, adoptNow;
        private int serial, stage;
        private float nextLayerCheck;

        public NationOutcome LastOutcome { get; private set; }
        public float LastOutcomeAt { get; private set; }

        internal void Configure(NationSettings nationSettings, NationNet network)
        {
            settings = nationSettings;
            net = network;
            live = this;
        }

        /// <summary>True for a factory whose production this layer does itself (see <c>NationFactoryPatch</c>).</summary>
        internal static bool Manages(Factory factory) => live != null && factory != null && live.managed.Contains(factory);

        public void ResetForScene()
        {
            sides.Clear();
            managed.Clear();
            events.Clear();
            owners.Clear();
            mirror.Plants.Clear();
            mirror.Stock.Clear();
            mirror.Moves.Clear();
            mirrorReady = layerOn = factorySearched = adoptNow = false;
            factoryDefinition = null;
            nextLayerCheck = 0f;
            clock.Reset(); staffGate.Reset(); adoptGate.Reset();
            ClosePresented();
            newsReady = false;
            newsPosted = 0;
        }

        private void OnDestroy()
        {
            ReleasePolicy();
            ResetForScene();
            if (live == this) live = null;
        }

        // ---- The contracts ---------------------------------------------------------------------------

        public bool Active => layerOn && Local != null;

        public NationView Local
        {
            get
            {
                if (!layerOn) return null;
                if (!GameAccess.IsServer()) return mirrorReady ? mirror : null;
                return GameAccess.TryGetLocalFaction(out FactionHQ hq) && sides.TryGetValue(hq, out Side side) && side.Rows.Count > 0 ? side.View : null;
            }
        }

        public NationOutcome Request(NationVerb verb, int a = 0, int b = 0)
        {
            if (!layerOn) return Answer(NationOutcome.Off);
            if (!GameAccess.IsServer()) return net != null && net.SendIntent(verb, a, b) ? NationOutcome.Ok : Answer(NationOutcome.Off);
            if (!GameAccess.TryGetLocalFaction(out FactionHQ hq)) return Answer(NationOutcome.NotMember);
            GameManager.GetLocalPlayer(out Player player);
            return Answer(Apply(hq, player, verb, a, b));
        }

        /// <summary>Records an answer for the local player (the host's own, or the host's reply on a client).</summary>
        internal NationOutcome Answer(NationOutcome outcome)
        {
            LastOutcome = outcome;
            LastOutcomeAt = Time.unscaledTime;
            return outcome;
        }

        public bool CanSupply(FactionHQ hq, Faction.ConvoyGroup group) => Covers(hq, group);

        // ---- Convoys draw on the vehicle reserve -------------------------------------------------------

        private static bool Row(Side side, UnitDefinition type, out int row)
        {
            for (row = 0; row < side.Rows.Count; row++) if (!side.Air[row] && side.Rows[row] == type) return true;
            row = -1;
            return false;
        }

        /// <summary>True when the reserve holds every vehicle of the group, and wherever the layer does not run this faction.</summary>
        internal static bool Covers(FactionHQ hq, Faction.ConvoyGroup group)
        {
            if (live == null || !live.layerOn || hq == null || group?.Constituents == null || !live.sides.TryGetValue(hq, out Side side) || side.Rows.Count == 0) return true;
            foreach (Faction.ConvoyUnit unit in group.Constituents)
                if (unit?.Type != null && Row(side, unit.Type, out int row) && side.Reserve[row] < unit.Count) return false;
            return true;
        }

        /// <summary>The group leaves: its vehicles come out of the reserve (never below zero, so a free or scripted convoy still goes).</summary>
        internal static void Draw(FactionHQ hq, Faction.ConvoyGroup group)
        {
            if (live == null || !live.layerOn || hq == null || group?.Constituents == null || !live.sides.TryGetValue(hq, out Side side)) return;
            foreach (Faction.ConvoyUnit unit in group.Constituents)
                if (unit?.Type != null && Row(side, unit.Type, out int row)) side.Reserve[row] = Mathf.Max(0, side.Reserve[row] - unit.Count);
            side.Book.Dirty = true;
        }

        internal static void Refuse(Player player, NationOutcome outcome)
        {
            if (live != null && live.net != null) live.net.Reply(player, outcome);
        }

        // ---- Client mirror ---------------------------------------------------------------------------

        internal void Receive(byte[] payload, int count)
        {
            if (GameAccess.IsServer()) return;
            mirrorReady = NationWire.TryUnpack(payload, count, mirror);
        }

        private void AgeMirror(float dt)
        {
            if (!mirrorReady) return;
            mirror.EconomyLock = Mathf.Max(0f, mirror.EconomyLock - dt);
            mirror.PriorityLock = Mathf.Max(0f, mirror.PriorityLock - dt);
            mirror.BuildLeft = Mathf.Max(0f, mirror.BuildLeft - dt);
            mirror.CabinetLeft = Mathf.Max(0f, mirror.CabinetLeft - dt);
            mirror.EnemyDecidedLeft = Mathf.Max(0f, mirror.EnemyDecidedLeft - dt);
            mirror.DecidedAge += dt;
            mirror.EnemyDecidedAge += dt;
            if (mirror.DoctrineAge >= 0f) mirror.DoctrineAge += dt;
            for (int i = 0; i < Decisions.Count; i++)
            {
                mirror.Cooldown[i] = Mathf.Max(0f, mirror.Cooldown[i] - dt);
                mirror.InForce[i] = Mathf.Max(0f, mirror.InForce[i] - dt);
            }
            for (int i = 0; i < mirror.Moves.Count; i++)
            {
                SupportMove m = mirror.Moves[i];
                m.Age += dt;
                mirror.Moves[i] = m;
            }
            for (int i = 0; i < mirror.Plants.Count; i++)
            {
                PlantView p = mirror.Plants[i];
                if (p.NextIn <= 0f) continue;
                p.NextIn = Mathf.Max(0f, p.NextIn - dt);
                mirror.Plants[i] = p;
            }
        }

        // ---- Tick ------------------------------------------------------------------------------------

        private void Update()
        {
            if (Time.unscaledTime >= nextLayerCheck)
            {
                nextLayerCheck = Time.unscaledTime + 1f;
                bool on = settings != null && settings.Enabled.Value && MissionManager.IsRunning && LayerMission(MissionManager.CurrentMission?.Name);
                if (!on && layerOn) { ReleasePolicy(); ResetForScene(); }
                layerOn = on;
            }
            if (!layerOn) return;
            Present();
            if (!GameAccess.IsServer()) { AgeMirror(Time.deltaTime); return; }
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            if (mission == null) return;
            float now = mission.MissionTime;
            if (!float.IsFinite(now) || !clock.Due(now, 1f, 0.4375f)) return;
            try { HostTick(mission, now); }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Nation] tick failed: " + e.Message); }
        }

        internal static bool LayerMission(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            for (int i = 0; i < Missions.Length; i++)
                if (string.Equals(name, Missions[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void HostTick(MissionManager mission, float now)
        {
            stage = EscalationStage.Of(mission.currentEscalation, mission.tacticalThreshold, mission.strategicThreshold);
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            foreach (FactionHQ hq in hqs)
                if (hq != null && hq.faction != null && !sides.ContainsKey(hq) && sides.Count < NationRules.MaxFactions)
                    sides.Add(hq, new Side { Owner = hq });
            if (adoptGate.Due(now, 10f, 0.9375f, 1f) || adoptNow) { adoptNow = false; Adopt(); }
            PollAirbases(now);
            bool staff = staffGate.Due(now, 30f, 0.6875f, 1f);
            foreach (Side side in sides.Values)
            {
                if (side.Owner == null) continue;
                if (side.Rows.Count == 0 && !Seed(side)) continue;
                NationWorld w = Read(side);
                if (staff)
                {
                    if (side.FreeBuilds > 0 && StaffBuild(side, w, now, true)) side.FreeBuilds--;
                    if (AutoStaff.Run(side.Book, w, now)) StaffBuild(side, w, now, false);
                }
                events.Clear();
                side.Book.Boost = side.Politics.Output(now);
                side.Book.Tick(now, w, events);
                for (int i = 0; i < events.Count; i++) Effect(side, events[i]);
                Income(side, now);
                SyncFactories(side);
                Politics(side, w, now, staff);
                Fill(side, w, now);
            }
        }

        /// <summary>The faction's producible types and their targets (the mission's own stock), then the starting-stock cut, once.</summary>
        private bool Seed(Side side)
        {
            seed.Clear();
            FactionHQ hq = side.Owner;
            foreach (var pair in hq.AircraftSupply) if (pair.Key != null && pair.Value.Count > 0) seed.Add(new KeyValuePair<UnitDefinition, int>(pair.Key, pair.Value.Count));
            int aircraft = seed.Count;
            // Vehicle lines are what the faction's convoy groups are made of; a line's target is two of the largest group's worth.
            List<Faction.ConvoyGroup> groups = hq.faction != null ? hq.faction.GetConvoyGroups() : null;
            if (groups != null)
                foreach (Faction.ConvoyGroup group in groups)
                {
                    if (group?.Constituents == null) continue;
                    foreach (Faction.ConvoyUnit unit in group.Constituents)
                    {
                        if (unit?.Type == null || unit.Count <= 0 || unit.Type is AircraftDefinition) continue;
                        int at = -1;
                        for (int i = aircraft; i < seed.Count; i++) if (seed[i].Key == unit.Type) at = i;
                        if (at < 0) seed.Add(new KeyValuePair<UnitDefinition, int>(unit.Type, unit.Count * 2));
                        else if (unit.Count * 2 > seed[at].Value) seed[at] = new KeyValuePair<UnitDefinition, int>(unit.Type, unit.Count * 2);
                    }
                }
            if (seed.Count == 0) return false;
            float share = Mathf.Clamp(settings.StartingStock.Value, 0.25f, 1f);
            for (int i = 0; i < seed.Count && side.Rows.Count < NationRules.MaxStockRows; i++)
            {
                int row = side.Rows.Count;
                side.Rows.Add(seed[i].Key);
                side.Air[row] = i < aircraft;
                side.Target[row] = seed[i].Value;
                int keep = Mathf.Max(1, Mathf.CeilToInt(seed[i].Value * share));
                if (!side.Air[row]) side.Reserve[row] = keep;
                else if (keep < seed[i].Value) hq.ModifyUnitSupply(seed[i].Key, keep - seed[i].Value);
            }
            Plugin.Logger?.LogInfo("[Nation] " + hq.name + ": " + side.Rows.Count + " stock lines, starting stock " + Mathf.RoundToInt(share * 100f) + " %.");
            adoptNow = true;
            return true;
        }

        private NationWorld Read(Side side)
        {
            FactionHQ hq = side.Owner;
            for (int i = 0; i < side.Rows.Count; i++) side.Stock[i] = side.Air[i] ? hq.GetUnitSupply(side.Rows[i]) : side.Reserve[i];
            int held = 0;
            foreach (Airbase airbase in hq.GetAirbases())
                if (airbase != null && !airbase.disabled && !airbase.AttachedAirbase) held++;
            float stability = 50f;
            if (ModuleServices.TryGet(out IFactionMoraleView morale) && morale.TryGetMorale(hq.GetInstanceID(), out float value)) stability = value;
            return new NationWorld
            {
                Stock = side.Stock, Target = side.Target, Aircraft = side.Air, Rows = side.Rows.Count, HeldAirbases = held,
                Funds = hq.factionFunds, Stability = stability, Escalation = stage, InitialPlants = side.Initial > 0 ? side.Initial : 0,
                FactoryCost = settings.FactoryCost.Value, BuildSeconds = NationRules.FactoryBuildSeconds,
            };
        }

        /// <summary>Drops dead or captured plants and adopts every live production factory building, mission-placed or built here.</summary>
        private void Adopt()
        {
            foreach (Side side in sides.Values)
            {
                gone.Clear();
                foreach (var pair in side.Plants)
                    if (pair.Value.Unit == null || pair.Value.Unit.disabled || pair.Value.Unit.NetworkHQ != side.Owner) gone.Add(pair.Key);
                foreach (int id in gone)
                {
                    managed.Remove(side.Plants[id].Factory);
                    side.Plants.Remove(id);
                    side.Book.RemovePlant(id);
                }
            }
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                if (!(units[i] is Building building) || building.disabled || building.NetworkHQ == null) continue;
                if (!sides.TryGetValue(building.NetworkHQ, out Side side) || side.Plants.Count >= NationRules.MaxPlants) continue;
                int id = building.GetInstanceID();
                if (side.Plants.ContainsKey(id) || !building.TryGetComponent(out Factory factory)) continue;
                // Big missions place more factories than the layer runs: it takes the first MissionPlants of them (the rest stay the game's own),
                // which leaves room under the cap for the ones a faction builds.
                bool ours = building.NetworkUniqueName != null && building.NetworkUniqueName.StartsWith(PlantName, StringComparison.Ordinal);
                if (!ours && side.Plants.Count >= MissionPlants) continue;
                // A warhead plant (no unit, an interval) stays the game's own.
                if (factory.ProductionUnit == null && factory.ProductionInterval > 0f) continue;
                side.Plants.Add(id, new PlantRef { Unit = building, Factory = factory, Baseline = Mathf.Max(1f, Health(building)) });
                side.Book.AddPlant(id);
                managed.Add(factory);
            }
            // The first census is what the mission gave: those plants never count against the slots.
            foreach (Side side in sides.Values) if (side.Initial < 0 && side.Rows.Count > 0) side.Initial = side.Plants.Count;
        }

        private static float Health(Unit unit)
        {
            List<UnitPart> parts = unit != null ? unit.GetAllParts() : null;
            if (parts == null || parts.Count == 0 || parts.Count > 256) return 0f;
            float total = 0f;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null && !parts[i].IsDetached() && float.IsFinite(parts[i].hitPoints)) total += Mathf.Max(0f, parts[i].hitPoints);
            return float.IsFinite(total) ? total : 0f;
        }

        // ---- Effects ---------------------------------------------------------------------------------

        private void Effect(Side side, NationEvent e)
        {
            FactionHQ hq = side.Owner;
            switch (e.Kind)
            {
                case NationEventKind.Produced:
                    if (e.B < 0 || e.B >= side.Rows.Count) break;
                    if (side.Air[e.B]) hq.AddSupplyUnit(side.Rows[e.B], 1);
                    else side.Reserve[e.B]++;
                    break;
                case NationEventKind.FactoryBuilt:
                    if (SpawnFactory(side)) break;
                    if (side.Charged > 0f) hq.AddFunds(side.Charged);
                    Plugin.Logger?.LogInfo("[Nation] " + hq.name + ": the factory site was lost; " + Mathf.RoundToInt(side.Charged) + " funds returned.");
                    break;
            }
        }

        // ponytail: income law as a correction on the treasury's own growth, once a minute. The game has no single income call to
        // scale, so a minute in which the faction spent more than it earned is simply not corrected; hook the real sources if that matters.
        private void Income(Side side, float now)
        {
            if (now < side.NextIncome) return;
            side.NextIncome = now + 60f;
            float funds = side.Owner.factionFunds;
            float gain = side.LastFunds < 0f ? 0f : funds - side.LastFunds;
            float bonus = gain > 0f ? gain * (NationRules.Income(side.Book.Economy) - 1f) : 0f;
            if (Mathf.Abs(bonus) > 0.5f) side.Owner.AddFunds(bonus);
            side.LastFunds = funds + bonus;
        }

        // ---- Building a factory ------------------------------------------------------------------------

        /// <summary>The staff's build, with the refusal said once per reason so a faction that cannot build is not a silent one.</summary>
        private bool StaffBuild(Side side, in NationWorld w, float now, bool free)
        {
            NationOutcome outcome = TryBuild(side, w, now, free);
            if (outcome != NationOutcome.Ok && outcome != NationOutcome.Busy && outcome != side.LastRefusal)
                Plugin.Logger?.LogInfo("[Nation] " + side.Owner.name + " staff cannot build a factory: " + outcome + " (" + side.Book.Plants.Count + " plants, " + w.HeldAirbases + " airbases).");
            side.LastRefusal = outcome;
            return outcome == NationOutcome.Ok;
        }

        private NationOutcome TryBuild(Side side, in NationWorld w, float now, bool free)
        {
            if (side.Book.Building) return NationOutcome.Busy;
            if (side.Book.Plants.Count >= NationRules.Slots(w.HeldAirbases, w.InitialPlants)) return NationOutcome.NoSlot;
            if (!FindSite(side, out Vector3 site, out Quaternion rotation)) return NationOutcome.NoSite;
            NationOutcome outcome = side.Book.StartBuild(now, w, free, out float charged);
            if (outcome != NationOutcome.Ok) return outcome;
            side.Site = site;
            side.SiteRotation = rotation;
            side.Charged = charged;
            if (charged > 0f) side.Owner.AddFunds(-charged);
            Plugin.Logger?.LogInfo("[Nation] " + side.Owner.name + " breaks ground on a factory (" + Mathf.RoundToInt(charged) + " funds).");
            return NationOutcome.Ok;
        }

        private BuildingDefinition FactoryDefinition()
        {
            if (factorySearched) return factoryDefinition;
            factorySearched = true;
            Encyclopedia book = GameAccess.EncyclopediaOrNull;
            if (book?.buildings == null) { factorySearched = false; return null; }
            foreach (BuildingDefinition definition in book.buildings)
            {
                if (definition == null || definition.unitPrefab == null || !definition.unitPrefab.TryGetComponent(out Factory factory)) continue;
                if (factory.factoryType != Factory.FactoryType.AircraftAndVehicles) continue;
                factoryDefinition = definition;
                break;
            }
            if (factoryDefinition == null) Plugin.Logger?.LogWarning("[Nation] the game has no factory building to place; BUILD FACTORY is off.");
            return factoryDefinition;
        }

        /// <summary>
        /// Dry level ground on a ring round the rear-most held land airbase (the one farthest from any airbase the faction does not hold).
        /// </summary>
        // ponytail: at most 36 footprint tests, only when a build starts; walk more rings if maps turn out cramped.
        private bool FindSite(Side side, out Vector3 global, out Quaternion rotation)
        {
            global = default;
            rotation = Quaternion.identity;
            BuildingDefinition definition = FactoryDefinition();
            if (definition == null) return false;
            Airbase rear = null;
            float best = -1f;
            foreach (Airbase own in side.Owner.GetAirbases())
            {
                if (own == null || own.disabled || own.AttachedAirbase || own.center == null) continue;
                float nearest = float.MaxValue;
                foreach (FactionHQ other in FactionRegistry.GetAllHQs())
                {
                    if (other == null || other == side.Owner) continue;
                    foreach (Airbase theirs in other.GetAirbases())
                        if (theirs != null && theirs.center != null)
                            nearest = Mathf.Min(nearest, (theirs.center.position - own.center.position).sqrMagnitude);
                }
                if (nearest > best) { best = nearest; rear = own; }
            }
            if (rear == null) return false;
            Vector3 origin = rear.center.position;
            for (int ring = 0; ring < SiteRings.Length; ring++)
                for (int step = 0; step < SiteBearings; step++)
                {
                    float angle = (step * 5 % SiteBearings) * Mathf.PI * 2f / SiteBearings;
                    Vector3 desired = origin + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * SiteRings[ring];
                    rotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
                    if (!Place(definition, desired, rotation, out Vector3 local)) continue;
                    GlobalPosition g = local.ToGlobalPosition();
                    global = new Vector3((float)g.x, (float)g.y, (float)g.z);
                    return true;
                }
            return false;
        }

        /// <summary>The footprint settled on terrain: centre and four corners dry and within 3 m of each other, nothing in the volume.</summary>
        private static bool Place(UnitDefinition definition, Vector3 desired, Quaternion rotation, out Vector3 position)
        {
            position = default;
            if (!GroundPlacement.DryGround(desired, out Vector3 center)) return false;
            var half = new Vector3(Mathf.Clamp(definition.width, 10f, 160f) * 0.5f + 2f, Mathf.Clamp(definition.height, 4f, 60f) * 0.5f,
                Mathf.Clamp(definition.length, 10f, 160f) * 0.5f + 2f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = center + rotation * new Vector3((i & 1) == 0 ? -half.x : half.x, 0f, (i & 2) == 0 ? -half.z : half.z);
                if (!GroundPlacement.DryGround(corner, out Vector3 hit) || Mathf.Abs(hit.y - center.y) > 3f) return false;
            }
            if (Physics.CheckBox(center + Vector3.up * (half.y + 3.2f), half, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            position = center;
            return true;
        }

        private bool SpawnFactory(Side side)
        {
            BuildingDefinition definition = FactoryDefinition();
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (definition == null || spawner == null || !spawner.IsServer) return false;
            Vector3 local = new GlobalPosition(side.Site.x, side.Site.y, side.Site.z).ToLocalPosition();
            Quaternion rotation = side.SiteRotation;
            // The ground may have been taken since the build began: look again before giving up.
            if (!Place(definition, local, rotation, out Vector3 point))
            {
                if (!FindSite(side, out Vector3 global, out rotation)) return false;
                point = new GlobalPosition(global.x, global.y, global.z).ToLocalPosition();
            }
            Building made = spawner.SpawnBuilding(definition.unitPrefab, point.ToGlobalPosition(), rotation, side.Owner, null,
                PlantName + side.Owner.GetInstanceID() + "_" + (++serial), false, null);
            if (made == null) return false;
            adoptNow = true;
            Plugin.Logger?.LogInfo("[Nation] " + side.Owner.name + " opens a factory at " + Mathf.RoundToInt(side.Site.x) + ", " + Mathf.RoundToInt(side.Site.z) + ".");
            return true;
        }

        // ---- What the game and the screens see -----------------------------------------------------------

        /// <summary>Keeps the factory's own replicated fields telling the truth, so the game's map tooltip shows the line.</summary>
        private static void SyncFactories(Side side)
        {
            foreach (Plant plant in side.Book.Plants)
            {
                if (!side.Plants.TryGetValue(plant.Id, out PlantRef reference) || reference.Factory == null) continue;
                bool makes = (plant.Kind == LineKind.Aircraft || plant.Kind == LineKind.Vehicle) && plant.Row >= 0 && plant.Row < side.Rows.Count;
                reference.Factory.NetworkproductionUnit = makes ? side.Rows[plant.Row] : null;
                reference.Factory.NetworkproductionInterval = makes ? plant.Interval : 0f;
                if (makes && !plant.Resting) reference.Factory.NetworklastProductionTime = plant.NextAt - plant.Interval;
            }
        }

        private void Fill(Side side, in NationWorld w, float now)
        {
            NationBook book = side.Book;
            NationView v = side.View;
            v.Slots = NationRules.Slots(w.HeldAirbases, w.InitialPlants);
            v.Output = NationRules.Output(book.Economy, w.Stability) * book.Boost;
            v.Stability = w.Stability;
            v.Funds = w.Funds;
            v.FactoryCost = w.FactoryCost;
            v.Economy = book.Economy;
            v.Priority = book.Priority;
            v.EconomyLock = Mathf.Max(0f, book.EconomyLockUntil - now);
            v.PriorityLock = Mathf.Max(0f, book.PriorityLockUntil - now);
            v.Building = book.Building;
            v.BuildLeft = book.Building ? Mathf.Max(0f, book.BuildEnd - now) : 0f;
            v.BuildTotal = book.Building ? book.BuildEnd - book.BuildStart : 0f;
            v.Escalation = w.Escalation;
            v.Plants.Clear();
            foreach (Plant plant in book.Plants)
            {
                var view = new PlantView
                {
                    Id = plant.Id, Kind = plant.Kind, Row = plant.Row, Pinned = plant.Pinned, Interval = plant.Interval, Raided = now < plant.RaidedUntil,
                    NextIn = plant.Resting || plant.NextAt <= 0f ? 0f : Mathf.Max(0f, plant.NextAt - now), Health01 = 1f,
                };
                if (side.Plants.TryGetValue(plant.Id, out PlantRef reference) && reference.Unit != null)
                {
                    view.Health01 = Mathf.Clamp01(Health(reference.Unit) / reference.Baseline);
                    GlobalPosition g = reference.Unit.transform.position.ToGlobalPosition();
                    view.X = (float)g.x;
                    view.Z = (float)g.z;
                }
                v.Plants.Add(view);
            }
            v.Stock.Clear();
            for (int i = 0; i < side.Rows.Count; i++)
                v.Stock.Add(new StockView
                {
                    Name = side.Rows[i].unitName ?? "", Code = side.Rows[i].code ?? "", Aircraft = side.Air[i],
                    Count = side.Stock[i], Target = side.Target[i],
                });
            FillPolitics(side, now);
        }

        // ---- Verbs (host) ----------------------------------------------------------------------------

        /// <summary>A faction member's request, judged by the books. <paramref name="player"/> null is the host's own console.</summary>
        internal NationOutcome Apply(FactionHQ hq, Player player, NationVerb verb, int a, int b)
        {
            if (!layerOn || !GameAccess.IsServer()) return NationOutcome.Off;
            if (hq == null || !sides.TryGetValue(hq, out Side side) || side.Rows.Count == 0) return NationOutcome.NotMember;
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            if (mission == null) return NationOutcome.Off;
            float now = mission.MissionTime;
            NationWorld w = Read(side);
            NationOutcome outcome;
            switch (verb)
            {
                case NationVerb.Economy: outcome = side.Book.SetEconomy((EconomyPolicy)(byte)a, now); break;
                case NationVerb.Priority: outcome = side.Book.SetPriority((PriorityPolicy)(byte)a, now); break;
                case NationVerb.Line:
                    outcome = side.Book.SetLine(a, b < 0 ? LineKind.Construction : LineKind.Aircraft, b, b >= 0 && b < side.Rows.Count && side.Air[b], side.Rows.Count);
                    break;
                case NationVerb.Unpin: outcome = side.Book.Unpin(a); break;
                case NationVerb.Build: outcome = TryBuild(side, w, now, false); break;
                case NationVerb.Answer: outcome = AnswerCard(side, a, b, NameOf(player), now); break;
                case NationVerb.Decide: outcome = Decide(side, a, NameOf(player), now); break;
                default: outcome = NationOutcome.Unknown; break;
            }
            if (outcome == NationOutcome.Ok) Fill(side, w, now);
            return outcome;
        }

        private static string NameOf(Player player)
        {
            if (player == null) return "HOST";
            try
            {
                string name = player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard) ?? "";
                int tag = name.IndexOf('<');
                while (tag >= 0)
                {
                    int end = name.IndexOf('>', tag);
                    if (end < 0) break;
                    name = name.Remove(tag, end - tag + 1);
                    tag = name.IndexOf('<');
                }
                name = name.Trim().ToUpperInvariant();
                return name.Length > 20 ? name.Substring(0, 20) : name;
            }
            catch (Exception) { return "PILOT"; }
        }

        // ---- For the net -----------------------------------------------------------------------------

        /// <summary>The faction's snapshot when it changed (or <paramref name="force"/>); clears the change mark.</summary>
        internal bool TryPack(FactionHQ hq, bool force, out byte[] bytes)
        {
            bytes = null;
            if (!layerOn || hq == null || !sides.TryGetValue(hq, out Side side) || side.Rows.Count == 0) return false;
            if (!force && !side.Book.Dirty && !side.Politics.Dirty) return false;
            side.Book.Dirty = side.Politics.Dirty = false;
            bytes = NationWire.Pack(side.View);
            return true;
        }
    }
}
