using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Runtime.Actions;
using BoscaliSummer.Framework.Contracts;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Host side of SPEC OPS: the only code that reads the scene for the detachment and the only
    /// code that applies what a team does to it. It lists each faction's objectives from the real
    /// map (airbases it does not hold, towns, clustered hostile air defence) with their threat and
    /// radars, advances every detachment, and turns a success into the native effect — a tracking
    /// reveal, a timed radar jam, or buildings occupied through Urban Combat's contract. It also
    /// runs the posts: an observation post re-reveals its ground, and SUPPRESS adds a jam zone.
    /// Bounded: one scan per two seconds, a town scan per ten, six air-defence clusters, twelve
    /// objectives, six jam zones pulsed every 0.25 s, 48 units per pulse and per reveal. Never spawns or commands a
    /// squad; the teams are abstract.
    /// </summary>
    internal sealed class SpecOpsTheater
    {
        private const float ScanInterval = 2f;
        private const float TownInterval = 10f;
        /// <summary>A radar's jam decays roughly as e^-t; the CYBER capstone's 0.25 s cadence keeps it under.</summary>
        private const float JamInterval = 0.25f;
        private const float JamStrength = 1000f;
        private const int MaximumCandidates = 48;
        private const int MaximumAirDefence = 6;
        private const int MaximumUnits = 2048;
        private const int MaximumJams = 6;
        private const int MaximumJammed = 48;
        private const int MaximumTowns = 24;
        private const float TownAirbaseRadius = 8000f;

        private struct Candidate
        {
            public ObjectiveKind Kind;
            public int Anchor;
            public float X, Z;
            public bool Hostile;
            public string Name;
            public int Threat, Radars;
            public float Order;
        }

        private struct Town
        {
            public int Anchor;
            public float X, Z;
            public string Name;
        }

        private struct Jam
        {
            public FactionHQ Owner;
            public float X, Z, Radius;
            public double Until;
        }

        private readonly List<Candidate> candidates = new List<Candidate>(MaximumCandidates);
        private readonly List<Town> towns = new List<Town>(MaximumTowns);
        private readonly List<Airbase> owned = new List<Airbase>(16);
        private readonly Vector3[] unitPositions = new Vector3[MaximumUnits];
        private readonly FactionHQ[] unitOwners = new FactionHQ[MaximumUnits];
        private readonly bool[] unitRadars = new bool[MaximumUnits];
        private readonly int[] unitIds = new int[MaximumUnits];
        private readonly Jam[] jams = new Jam[MaximumJams];
        private readonly Dictionary<FactionHQ, int> loggedSerial = new Dictionary<FactionHQ, int>();
        private readonly double[] opRefresh = new double[SpecOpsDetachment.TeamCount * SpaceOperations.MaximumFactions];
        private int unitCount;
        private float nextScan;
        private float nextTowns;
        private double nextJam;

        private FactionHQ currentOwner;
        private SpaceOperations currentSpace;
        private IZoneFortificationService fortifications;
        private ManualLogSource logger;
        private readonly Func<double> roll = () => UnityEngine.Random.value;
        private readonly Func<FieldResult, bool> apply;

        public SpecOpsTheater() => apply = Apply;

        /// <summary>Urban Combat's seam, resolved by the manager; null when the module is off.</summary>
        public IZoneFortificationService Fortifications
        {
            get => fortifications;
            set => fortifications = value;
        }

        // ---- Host tick ---------------------------------------------------------------------------

        /// <summary>Host, every frame. <paramref name="enabled"/> is the SPEC OPS host switch;
        /// <paramref name="seize"/> whether Urban Combat can occupy buildings.</summary>
        public void Tick(SpaceOperations space, double now, float unscaledNow, bool enabled, bool seize, ManualLogSource log)
        {
            if (space == null) return;
            logger = log;
            bool scan = unscaledNow >= nextScan;
            if (scan)
            {
                nextScan = unscaledNow + ScanInterval;
                if (unscaledNow >= nextTowns)
                {
                    nextTowns = unscaledNow + TownInterval;
                    ScanTowns();
                }
                CollectUnits();
            }

            for (int f = 0; f < space.FactionCount; f++)
            {
                FactionHQ hq = space.FactionAt(f);
                SpecOpsDetachment detachment = space.DetachmentFor(hq);
                if (hq == null || detachment == null) continue;
                detachment.Enabled = enabled;
                detachment.SeizeAvailable = seize && fortifications != null;
                if (!enabled) continue;
                if (scan) List(hq, detachment);
                currentOwner = hq;
                currentSpace = space;
                detachment.Tick(now, roll, apply);
                RefreshPosts(f, hq, detachment, now);
                Announce(hq, detachment);
            }
            currentOwner = null;
            currentSpace = null;

            if (now >= nextJam)
            {
                nextJam = now + JamInterval;
                PulseJams(now);
            }
        }

        public void Clear()
        {
            candidates.Clear();
            towns.Clear();
            owned.Clear();
            Array.Clear(unitOwners, 0, unitOwners.Length);
            Array.Clear(jams, 0, jams.Length);
            Array.Clear(opRefresh, 0, opRefresh.Length);
            loggedSerial.Clear();
            unitCount = 0;
            nextScan = nextTowns = 0f;
            nextJam = 0.0;
            currentOwner = null;
            currentSpace = null;
        }

        /// <summary>Metres from the faction's nearest held airbase to a point; negative when it holds none.</summary>
        public static float TravelMetres(FactionHQ hq, float x, float z)
        {
            if (hq == null) return -1f;
            float best = -1f;
            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.AttachedAirbase || airbase.CurrentHQ != hq) continue;
                GlobalPosition at = Centre(airbase).ToGlobalPosition();
                float distance = Mathf.Sqrt((at.x - x) * (at.x - x) + (at.z - z) * (at.z - z));
                if (best < 0f || distance < best) best = distance;
            }
            return best;
        }

        // ---- Effects -----------------------------------------------------------------------------

        /// <summary>A timed jam on hostile ground radars; false when all six zones are busy.</summary>
        public bool AddJam(FactionHQ owner, float x, float z, float radius, float seconds, double now)
        {
            if (owner == null || float.IsNaN(x) || float.IsNaN(z)) return false;
            for (int i = 0; i < jams.Length; i++)
            {
                if (jams[i].Owner != null && jams[i].Until > now) continue;
                jams[i] = new Jam { Owner = owner, X = x, Z = z, Radius = radius, Until = now + seconds };
                return true;
            }
            return false;
        }

        private bool Apply(FieldResult result)
        {
            FactionHQ owner = currentOwner;
            if (owner == null) return false;
            var target = new GlobalPosition(result.X, 0f, result.Z);
            try
            {
                switch (result.Mission)
                {
                    case FieldMission.Recon:
                        ReconAction.Reveal(owner, target, FieldCatalog.ReconRadius(result.Rank), logger, RevealFilter.Ground);
                        return true;
                    case FieldMission.Sabotage:
                        return AddJam(owner, result.X, result.Z, FieldCatalog.SabotageRadius(result.Rank),
                            FieldCatalog.SabotageSeconds(result.Rank), Time.timeSinceLevelLoadAsDouble);
                    case FieldMission.Steal:
                        var cyber = currentSpace?.CyberFor(owner);
                        if (cyber == null) return false;
                        cyber.GrantIntel(FieldCatalog.StealIntel(result.Rank));
                        return true;
                    default:
                        if (fortifications == null) return false;
                        int placed = fortifications.TrySeize(result.X, result.Z, FieldCatalog.SeizeRadius, owner,
                            FieldCatalog.SeizeBuildings(result.Rank));
                        return placed > 0;
                }
            }
            catch (Exception e)
            {
                logger?.LogWarning("[Support] SPEC OPS effect failed: " + e.Message);
                return false;
            }
        }

        /// <summary>An observation post keeps its ground on the datalink: a quiet reveal every 30 s.</summary>
        private void RefreshPosts(int faction, FactionHQ hq, SpecOpsDetachment detachment, double now)
        {
            for (int t = 0; t < SpecOpsDetachment.TeamCount; t++)
            {
                int slot = faction * SpecOpsDetachment.TeamCount + t;
                if (slot >= opRefresh.Length) return;
                FieldTeam team = detachment.Team(t);
                if (team.State != TeamState.Holding || team.Mission != FieldMission.Recon)
                {
                    opRefresh[slot] = 0.0;
                    continue;
                }
                if (opRefresh[slot] == 0.0) opRefresh[slot] = now + FieldCatalog.OpRefreshSeconds;
                if (now < opRefresh[slot]) continue;
                opRefresh[slot] = now + FieldCatalog.OpRefreshSeconds;
                try
                {
                    ReconAction.Reveal(hq, new GlobalPosition(team.X, 0f, team.Z), FieldCatalog.ReconRadius(team.Rank),
                        logger, RevealFilter.Ground, quiet: true);
                }
                catch (Exception e)
                {
                    logger?.LogWarning("[Support] Observation post refresh failed: " + e.Message);
                }
            }
        }

        private void PulseJams(double now)
        {
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return;
            for (int j = 0; j < jams.Length; j++)
            {
                Jam jam = jams[j];
                if (jam.Owner == null) continue;
                if (jam.Until <= now)
                {
                    jams[j] = default;
                    continue;
                }
                Vector3 centre = new GlobalPosition(jam.X, 0f, jam.Z).ToLocalPosition();
                float radiusSquared = jam.Radius * jam.Radius;
                int affected = 0;
                for (int i = 0; i < units.Count && affected < MaximumJammed; i++)
                {
                    Unit unit = units[i];
                    if (!HostileRadar(unit, jam.Owner)) continue;
                    Vector3 position = unit.transform.position;
                    float dx = position.x - centre.x, dz = position.z - centre.z;
                    if (dx * dx + dz * dz > radiusSquared) continue;
                    unit.Jam(new Unit.JamEventArgs { jammingUnit = null, jamAmount = JamStrength });
                    affected++;
                }
            }
        }

        // ---- Objectives --------------------------------------------------------------------------

        private void List(FactionHQ hq, SpecOpsDetachment detachment)
        {
            candidates.Clear();
            owned.Clear();
            foreach (Airbase airbase in hq.GetAirbases())
                if (airbase != null && !airbase.AttachedAirbase && airbase.CurrentHQ == hq && owned.Count < 16) owned.Add(airbase);

            // Order by distance from the centre of the holdings, so the front comes first.
            float cx = 0f, cz = 0f;
            for (int i = 0; i < owned.Count; i++)
            {
                GlobalPosition at = Centre(owned[i]).ToGlobalPosition();
                cx += at.x;
                cz += at.z;
            }
            if (owned.Count > 0)
            {
                cx /= owned.Count;
                cz /= owned.Count;
            }

            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (candidates.Count >= MaximumCandidates) break;
                    if (airbase == null || airbase.AttachedAirbase || airbase.disabled || airbase.CurrentHQ == hq) continue;
                    GlobalPosition at = Centre(airbase).ToGlobalPosition();
                    bool runway = airbase.runways != null && airbase.runways.Length > 0;
                    Add(runway ? ObjectiveKind.Airfield : ObjectiveKind.Outpost, airbase.GetInstanceID(), at.x, at.z,
                        airbase.CurrentHQ != null, AirbaseName(airbase, runway), cx, cz);
                }

            for (int i = 0; i < towns.Count && candidates.Count < MaximumCandidates; i++)
            {
                Town town = towns[i];
                Airbase nearest = NearestAirbase(town.X, town.Z, out float distance);
                // A town next to one of our own bases is home ground, not an objective.
                if (nearest != null && distance < TownAirbaseRadius && nearest.CurrentHQ == hq) continue;
                bool hostile = nearest != null && distance < TownAirbaseRadius && nearest.CurrentHQ != null;
                Add(ObjectiveKind.Town, town.Anchor, town.X, town.Z, hostile, town.Name, cx, cz);
            }

            ClusterAirDefence(hq, cx, cz);
            CountThreats(hq);

            candidates.Sort((a, b) => a.Order.CompareTo(b.Order));
            detachment.BeginObjectives();
            // An objective a team is on stays listed, so its card and the launch rules keep working.
            for (int i = 0; i < candidates.Count; i++)
                if (detachment.TeamOn(candidates[i].Anchor) >= 0) Report(detachment, candidates[i]);
            for (int i = 0; i < candidates.Count; i++)
                if (detachment.TeamOn(candidates[i].Anchor) < 0) Report(detachment, candidates[i]);
            detachment.EndObjectives();
        }

        private static void Report(SpecOpsDetachment detachment, in Candidate c) =>
            detachment.ReportObjective(c.Kind, c.Anchor, c.X, c.Z, c.Threat, c.Radars, c.Hostile, c.Name);

        private void Add(ObjectiveKind kind, int anchor, float x, float z, bool hostile, string name, float cx, float cz)
        {
            if (candidates.Count >= MaximumCandidates) return;
            candidates.Add(new Candidate
            {
                Kind = kind, Anchor = anchor, X = x, Z = z, Hostile = hostile, Name = name,
                Order = (x - cx) * (x - cx) + (z - cz) * (z - cz)
            });
        }

        /// <summary>Hostile ground radars more than 2.5 km from any listed place, grouped within 3 km.</summary>
        private void ClusterAirDefence(FactionHQ hq, float cx, float cz)
        {
            int start = candidates.Count;
            int clusters = 0;
            for (int u = 0; u < unitCount && candidates.Count < MaximumCandidates && clusters < MaximumAirDefence; u++)
            {
                if (!unitRadars[u] || unitOwners[u] == null || unitOwners[u] == hq) continue;
                GlobalPosition at = unitPositions[u].ToGlobalPosition();
                bool placed = false;
                for (int c = 0; c < candidates.Count; c++)
                {
                    Candidate candidate = candidates[c];
                    float limit = c >= start ? FieldCatalog.AirDefenceCluster : FieldCatalog.RadarRadius;
                    float dx = candidate.X - at.x, dz = candidate.Z - at.z;
                    if (dx * dx + dz * dz > limit * limit) continue;
                    placed = true;
                    break;
                }
                if (placed) continue;
                Add(ObjectiveKind.AirDefence, unitIds[u], at.x, at.z, true,
                    "AIR DEFENCE " + GridRef(at.x, at.z), cx, cz);
                clusters++;
            }
        }

        private void CountThreats(FactionHQ hq)
        {
            float threat = FieldCatalog.ThreatRadius * FieldCatalog.ThreatRadius;
            float radar = FieldCatalog.RadarRadius * FieldCatalog.RadarRadius;
            for (int c = 0; c < candidates.Count; c++)
            {
                Candidate candidate = candidates[c];
                Vector3 local = new GlobalPosition(candidate.X, 0f, candidate.Z).ToLocalPosition();
                int units = 0, radars = 0;
                for (int u = 0; u < unitCount; u++)
                {
                    if (unitOwners[u] == null || unitOwners[u] == hq) continue;
                    float dx = unitPositions[u].x - local.x, dz = unitPositions[u].z - local.z;
                    float distance = dx * dx + dz * dz;
                    if (distance <= threat) units++;
                    if (unitRadars[u] && distance <= radar) radars++;
                }
                candidate.Threat = units;
                candidate.Radars = radars;
                candidates[c] = candidate;
            }
        }

        private void CollectUnits()
        {
            unitCount = 0;
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return;
            for (int i = 0; i < units.Count && unitCount < MaximumUnits; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled || unit is Aircraft || unit.NetworkHQ == null) continue;
                unitPositions[unitCount] = unit.transform.position;
                unitOwners[unitCount] = unit.NetworkHQ;
                // Unit.radar is the base detector; only a real Radar is air defence and can be jammed.
                unitRadars[unitCount] = unit.radar is Radar;
                unitIds[unitCount] = unit.GetInstanceID();
                unitCount++;
            }
            for (int i = unitCount; i < MaximumUnits && unitOwners[i] != null; i++) unitOwners[i] = null;
        }

        /// <summary>Every ten seconds: the named city sets, outside any airbase's own footprint (the CYBER rule).</summary>
        private void ScanTowns()
        {
            towns.Clear();
            MapBuildingSet[] sets = Resources.FindObjectsOfTypeAll<MapBuildingSet>();
            if (sets == null) return;
            for (int i = 0; i < sets.Length && towns.Count < MaximumTowns; i++)
            {
                MapBuildingSet set = sets[i];
                if (set == null || !set.gameObject.scene.IsValid() || !set.gameObject.activeInHierarchy) continue;
                if (!CyberLocations.CitySetName(set.name)) continue;
                GlobalPosition at = set.transform.position.ToGlobalPosition();
                if (InsideAirbase(set.transform.position)) continue;
                int anchor = unchecked((int)set.NetId);
                if (anchor == 0) anchor = set.GetInstanceID();
                towns.Add(new Town { Anchor = anchor, X = at.x, Z = at.z, Name = TownName(set.name, at.x, at.z) });
            }
        }

        // ---- Notices -------------------------------------------------------------------------------

        private void Announce(FactionHQ hq, SpecOpsDetachment detachment)
        {
            loggedSerial.TryGetValue(hq, out int logged);
            int fresh = Math.Min(detachment.NoticeSerial - logged, detachment.NoticeCount);
            loggedSerial[hq] = detachment.NoticeSerial;
            for (int age = fresh - 1; age >= 0; age--)
            {
                int team = detachment.NoticeTeam(age);
                string line = FieldWords.Notice(detachment.NoticeKind(age), team, detachment.NoticeMission(age),
                    detachment.Team(team).Target);
                if (line != null) logger?.LogInfo("[Support] " + CyberDefense.Name(hq) + " SPEC OPS: " + line + ".");
            }
        }

        // ---- Helpers -------------------------------------------------------------------------------

        private static bool HostileRadar(Unit unit, FactionHQ owner) =>
            unit != null && !unit.disabled && !(unit is Aircraft) && unit.radar is Radar &&
            unit.NetworkHQ != null && unit.NetworkHQ != owner;

        private static Vector3 Centre(Airbase airbase) =>
            airbase.center != null ? airbase.center.position : airbase.transform.position;

        private static Airbase NearestAirbase(float x, float z, out float distance)
        {
            distance = float.MaxValue;
            Airbase best = null;
            if (FactionRegistry.airbaseLookup == null) return null;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.AttachedAirbase) continue;
                GlobalPosition at = Centre(airbase).ToGlobalPosition();
                float d = Mathf.Sqrt((at.x - x) * (at.x - x) + (at.z - z) * (at.z - z));
                if (d >= distance) continue;
                distance = d;
                best = airbase;
            }
            return best;
        }

        private static bool InsideAirbase(Vector3 local)
        {
            if (FactionRegistry.airbaseLookup == null) return false;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.AttachedAirbase) continue;
                float radius = Mathf.Max(airbase.GetRadius(), 1500f);
                Vector3 centre = Centre(airbase);
                float dx = centre.x - local.x, dz = centre.z - local.z;
                if (dx * dx + dz * dz <= radius * radius) return true;
            }
            return false;
        }

        private static string AirbaseName(Airbase airbase, bool runway)
        {
            string name = null;
            try
            {
                if (airbase.SavedAirbase != null) name = airbase.SavedAirbase.DisplayName;
            }
            catch (Exception)
            {
                name = null;
            }
            if (string.IsNullOrWhiteSpace(name)) name = airbase.name;
            name = Clean(name);
            if (name.Length < 3) name = (runway ? "AIRFIELD " : "OUTPOST ") + GridRef(Centre(airbase).ToGlobalPosition());
            return SpecOpsDetachment.Clip(name);
        }

        private static string TownName(string raw, float x, float z)
        {
            // The objective name on the wire is 20 characters. PlaceNames keeps the phrase; Clip fits the field.
            return SpecOpsDetachment.Clip(PlaceNames.Town(raw, NearestLabel(x, z), GridRef(x, z)));
        }

        /// <summary>The nearest airbase's cleaned display name, or null when it has none.</summary>
        private static string NearestLabel(float x, float z)
        {
            Airbase near = NearestAirbase(x, z, out _);
            if (near == null) return null;
            string name = null;
            try
            {
                if (near.SavedAirbase != null) name = near.SavedAirbase.DisplayName;
            }
            catch (Exception)
            {
                name = null;
            }
            if (string.IsNullOrWhiteSpace(name)) name = near.name;
            name = PlaceNames.Clean(name);
            return name.Length >= 3 ? name : null;
        }

        private static string Clean(string raw) => PlaceNames.Clean(raw);

        private static string GridRef(GlobalPosition at) => GridRef(at.x, at.z);

        /// <summary>"12/-3": kilometres east/north of the map centre, short enough for a name.</summary>
        private static string GridRef(float x, float z) =>
            Mathf.RoundToInt(x / 1000f) + "/" + Mathf.RoundToInt(z / 1000f);
    }
}
