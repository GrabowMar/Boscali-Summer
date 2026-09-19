using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Runtime.Actions;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Host side of the CYBER network: the airbases behind Cyber Command and the gateways (and
    /// whether their anchor building stands), the vanilla vehicle behind each field site and
    /// the depot bay it leaves from, where it is and whether it lives, and everything the sites
    /// do to the world — early-warning reveals, SIGINT emitter reveals, the jammers' ECM
    /// umbrella against hostile radar seekers, and exposure of your emitters after a successful
    /// probe. The model (<see cref="CyberNetwork"/>) decides; this class only reads it and
    /// touches the game. Bounded: six airbase nodes and ten vehicles per faction, one airbase
    /// pass a second, one registry pass per pulse, at most 48 reveals per sweep.
    /// </summary>
    internal sealed class CyberDefense
    {
        private const float PulseInterval = 0.25f;
        private const float OriginInterval = 1f;
        private const float RadarInterval = 4f;
        private const float SigintInterval = 6f;
        private const float ExposureInterval = 5f;
        private const float ArrivalRadius = 150f;
        private const float StallSeconds = 12f;
        private const float StallGrace = 20f;
        private const float JamPulse = 0.2f;
        private const float TraceRevealRadius = 40000f;
        private const int MaximumDefeatedMemory = 256;

        private static readonly AccessTools.FieldRef<Missile, MissileSeeker> SeekerOf =
            AccessTools.FieldRefAccess<Missile, MissileSeeker>("seeker");
        private static readonly AccessTools.FieldRef<ARHSeeker, float> ActiveJam =
            AccessTools.FieldRefAccess<ARHSeeker, float>("jamAccumulation");
        private static readonly AccessTools.FieldRef<ARHSeeker, float> ActiveTolerance =
            AccessTools.FieldRefAccess<ARHSeeker, float>("jamTolerance");
        private static readonly AccessTools.FieldRef<SARHSeeker, float> SemiActiveJam =
            AccessTools.FieldRefAccess<SARHSeeker, float>("jamAccumulation");
        private static readonly AccessTools.FieldRef<SARHSeeker, float> SemiActiveTolerance =
            AccessTools.FieldRefAccess<SARHSeeker, float>("jamTolerance");
        private static readonly AccessTools.FieldRef<VehicleDepot, Transform> DepotBay =
            AccessTools.FieldRefAccess<VehicleDepot, Transform>("spawnTransform");

        private const float InfrastructureInterval = 1f;
        private const float BaySpacing = 10f;
        private const float BayReuseSeconds = 8f;

        private sealed class Site
        {
            public GroundVehicle Unit;
            public GlobalPosition Destination;
            public float OrderedAt;
            public float StillSince;
            public Vector3 LastPosition;
        }

        private sealed class Faction
        {
            public readonly Site[] Sites = new Site[CyberNetwork.SlotCount];
            public readonly List<FactionHQ> Origins = new List<FactionHQ>(CyberNetwork.MaximumOrigins);
            public float NextRadar, NextSigint, NextExposure;
            public int LoggedSerial;

            /// <summary>The airbase Cyber Command sits on; kept while the faction holds it so the root never wanders.</summary>
            public Airbase Root;
        }

        private readonly Dictionary<FactionHQ, Faction> factions = new Dictionary<FactionHQ, Faction>();
        private readonly HashSet<int> defeated = new HashSet<int>();
        private readonly List<FactionHQ> scratch = new List<FactionHQ>(8);
        private readonly List<Airbase> bases = new List<Airbase>(16);
        private static readonly Comparison<FactionHQ> ByName = (a, b) =>
            string.CompareOrdinal(Name(a), Name(b));
        private float nextPulse;
        private float nextOrigins;
        private float nextInfrastructure;
        private VehicleDepot lastBay;
        private float lastBayAt;
        private int bayQueue;

        // ---- Sites ---------------------------------------------------------------------------

        public bool Attach(FactionHQ hq, int slot, GroundVehicle unit, GlobalPosition destination, float now)
        {
            if (hq == null || unit == null || slot < 0 || slot >= CyberNetwork.SlotCount) return false;
            Faction faction = Get(hq);
            faction.Sites[slot] = new Site
            {
                Unit = unit,
                Destination = destination,
                OrderedAt = now,
                StillSince = now,
                LastPosition = unit.transform.position
            };
            return true;
        }

        public GroundVehicle UnitAt(FactionHQ hq, int slot)
        {
            Site site = SiteAt(hq, slot);
            return site != null && site.Unit != null && !site.Unit.disabled ? site.Unit : null;
        }

        /// <summary>Send a site's vehicle to a new mark.</summary>
        public bool Redirect(FactionHQ hq, int slot, GlobalPosition destination, float now)
        {
            Site site = SiteAt(hq, slot);
            if (site == null || site.Unit == null || site.Unit.disabled || site.Unit.UnitCommand == null) return false;
            site.Destination = destination;
            site.OrderedAt = now;
            site.StillSince = now;
            site.Unit.UnitCommand.SetDestination(destination, false);
            return true;
        }

        /// <summary>
        /// Where a site truck starts: the spawn bay of the faction's working vehicle depot nearest
        /// the mark, facing out of the bay; the nearest owned airbase only when the faction has no
        /// depot. Trucks ordered back to back from one bay are spaced so they never spawn inside
        /// each other.
        /// </summary>
        public bool TryStart(Player player, Vector3 mark, VehicleDefinition definition, out Vector3 point,
                             out Quaternion rotation, out string from)
        {
            point = default;
            rotation = Quaternion.identity;
            from = null;
            FactionHQ hq = player != null ? player.HQ : null;
            if (hq == null) return false;

            if (hq.TryGetNearestDepot(mark, float.MaxValue, out VehicleDepot depot) && depot != null &&
                !depot.disabled && depot.NetworkHQ == hq)
            {
                Transform bay = DepotBay(depot);
                if (bay == null) bay = depot.transform;
                float now = Time.unscaledTime;
                bayQueue = depot == lastBay && now - lastBayAt < BayReuseSeconds ? bayQueue + 1 : 0;
                lastBay = depot;
                lastBayAt = now;
                rotation = bay.rotation;
                float lift = definition != null ? definition.spawnOffset.y : 0f;
                point = bay.position + Vector3.up * lift + bay.right * (BaySpacing * (bayQueue % 4));
                from = string.IsNullOrEmpty(depot.name) ? "VEHICLE DEPOT" : depot.name;
                return true;
            }

            Airbase airbase = SupportTargeting.NearestOwnedAirbase(player, mark, out _);
            if (airbase == null) return false;
            point = airbase.center != null ? airbase.center.position : airbase.transform.position;
            Vector3 facing = mark - point;
            facing.y = 0f;
            rotation = facing.sqrMagnitude > 1f ? Quaternion.LookRotation(facing.normalized) : Quaternion.identity;
            from = airbase.name;
            return true;
        }

        /// <summary>Forget a site; despawn its vehicle when it was scrapped rather than killed.</summary>
        public void Remove(FactionHQ hq, int slot, bool despawn)
        {
            Site site = SiteAt(hq, slot);
            if (site == null) return;
            factions[hq].Sites[slot] = null;
            if (!despawn || site.Unit == null) return;
            Unit unit = site.Unit;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner != null && spawner.IsServer) spawner.ServerObjectManager.Destroy(unit.gameObject);
            else if (unit.IsServer) UnityEngine.Object.Destroy(unit.gameObject);
        }

        // ---- Origins -------------------------------------------------------------------------

        /// <summary>The enemy faction an incident's origin byte names for this defender.</summary>
        public FactionHQ Origin(FactionHQ defender, int index)
        {
            if (defender == null || !factions.TryGetValue(defender, out Faction faction)) return null;
            return index >= 0 && index < faction.Origins.Count ? faction.Origins[index] : null;
        }

        public int OriginIndex(FactionHQ defender, FactionHQ attacker)
        {
            if (defender == null || attacker == null || !factions.TryGetValue(defender, out Faction faction)) return -1;
            return faction.Origins.IndexOf(attacker);
        }

        /// <summary>Up to <paramref name="names"/>.Length enemy faction names in origin order.</summary>
        public int OriginNames(FactionHQ defender, string[] names)
        {
            if (names == null || defender == null || !factions.TryGetValue(defender, out Faction faction)) return 0;
            int count = Math.Min(names.Length, faction.Origins.Count);
            for (int i = 0; i < count; i++) names[i] = Name(faction.Origins[i]);
            return count;
        }

        /// <summary>
        /// Host: an enemy player's operation landed. The attacker's own adversaries grow warier;
        /// every other network with an ear on the point hears it.
        /// </summary>
        public void ReportOperation(SpaceOperations space, FactionHQ attacker, GlobalPosition target, double now)
        {
            if (space == null || attacker == null) return;
            space.CyberFor(attacker)?.NoteOffensive();
            for (int i = 0; i < space.FactionCount; i++)
            {
                FactionHQ defender = space.FactionAt(i);
                if (defender == null || defender == attacker) continue;
                CyberNetwork network = space.CyberFor(defender);
                if (network == null || !network.HasCommand) continue;
                int index = OriginIndex(defender, attacker);
                if (index >= 0) network.ReportHostile((byte)index, target.x, target.z, now);
            }
        }

        // ---- Tick ----------------------------------------------------------------------------

        /// <summary>Host, before the model ticks: the airbase infrastructure, vehicle positions,
        /// arrivals and losses, and the origin tables the campaign draws from.
        /// <paramref name="infrastructure"/> is false while the host has spectrum defence off.</summary>
        public void Sync(SpaceOperations space, double now, float unscaledNow, bool infrastructure)
        {
            if (infrastructure && unscaledNow >= nextInfrastructure)
            {
                nextInfrastructure = unscaledNow + InfrastructureInterval;
                SyncInfrastructure(space, now);
            }
            if (unscaledNow >= nextOrigins)
            {
                nextOrigins = unscaledNow + OriginInterval;
                RefreshOrigins(space);
            }

            foreach (KeyValuePair<FactionHQ, Faction> entry in factions)
            {
                CyberNetwork network = space.CyberFor(entry.Key);
                if (network == null) continue;
                Site[] sites = entry.Value.Sites;
                for (int slot = 0; slot < sites.Length; slot++)
                {
                    Site site = sites[slot];
                    if (site == null) continue;
                    if (!network.Exists(slot))
                    {
                        sites[slot] = null;
                        continue;
                    }
                    if (site.Unit == null || site.Unit.disabled)
                    {
                        network.MarkLost(slot, now);
                        sites[slot] = null;
                        continue;
                    }
                    Vector3 position = site.Unit.transform.position;
                    if ((position - site.LastPosition).sqrMagnitude > 4f)
                    {
                        site.LastPosition = position;
                        site.StillSince = unscaledNow;
                    }
                    GlobalPosition global = position.ToGlobalPosition();
                    float dx = global.x - site.Destination.x, dz = global.z - site.Destination.z;
                    bool arrived = dx * dx + dz * dz <= ArrivalRadius * ArrivalRadius ||
                                   (unscaledNow - site.OrderedAt > StallGrace && unscaledNow - site.StillSince > StallSeconds);
                    network.SetPosition(slot, global.x, global.z, arrived);
                }
            }
        }

        /// <summary>Host, after the model ticks: world effects and notices.</summary>
        public void Apply(SpaceOperations space, double now, float unscaledNow, ManualLogSource logger)
        {
            foreach (KeyValuePair<FactionHQ, Faction> entry in factions)
            {
                CyberNetwork network = space.CyberFor(entry.Key);
                if (network == null) continue;
                Announce(space, entry.Key, entry.Value, network, now, logger);
                if (!network.HasCommand) continue;
                Sweep(entry.Key, entry.Value, network, now, unscaledNow, logger);
            }

            if (unscaledNow < nextPulse) return;
            nextPulse = unscaledNow + PulseInterval;
            Umbrella(space, now);
        }

        public void Clear()
        {
            factions.Clear();
            defeated.Clear();
            bases.Clear();
            nextPulse = 0f;
            nextOrigins = 0f;
            nextInfrastructure = 0f;
            lastBay = null;
            lastBayAt = 0f;
            bayQueue = 0;
        }

        // ---- Airbase infrastructure ------------------------------------------------------------

        /// <summary>
        /// Every faction's airbase nodes follow the bases it holds: Cyber Command on the base
        /// nearest the centre of its holdings (kept there while it stands), gateways on the next
        /// nearest bases. A node is down while its anchor building — the map tower, else the
        /// first building of the base — is destroyed, and back when the game repairs it.
        /// Carriers and other attached airbases are never nodes.
        /// </summary>
        private void SyncInfrastructure(SpaceOperations space, double now)
        {
            for (int i = 0; i < space.FactionCount; i++)
            {
                FactionHQ hq = space.FactionAt(i);
                CyberNetwork network = space.CyberFor(hq);
                if (hq == null || network == null) continue;
                Faction faction = Get(hq);
                CollectBases(hq);

                network.BeginInfrastructure();
                Airbase root = PickRoot(faction);
                if (root != null)
                {
                    Report(network, root, CyberSiteKind.Command, now);
                    Vector3 origin = Centre(root);
                    // Nearest bases to Cyber Command first; the list is tiny, so select rather than sort.
                    for (int g = 0; g < CyberSites.MaximumGateways; g++)
                    {
                        int best = -1;
                        float bestDistance = float.MaxValue;
                        for (int b = 0; b < bases.Count; b++)
                        {
                            if (bases[b] == null) continue;
                            float distance = (Centre(bases[b]) - origin).sqrMagnitude;
                            if (distance >= bestDistance) continue;
                            best = b;
                            bestDistance = distance;
                        }
                        if (best < 0) break;
                        Report(network, bases[best], CyberSiteKind.Gateway, now);
                        bases[best] = null;
                    }
                }
                network.EndInfrastructure(now);
            }
            bases.Clear();
        }

        private void CollectBases(FactionHQ hq)
        {
            bases.Clear();
            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.AttachedAirbase || airbase.CurrentHQ != hq) continue;
                bases.Add(airbase);
            }
        }

        /// <summary>The root stays on its base while the faction holds it; otherwise the base
        /// nearest the centre of the holdings takes over. Removed from <see cref="bases"/>.</summary>
        private Airbase PickRoot(Faction faction)
        {
            int keep = faction.Root != null ? bases.IndexOf(faction.Root) : -1;
            if (keep < 0)
            {
                if (bases.Count == 0)
                {
                    faction.Root = null;
                    return null;
                }
                Vector3 centre = Vector3.zero;
                for (int b = 0; b < bases.Count; b++) centre += Centre(bases[b]);
                centre /= bases.Count;
                float bestDistance = float.MaxValue;
                for (int b = 0; b < bases.Count; b++)
                {
                    float distance = (Centre(bases[b]) - centre).sqrMagnitude;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    keep = b;
                }
            }
            faction.Root = bases[keep];
            bases[keep] = null;
            return faction.Root;
        }

        private static void Report(CyberNetwork network, Airbase airbase, CyberSiteKind kind, double now)
        {
            GlobalPosition at = Centre(airbase).ToGlobalPosition();
            Unit anchor = Anchor(airbase);
            bool down = airbase.disabled || (anchor != null && anchor.disabled);
            network.ReportInfrastructure(airbase.GetInstanceID(), kind, at.x, at.z, down, now);
        }

        /// <summary>The building a node lives in: the map tower, else the first building of the base.</summary>
        private static Unit Anchor(Airbase airbase)
        {
            if (airbase.MapTower != null) return airbase.MapTower;
            List<Building> buildings = airbase.buildings;
            if (buildings == null) return null;
            for (int i = 0; i < buildings.Count; i++)
                if (buildings[i] != null) return buildings[i];
            return null;
        }

        private static Vector3 Centre(Airbase airbase) =>
            airbase.center != null ? airbase.center.position : airbase.transform.position;

        private void RefreshOrigins(SpaceOperations space)
        {
            for (int i = 0; i < space.FactionCount; i++)
            {
                FactionHQ hq = space.FactionAt(i);
                CyberNetwork network = space.CyberFor(hq);
                if (hq == null || network == null || !network.HasCommand) continue;
                Faction faction = Get(hq);
                scratch.Clear();
                foreach (FactionHQ other in FactionRegistry.GetAllHQs())
                    if (other != null && other != hq) scratch.Add(other);
                scratch.Sort(ByName);
                // Keep existing indices stable so an incident never renames its origin mid-flight.
                for (int s = 0; s < scratch.Count && faction.Origins.Count < CyberNetwork.MaximumOrigins; s++)
                    if (!faction.Origins.Contains(scratch[s])) faction.Origins.Add(scratch[s]);
                network.OriginCount = faction.Origins.Count;
            }
        }

        private void Announce(SpaceOperations space, FactionHQ hq, Faction faction, CyberNetwork network, double now,
                              ManualLogSource logger)
        {
            int fresh = Math.Min(network.NoticeSerial - faction.LoggedSerial, network.NoticeCount);
            faction.LoggedSerial = network.NoticeSerial;
            for (int age = fresh - 1; age >= 0; age--)
            {
                CyberNotice notice = network.NoticeKind(age);
                int slot = network.NoticeSite(age);
                FactionHQ origin = Origin(hq, network.NoticeOrigin(age));
                string line = CyberWords.Notice(notice, CyberWords.Callsign(network, slot), Name(origin));
                if (line != null) logger?.LogInfo("[Support] " + Name(hq) + " CYBER: " + line + ".");
                if (notice != CyberNotice.TraceComplete) continue;

                space.ProgramsFor(hq)?.Grant(OpsReserve.Intel, 1);
                GlobalPosition centre = slot >= 0
                    ? new GlobalPosition(network.Site(slot).X, 0f, network.Site(slot).Z)
                    : default;
                try
                {
                    ReconAction.Reveal(hq, centre, TraceRevealRadius, logger, RevealFilter.Emitters, quiet: true);
                }
                catch (Exception e)
                {
                    logger?.LogWarning("[Support] Trace reveal failed: " + e.Message);
                }
            }
        }

        private static void Sweep(FactionHQ hq, Faction faction, CyberNetwork network, double now, float unscaledNow,
                                  ManualLogSource logger)
        {
            bool radar = unscaledNow >= faction.NextRadar;
            bool sigint = unscaledNow >= faction.NextSigint;
            bool exposure = unscaledNow >= faction.NextExposure;
            if (radar) faction.NextRadar = unscaledNow + RadarInterval;
            if (sigint) faction.NextSigint = unscaledNow + SigintInterval;
            if (exposure) faction.NextExposure = unscaledNow + ExposureInterval;
            if (!radar && !sigint && !exposure) return;

            FactionHQ exposedTo = null;
            if (exposure && network.ExposedUntil > now && faction.Origins.Count > network.ExposedOrigin)
                exposedTo = faction.Origins[network.ExposedOrigin];

            for (int slot = 0; slot < CyberNetwork.SlotCount; slot++)
            {
                Site site = faction.Sites[slot];
                if (site == null || site.Unit == null || site.Unit.disabled) continue;
                CyberSiteKind kind = network.Site(slot).Kind;
                float scale = network.EffectScale(slot, now);
                GlobalPosition at = site.Unit.transform.position.ToGlobalPosition();
                try
                {
                    if (radar && kind == CyberSiteKind.EarlyWarning && scale > 0f)
                        ReconAction.Reveal(hq, at, CyberSites.Info(kind).EffectRadius * scale, logger,
                            RevealFilter.Air, quiet: true);
                    if (sigint && kind == CyberSiteKind.Sigint && scale > 0f)
                        ReconAction.Reveal(hq, at, CyberSites.Info(kind).EffectRadius * scale, logger,
                            RevealFilter.Emitters, quiet: true);
                    if (exposedTo != null && network.EmissionOf(slot) != Emission.Silent)
                        exposedTo.RpcUpdateTrackingInfo(site.Unit.persistentID);
                }
                catch (Exception e)
                {
                    logger?.LogWarning("[Support] CYBER site sweep failed: " + e.Message);
                }
            }
        }

        /// <summary>
        /// The ECM umbrella: hostile active and semi-active radar seekers homing on a unit inside
        /// a working jammer's bubble accumulate jamming; past their tolerance they lose the
        /// return and fall back to the datalink, which is what breaks the lock. Only seekers
        /// this process simulates are touched (AI missiles on the host).
        /// </summary>
        private void Umbrella(SpaceOperations space, double now)
        {
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null || factions.Count == 0) return;
            if (defeated.Count > MaximumDefeatedMemory) defeated.Clear();

            for (int i = 0; i < units.Count; i++)
            {
                if (!(units[i] is Missile missile) || missile == null || missile.disabled) continue;
                MissileSeeker seeker = SeekerOf(missile);
                bool active = seeker is ARHSeeker;
                if (!active && !(seeker is SARHSeeker)) continue;
                if (!missile.targetID.TryGetUnit(out Unit target) || target == null || target.disabled) continue;
                FactionHQ defender = target.NetworkHQ;
                if (defender == null || defender == missile.NetworkHQ) continue;
                if (!factions.TryGetValue(defender, out Faction faction)) continue;
                CyberNetwork network = space.CyberFor(defender);
                if (network == null) continue;

                float strength = Cover(faction, network, target.transform.position, now, out int jammer);
                if (strength <= 0f) continue;

                float jam, tolerance;
                if (active)
                {
                    var arh = (ARHSeeker)seeker;
                    tolerance = ActiveTolerance(arh);
                    jam = ActiveJam(arh) = Mathf.Clamp01(ActiveJam(arh) + JamPulse * strength);
                }
                else
                {
                    var sarh = (SARHSeeker)seeker;
                    tolerance = SemiActiveTolerance(sarh);
                    jam = SemiActiveJam(sarh) = Mathf.Clamp01(SemiActiveJam(sarh) + JamPulse * strength);
                }
                if (jam > tolerance && defeated.Add(missile.GetInstanceID()))
                    network.NoteSeekerDefeated(jammer, now);
            }
        }

        /// <summary>Strongest umbrella over a point: mode × site health × distance falloff.</summary>
        private static float Cover(Faction faction, CyberNetwork network, Vector3 point, double now, out int slotUsed)
        {
            float best = 0f;
            slotUsed = -1;
            for (int slot = 0; slot < CyberNetwork.SlotCount; slot++)
            {
                CyberSite model = network.Site(slot);
                if (model.Kind != CyberSiteKind.Jammer) continue;
                Site site = faction.Sites[slot];
                if (site == null || site.Unit == null || site.Unit.disabled) continue;
                float umbrella = EwPostures.Umbrella(model.Mode) * network.EffectScale(slot, now);
                if (umbrella <= 0f) continue;
                float radius = CyberSites.Info(CyberSiteKind.Jammer).EffectRadius;
                Vector3 offset = point - site.Unit.transform.position;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > radius) continue;
                float strength = umbrella * (1f - 0.5f * distance / radius);
                if (strength <= best) continue;
                best = strength;
                slotUsed = slot;
            }
            return best;
        }

        private Faction Get(FactionHQ hq)
        {
            if (!factions.TryGetValue(hq, out Faction faction))
            {
                faction = new Faction();
                factions.Add(hq, faction);
            }
            return faction;
        }

        private Site SiteAt(FactionHQ hq, int slot) =>
            hq != null && slot >= 0 && slot < CyberNetwork.SlotCount && factions.TryGetValue(hq, out Faction faction)
                ? faction.Sites[slot]
                : null;

        internal static string Name(FactionHQ hq) =>
            hq != null && hq.faction != null && !string.IsNullOrEmpty(hq.faction.factionName)
                ? hq.faction.factionName.ToUpperInvariant()
                : "UNKNOWN ACTOR";
    }
}
