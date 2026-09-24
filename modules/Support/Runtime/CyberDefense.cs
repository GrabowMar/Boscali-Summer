using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Host side of the CYBER network: which airbases are home nodes and whether their anchor
    /// building stands, which map locations (enemy or neutral airfields, cities) each faction can
    /// breach, and the origin tables the adversary campaign draws from. Everything a location
    /// does to the world is an ability, executed by its action; the model
    /// (<see cref="CyberNetwork"/>) decides and this class only reads the scene. Bounded: one
    /// scene scan every two seconds, at most sixteen targets per faction, one airbase pass a
    /// second, at most eight home nodes.
    /// </summary>
    internal sealed class CyberDefense
    {
        private const float LocationInterval = 2f;
        private const float OriginInterval = 1f;
        private const float InfrastructureInterval = 1f;

        private sealed class Faction
        {
            public readonly List<FactionHQ> Origins = new List<FactionHQ>(CyberNetwork.MaximumOrigins);
            public int LoggedSerial;

            /// <summary>The airbase Cyber Command sits on; kept while the faction holds it.</summary>
            public Airbase Root;
        }

        private readonly struct SceneLocation
        {
            public readonly int Anchor;
            public readonly LocationKind Kind;
            public readonly float X, Z;

            public SceneLocation(int anchor, LocationKind kind, float x, float z)
            {
                Anchor = anchor;
                Kind = kind;
                X = x;
                Z = z;
            }
        }

        private readonly Dictionary<FactionHQ, Faction> factions = new Dictionary<FactionHQ, Faction>();
        private readonly List<FactionHQ> scratch = new List<FactionHQ>(8);
        private readonly List<Airbase> bases = new List<Airbase>(16);
        private readonly List<SceneLocation> scene = new List<SceneLocation>(CyberLocations.MaximumTargets);
        private readonly List<SceneLocation> candidates = new List<SceneLocation>(64);
        private static readonly Comparison<FactionHQ> ByName = (a, b) => string.CompareOrdinal(Name(a), Name(b));
        private float nextLocations;
        private float nextOrigins;
        private float nextInfrastructure;

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
        /// every other network with a stage-2 ear on the point hears it.
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

        /// <summary>Host, before the model ticks: home nodes, hackable locations and the origin
        /// tables the campaign draws from. <paramref name="enabled"/> is false while the host has
        /// spectrum defence off.</summary>
        public void Sync(SpaceOperations space, double now, float unscaledNow, bool enabled)
        {
            if (!enabled || space == null) return;
            if (unscaledNow >= nextLocations)
            {
                nextLocations = unscaledNow + LocationInterval;
                ScanScene();
            }
            if (unscaledNow >= nextOrigins)
            {
                nextOrigins = unscaledNow + OriginInterval;
                RefreshOrigins(space);
            }
            if (unscaledNow < nextInfrastructure) return;
            nextInfrastructure = unscaledNow + InfrastructureInterval;

            for (int i = 0; i < space.FactionCount; i++)
            {
                FactionHQ hq = space.FactionAt(i);
                CyberNetwork network = space.CyberFor(hq);
                if (hq == null || network == null) continue;
                Faction faction = Get(hq);
                CollectBases(hq);

                network.BeginInfrastructure();
                Airbase root = PickRoot(faction);
                if (root != null) Report(network, root, NodeKind.Command, now);
                for (int b = 0; b < bases.Count; b++)
                    if (bases[b] != null) Report(network, bases[b], NodeKind.Base, now);
                network.EndInfrastructure(now);

                network.BeginLocations();
                CollectTargets(hq, network, now);
                network.EndLocations(now);
                bases.Clear();
            }
        }

        /// <summary>Host, after the model ticks: the voice loop.</summary>
        public void Apply(SpaceOperations space, double now, float unscaledNow, ManualLogSource logger)
        {
            if (space == null) return;
            for (int i = 0; i < space.FactionCount; i++)
            {
                FactionHQ hq = space.FactionAt(i);
                CyberNetwork network = space.CyberFor(hq);
                if (hq == null || network == null) continue;
                Announce(space, hq, Get(hq), network, logger);
            }
        }

        public void Clear()
        {
            factions.Clear();
            bases.Clear();
            scene.Clear();
            candidates.Clear();
            nextLocations = 0f;
            nextOrigins = 0f;
            nextInfrastructure = 0f;
        }

        // ---- Home nodes ----------------------------------------------------------------------

        private void CollectBases(FactionHQ hq)
        {
            bases.Clear();
            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.AttachedAirbase || airbase.CurrentHQ != hq) continue;
                if (bases.Count >= CyberLocations.MaximumBases) break;
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

        private static void Report(CyberNetwork network, Airbase airbase, NodeKind kind, double now)
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

        // ---- Hackable locations ---------------------------------------------------------------

        /// <summary>Host, every two seconds: the city building sets in the loaded scene. The map
        /// dresses power lines, pylons, wind and solar farms and lighthouses with the same
        /// component, so only a set named for a city is a location.</summary>
        private void ScanScene()
        {
            scene.Clear();
            MapBuildingSet[] sets = Resources.FindObjectsOfTypeAll<MapBuildingSet>();
            if (sets == null) return;
            for (int i = 0; i < sets.Length && scene.Count < CyberLocations.MaximumTargets; i++)
            {
                MapBuildingSet set = sets[i];
                if (set == null || !set.gameObject.scene.IsValid() || !set.gameObject.activeInHierarchy) continue;
                if (!CyberLocations.CitySetName(set.name)) continue;
                GlobalPosition at = set.transform.position.ToGlobalPosition();
                int anchor = unchecked((int)set.NetId);
                if (anchor == 0) anchor = set.GetInstanceID();
                scene.Add(new SceneLocation(anchor, LocationKind.City, at.x, at.z));
            }
        }

        /// <summary>Every airfield the faction does not hold and every city, nearest Cyber
        /// Command first, capped at the target slots. A city inside an airbase's own footprint
        /// is that base, not a separate location: drawing both stacked their labels.</summary>
        private void CollectTargets(FactionHQ hq, CyberNetwork network, double now)
        {
            candidates.Clear();
            for (int i = 0; i < scene.Count; i++)
                if (!InsideAirbase(scene[i].X, scene[i].Z)) candidates.Add(scene[i]);
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.AttachedAirbase || airbase.CurrentHQ == hq) continue;
                GlobalPosition at = Centre(airbase).ToGlobalPosition();
                candidates.Add(new SceneLocation(airbase.GetInstanceID(), LocationKind.Airfield, at.x, at.z));
            }

            int command = network.CommandSlot;
            float cx = command >= 0 ? network.Node(command).X : 0f;
            float cz = command >= 0 ? network.Node(command).Z : 0f;
            candidates.Sort((a, b) =>
            {
                float da = (a.X - cx) * (a.X - cx) + (a.Z - cz) * (a.Z - cz);
                float db = (b.X - cx) * (b.X - cx) + (b.Z - cz) * (b.Z - cz);
                return da.CompareTo(db);
            });
            int count = Math.Min(candidates.Count, CyberLocations.MaximumTargets);
            for (int i = 0; i < count; i++)
            {
                SceneLocation location = candidates[i];
                network.ReportLocation(location.Anchor, location.Kind, location.X, location.Z, now);
            }
        }

        /// <summary>True when a point lies inside any airbase's capture footprint. A city set
        /// there is the base's own city; the airfield node already marks the place.</summary>
        private static bool InsideAirbase(float x, float z)
        {
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.AttachedAirbase) continue;
                float radius = Mathf.Max(airbase.GetRadius(), 1500f);
                Vector3 centre = Centre(airbase);
                float dx = centre.x - x, dz = centre.z - z;
                if (dx * dx + dz * dz <= radius * radius) return true;
            }
            return false;
        }

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

        private void Announce(SpaceOperations space, FactionHQ hq, Faction faction, CyberNetwork network,
                              ManualLogSource logger)
        {
            int fresh = Math.Min(network.NoticeSerial - faction.LoggedSerial, network.NoticeCount);
            faction.LoggedSerial = network.NoticeSerial;
            for (int age = fresh - 1; age >= 0; age--)
            {
                CyberNotice notice = network.NoticeKind(age);
                int slot = network.NoticeSite(age);
                FactionHQ origin = Origin(hq, network.NoticeOrigin(age));
                string line = CyberWords.Notice(notice, CyberWords.Callsign(network, slot), Name(origin),
                    network.NoticeOrigin(age));
                if (line != null) logger?.LogInfo("[Support] " + Name(hq) + " CYBER: " + line + ".");
            }
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

        internal static string Name(FactionHQ hq) =>
            hq != null && hq.faction != null && !string.IsNullOrEmpty(hq.faction.factionName)
                ? hq.faction.factionName.ToUpperInvariant()
                : "UNKNOWN ACTOR";
    }
}
