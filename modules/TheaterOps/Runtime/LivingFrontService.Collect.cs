using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Math;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    // Sensing: the place book, one pass over the units, the fronts (named for places, followed along the contour) and what stands around them.
    internal sealed partial class LivingFrontService
    {
        private const float HeldAbove = .35f;
        private static readonly List<(int Hostile, int Index)> postScratch = new List<(int, int)>(LivingWarRules.MaximumBasePosts);
        private List<Objective> activeObjectives;

        /// <summary>
        /// Everything a review or a command needs to know about the faction's fronts, rebuilt in place: places, contacts, candidates (the live operation's and the
        /// deck's fronts re-found by key, then own bases under attack, mission objectives, contour samples), rings and the reads. It writes no baseline, no
        /// log line and no Fronts: only PublishFronts does, from a review.
        /// </summary>
        private void Collect(FactionWar war, float now)
        {
            ResolveServices();
            FactionHQ hq = war.HQ;
            BuildPlaces(war);
            if (scanned) scanned = false;   // the review's contacts were gathered ahead, in slices
            else ScanContacts(war, now);
            activeObjectives = MissionPosition.TryGetActiveObjectives(hq, out List<Objective> active) ? active : null;
            int traces = territory != null
                ? Mathf.Clamp(territory.CopyFrontlineTraces(hq.GetInstanceID(), tracePoints, traceLengths, tracePressure), 0, FrontlineTraceLimits.MaximumTraces) : 0;

            seeds.Clear();
            for (int i = 0; i < war.Candidates.Count; i++)
                if (IsSeedKey(war, war.Candidates[i].Key)) seeds.Add(war.Candidates[i]);
            war.Candidates.Clear();
            if (war.Active != null) AddSeed(war, FindSeed(war.Active.Key) ?? SeedOf(war.Active), traces);
            for (int i = 0; i < war.Deck.Count; i++)
            {
                FrontCandidate seed = FindSeed(war.Deck[i].Key);
                if (seed != null) AddSeed(war, seed, traces);
            }
            AddOwnBasesUnderAttack(war);
            AddObjectives(war);
            AddContour(war, traces);
            BuildReads(war, hq, now);
            Forget(war.Baseline, war);
            Forget(war.ObservedAt, war);
        }

        private void BuildPlaces(FactionWar war)
        {
            war.Places.Clear();
            if (FactionRegistry.airbaseLookup == null) return;
            foreach (KeyValuePair<string, Airbase> pair in FactionRegistry.airbaseLookup)
            {
                Airbase airbase = pair.Value;
                if (war.Places.Count >= LivingWarRules.MaximumPlaces) break;
                if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed() || string.IsNullOrEmpty(pair.Key)) continue;
                GlobalPosition at = (airbase.center != null ? airbase.center : airbase.transform).GlobalPosition();
                if (!float.IsFinite(at.x) || !float.IsFinite(at.z)) continue;
                if (!placeNames.TryGetValue(pair.Key, out string name))
                    placeNames[pair.Key] = name = PlaceNames.Clip(MfdAirbaseFormatter.Format(airbase));
                war.Places.Add(new Place(pair.Key, name, at.x, at.z, ReferenceEquals(airbase.CurrentHQ, war.HQ)));
            }
        }

        // The scan a review waits for, taken in slices of ScanUnitsPerFrame units: the faction it is for, the unit it has reached and the clock it started on.
        // The contact lists are shared, so one scan is in flight at a time; a command's whole-list scan (ScanContacts) cancels it and the review starts over.
        private FactionWar scanWar;
        private int scanAt;
        private float scanNow;
        private bool scanned;   // the review being run has its contacts already
        private const int ScanUnitsPerFrame = 512;

        /// <summary>One pass over the units into the contact list the rings count, all at once (a command wants the picture now).</summary>
        private void ScanContacts(FactionWar war, float now)
        {
            BeginScan(war, now);
            StepScan(int.MaxValue);
            scanWar = null;
        }

        private void BeginScan(FactionWar war, float now)
        {
#if !PUBLIC_RELEASE
            WarProof.BeforeScan(war.HQ, now);
#endif
            fixes.Clear(); ownGround.Clear();
            war.GroundSeen = false;
            war.HostileFixes = 0;
            scanWar = war; scanAt = 0; scanNow = now;
        }

        /// <summary>The next slice of the scan: hostile units count only while the faction has a fresh track on them. True when the scan is over.</summary>
        private bool StepScan(int budget)
        {
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null) return true;
            FactionWar war = scanWar;
            FactionHQ hq = war.HQ;
            float now = scanNow;
            GroundFrontService ground = GroundFrontService.Active;
            int cap = Math.Min(all.Count, MaximumUnitScan);
            int end = budget >= cap - scanAt ? cap : scanAt + budget;
            for (int i = scanAt; i < end; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null) continue;
                GlobalPosition at;
                ContactKind kind;
                if (ReferenceEquals(unit.NetworkHQ, hq))
                {
                    if (unit is GroundVehicle vehicle)
                    {
                        kind = ground != null && ground.CanCarry(vehicle) ? ContactKind.FriendlyCarrier : ContactKind.FriendlyGround;
                        war.GroundSeen = true;
                    }
                    else if (unit is Ship || unit is Aircraft aircraft && IsStaffAircraft(aircraft)) kind = ContactKind.FriendlyOther;
                    else continue;
                    at = unit.GlobalPosition();
                }
                else
                {
                    if (!(unit is GroundVehicle || unit is Ship || unit is Aircraft) || !HostileContact.TryRead(hq, unit, now, out at)) continue;
                    kind = unit is Aircraft ? ContactKind.HostileAir : ContactKind.HostileSurface;
                    if (unit is GroundVehicle) war.GroundSeen = true;
                }
                if (!float.IsFinite(at.x) || !float.IsFinite(at.z)) continue;
                fixes.Add(new ContactFix(at.x, at.z, kind));
                if (kind == ContactKind.HostileSurface || kind == ContactKind.HostileAir) war.HostileFixes++;
                if (kind == ContactKind.FriendlyGround || kind == ContactKind.FriendlyCarrier) ownGround.Add(new Vector2(at.x, at.z));
            }
            scanAt = end;
            return end >= cap;
        }

        /// <summary>An AI aircraft that is neither the player's, a wing member, nor reserved for another task: the staff counts it as other presence.</summary>
        private bool IsStaffAircraft(Aircraft aircraft) =>
            aircraft.Player == null && aircraft.pilots != null && aircraft.pilots.Length > 0 && aircraft.pilots[0] != null &&
            !aircraft.pilots[0].playerControlled &&
            !(excluded != null && excluded.IsExcluded(aircraft.persistentID.GetHashCode())) &&
            !WingLink.IsWingMember(aircraft.persistentID.GetHashCode());

        private bool IsSeedKey(FactionWar war, string key)
        {
            if (war.Active != null && war.Active.Key == key) return true;
            for (int i = 0; i < war.Deck.Count; i++) if (war.Deck[i].Key == key) return true;
            return false;
        }

        private FrontCandidate FindSeed(string key)
        {
            for (int i = 0; i < seeds.Count; i++) if (seeds[i].Key == key) return seeds[i];
            return null;
        }

        /// <summary>A candidate rebuilt from the operation itself, for the case the previous pass lost it.</summary>
        private static FrontCandidate SeedOf(Operation op) => new FrontCandidate
        { Key = op.Key, Name = op.Name, PlaceId = op.PlaceId, X = op.X, Z = op.Z, Source = op.Source, Live = true };

        private static bool HasKey(FactionWar war, string key)
        {
            for (int i = 0; i < war.Candidates.Count; i++) if (war.Candidates[i].Key == key) return true;
            return false;
        }

        /// <summary>Re-finds a pinned front by its key: an objective by name, a sector along the contour, a base in the place book. The name stays what the pilot saw.</summary>
        private void AddSeed(FactionWar war, FrontCandidate c, int traces)
        {
            if (war.Candidates.Count >= LivingWarRules.MaximumFronts || HasKey(war, c.Key)) return;
            c.Live = false;
            switch (c.Source)
            {
                case FrontSource.Objective:
                    Objective objective = ObjectiveBook.Find(c.Key);
                    if (objective != null && objective.Status != ObjectiveStatus.Complete && IsActive(objective) &&
                        TryFirstPosition(objective, out float ox, out float oz))
                    { c.X = ox; c.Z = oz; c.Live = true; }
                    break;
                case FrontSource.Sector:
                    if (FollowFix(c.X, c.Z, traces, out float fx, out float fz, out float pressure))
                    {
                        c.Live = true;
                        c.TracePressure = pressure;
                        if (Scalar.Distance2DSquared(c.X, c.Z, fx, fz) >= LivingWarRules.FollowStep * LivingWarRules.FollowStep) { c.X = fx; c.Z = fz; }
                    }
                    else c.TracePressure = 0f;
                    break;
                default:
                    int at = IndexOfPlace(war, c.PlaceId);
                    c.Live = c.Held = at >= 0 && war.Places[at].Owned;
                    if (at >= 0) { c.X = war.Places[at].X; c.Z = war.Places[at].Z; }
                    break;
            }
            war.Candidates.Add(c);
        }

        private static int IndexOfPlace(FactionWar war, string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < war.Places.Count; i++) if (war.Places[i].Id == id) return i;
            return -1;
        }

        /// <summary>The trace point nearest (x, z) within FollowRadius, with its trace's pressure; false when the contour no longer runs there.</summary>
        private bool FollowFix(float x, float z, int traces, out float fx, out float fz, out float pressure)
        {
            fx = x; fz = z; pressure = 0f;
            float best = LivingWarRules.FollowRadius * LivingWarRules.FollowRadius;
            bool found = false;
            int start = 0;
            for (int trace = 0; trace < traces; trace++)
            {
                int length = Mathf.Clamp(traceLengths[trace], 0, tracePoints.Length - start);
                for (int k = 0; k < length; k++)
                {
                    FrontlineTracePoint point = tracePoints[start + k];
                    float distance = Scalar.Distance2DSquared(x, z, point.X, point.Z);
                    if (!(distance <= best)) continue;
                    best = distance; fx = point.X; fz = point.Z; pressure = Mathf.Clamp01(tracePressure[trace]); found = true;
                }
                start += length;
            }
            return found;
        }

        /// <summary>
        /// A hostile at an owned airbase is a DEFEND the contour can never show (the base sits behind the line). Up to ReservedBaseSlots of them join the candidates,
        /// the most pressed first, so contour sampling leaves them room.
        /// </summary>
        private void AddOwnBasesUnderAttack(FactionWar war)
        {
            postScratch.Clear();
            for (int i = 0; i < war.Places.Count && postScratch.Count < LivingWarRules.MaximumBasePosts; i++)
            {
                Place place = war.Places[i];
                if (!place.Owned) continue;
                RingCounts rings = ContactCounts.Count(fixes, fixes.Count, place.X, place.Z, LivingWarRules.ReachRadius);
                int hostile = rings.NearHostile + rings.HostileAirNear;
                if (hostile > 0) postScratch.Add((hostile, i));
            }
            war.PostsHit = postScratch.Count;
            postScratch.Sort((a, b) => a.Hostile != b.Hostile ? b.Hostile.CompareTo(a.Hostile) : string.CompareOrdinal(war.Places[a.Index].Id, war.Places[b.Index].Id));
            for (int i = 0, added = 0; i < postScratch.Count && added < LivingWarRules.ReservedBaseSlots && war.Candidates.Count < LivingWarRules.MaximumFronts; i++)
            {
                Place place = war.Places[postScratch[i].Index];
                string key = PlaceNames.BaseKey(place.Id);
                if (HasKey(war, key)) continue;
                war.Candidates.Add(new FrontCandidate { Key = key, Name = place.Name, PlaceId = place.Id, X = place.X, Z = place.Z, Source = FrontSource.Base, Held = true, Live = true });
                added++;
            }
        }

        private void AddObjectives(FactionWar war)
        {
            war.HasObjectives = false;
            if (activeObjectives == null || activeObjectives.Count == 0) return;
            int scan = Math.Min(activeObjectives.Count, MaximumObjectiveScan);
            for (int i = 0; i < scan && !war.HasObjectives; i++) war.HasObjectives = IsOpenObjective(activeObjectives[i]);
            int inspected = 0;
            while (inspected < scan && CountSource(war, FrontSource.Objective) < MaximumObjectiveFronts && war.Candidates.Count < LivingWarRules.MaximumFronts)
                TryAddObjective(war, activeObjectives[(war.ObjectiveCursor + inspected++) % activeObjectives.Count]);
            war.ObjectiveCursor = (war.ObjectiveCursor + inspected) % activeObjectives.Count;
        }

        private static int CountSource(FactionWar war, FrontSource source)
        {
            int count = 0;
            for (int i = 0; i < war.Candidates.Count; i++) if (war.Candidates[i].Source == source) count++;
            return count;
        }

        private static bool IsOpenObjective(Objective objective) =>
            objective?.SavedObjective != null && !objective.SavedObjective.Hidden && objective.Status != ObjectiveStatus.Complete &&
            (objective.SavedObjective.ObjectiveTypeEnum == ObjectiveType.CaptureAirbase ||
             objective.SavedObjective.ObjectiveTypeEnum == ObjectiveType.DestroyUnits);

        private static bool TryFirstPosition(Objective objective, out float x, out float z)
        {
            x = z = 0f;
            if (!(objective is IObjectiveWithPosition positioned) || positioned.Positions.Count == 0) return false;
            GlobalPosition point = positioned.Positions[0].Position;
            if (!float.IsFinite(point.x) || !float.IsFinite(point.z)) return false;
            x = point.x; z = point.z;
            return true;
        }

        private bool IsActive(Objective objective) => activeObjectives != null && activeObjectives.Contains(objective);

        private void TryAddObjective(FactionWar war, Objective objective)
        {
            if (!IsOpenObjective(objective) || !TryFirstPosition(objective, out float x, out float z)) return;
            string key = objective.SavedObjective.UniqueName;
            if (string.IsNullOrEmpty(key) || HasKey(war, key)) return;
            war.Candidates.Add(new FrontCandidate { Key = key, Name = NameObjective(war, objective, x, z), X = x, Z = z, Source = FrontSource.Objective, Live = true });
        }

        /// <summary>A capture is named for the airbase it targets; a destroy objective for what its author typed, else for the place nearest it.</summary>
        private static string NameObjective(FactionWar war, Objective objective, float x, float z)
        {
            SavedObjective saved = objective.SavedObjective;
            int at = PlaceNames.Nearest(war.Places, x, z, out float distance);
            if (saved.ObjectiveTypeEnum == ObjectiveType.CaptureAirbase)
            {
                if (at >= 0) return distance <= 1500f ? war.Places[at].Name : PlaceNames.Describe(war.Places[at].Name, war.Places[at].X, war.Places[at].Z, x, z);
            }
            else if (PlaceNames.LooksAuthored(saved.DisplayName, saved.UniqueName))
                return PlaceNames.Clip(MfdAirbaseFormatter.FormatRaw(PlaceNames.StripTags(saved.DisplayName)));
            else if (at >= 0) return PlaceNames.Clip(war.Places[at].Name + " TARGETS");
            return PlaceNames.Clip(MfdAirbaseFormatter.FormatRaw(PlaceNames.StripTags(saved.UniqueName)));
        }

        /// <summary>
        /// Contour samples, nearest our own ground first, up to three per trace. A trace whose peak pressure is below the presence threshold is the strategic
        /// bisector between two airbases, not a fight. A sector is keyed by the airbase nearest the point, so the key survives the contour moving.
        /// </summary>
        private void AddContour(FactionWar war, int traces)
        {
            if (traces == 0 || war.Places.Count == 0) return;
            int limit = LivingWarRules.MaximumFronts - Math.Max(0, LivingWarRules.ReservedBaseSlots - CountSource(war, FrontSource.Base));
            int start = 0;
            for (int trace = 0; trace < traces && war.Candidates.Count < limit; trace++)
            {
                int length = Mathf.Clamp(traceLengths[trace], 0, tracePoints.Length - start);
                float peak = Mathf.Clamp01(tracePressure[trace]);
                if (peak >= LivingWarRules.PresenceThreshold)
                {
                    int tries = OrderByOwnGround(start, length);
                    for (int k = 0, added = 0; k < tries && added < 3 && war.Candidates.Count < limit; k++)
                    {
                        FrontlineTracePoint point = tracePoints[pointOrder[k]];
                        if (!float.IsFinite(point.X) || !float.IsFinite(point.Z)) continue;
                        int at = PlaceNames.Nearest(war.Places, point.X, point.Z, out _);
                        if (at < 0 || NearObjective(war, war.Places[at])) continue;
                        string key = PlaceNames.SectorKey(war.Places[at].Id);
                        if (MergeInto(war, key, point.X, point.Z, peak)) continue;
                        war.Candidates.Add(new FrontCandidate
                        {
                            Key = key, Name = PlaceNames.Describe(war.Places[at].Name, war.Places[at].X, war.Places[at].Z, point.X, point.Z),
                            X = point.X, Z = point.Z, TracePressure = peak, Source = FrontSource.Sector, Live = true,
                        });
                        added++;
                    }
                }
                start += length;
            }
        }

        /// <summary>True when a front with this key, or any sector or base within 2 km of the point, is already a candidate; that front takes the pressure of the trace running through it.</summary>
        private static bool MergeInto(FactionWar war, string key, float x, float z, float pressure)
        {
            for (int i = 0; i < war.Candidates.Count; i++)
            {
                FrontCandidate c = war.Candidates[i];
                if (c.Key != key && (c.Source == FrontSource.Objective || Scalar.Distance2DSquared(c.X, c.Z, x, z) >= 2000f * 2000f)) continue;
                if (c.Source != FrontSource.Objective) c.TracePressure = Mathf.Max(c.TracePressure, pressure);
                return true;
            }
            return false;
        }

        /// <summary>The objective already names this place, so a sector anchored within 6 km of one is not a second front.</summary>
        private static bool NearObjective(FactionWar war, Place place)
        {
            for (int i = 0; i < war.Candidates.Count; i++)
                if (war.Candidates[i].Source == FrontSource.Objective &&
                    Scalar.Distance2DSquared(war.Candidates[i].X, war.Candidates[i].Z, place.X, place.Z) < 6000f * 6000f) return true;
            return false;
        }

        /// <summary>Fills <see cref="pointOrder"/> with this trace's point indices, nearest own ground first; with no own ground it is the quarter points. Returns how many to try.</summary>
        private int OrderByOwnGround(int start, int length)
        {
            if (length < 2) return 0;
            if (ownGround.Count == 0)
            {
                for (int part = 1; part <= 3; part++) pointOrder[part - 1] = start + (length - 1) * part / 4;
                return 3;
            }
            // ponytail: at most 64 evenly spaced points per trace (~2.5 km apart on a 160 km line) x own vehicles per 10 s review; a spatial index would be the upgrade if factions field thousands of vehicles.
            int stride = Mathf.Max(1, length / 64), n = 0;
            for (int i = 0; i < length; i += stride, n++)
            {
                FrontlineTracePoint point = tracePoints[start + i];
                float best = float.MaxValue;
                for (int u = 0; u < ownGround.Count; u++)
                    best = Mathf.Min(best, Scalar.Distance2DSquared(point.X, point.Z, ownGround[u].x, ownGround[u].y));
                pointDistance[n] = float.IsFinite(point.X) && float.IsFinite(point.Z) ? best : float.MaxValue;
                pointOrder[n] = start + i;
            }
            Array.Sort(pointDistance, pointOrder, 0, n);
            return n;
        }

        /// <summary>Counts what stands around each candidate in both rings and reads whether it is held and observed.</summary>
        private void BuildReads(FactionWar war, FactionHQ hq, float now)
        {
            war.Reads.Clear();
            int hqId = hq.GetInstanceID();
            for (int i = 0; i < war.Candidates.Count; i++)
            {
                FrontCandidate c = war.Candidates[i];
                RingCounts rings = ContactCounts.Count(fixes, fixes.Count, c.X, c.Z, LivingWarRules.ReachRadius);
                if (c.Source == FrontSource.Sector)
                    c.Held = c.Live && territory != null && territory.TryGetHoldStrength(hqId, c.X, c.Z, out float hold) && hold > HeldAbove;
                else if (c.Source == FrontSource.Objective) c.Held = false;
                bool observed = rings.NearHostile + rings.HostileAirNear > 0 ||
                    picture != null && picture.TryGetAreaIntel(hqId, c.X, c.Z, ContactCounts.NearRadius, out AreaIntel intel) && intel.Scouted;
                float pressure = Mathf.Clamp01(Mathf.Max(c.TracePressure, rings.NearHostile / (float)Mathf.Max(1, rings.NearFriendly + rings.NearHostile)));
                if (observed && (war.ObservedAt.Count < MaximumRemembered || war.ObservedAt.ContainsKey(c.Key))) war.ObservedAt[c.Key] = now;
                war.Reads.Add(new WarFrontRead(c.Key, c.Name, c.X, c.Z, pressure, rings.NearFriendly, rings.NearHostile, rings.HostileAirNear,
                    rings.ReachFriendlyGround, rings.ReachFriendlyOther, rings.ReachHostile, rings.Carriers, c.Source, c.Held, observed, c.Live));
            }
        }

        private void Forget<T>(Dictionary<string, T> map, FactionWar war)
        {
            forgotten.Clear();
            foreach (string key in map.Keys) if (!HasKey(war, key)) forgotten.Add(key);
            for (int i = 0; i < forgotten.Count; i++) map.Remove(forgotten[i]);
        }

        /// <summary>Objectives are found by name and never throw: the game's own lookup raises on a key it does not hold.</summary>
        private static class ObjectiveBook
        {
            internal static Objective Find(string key)
            {
                List<Objective> all = MissionManager.Objectives?.AllObjectives;
                if (all == null || string.IsNullOrEmpty(key)) return null;
                for (int i = 0; i < all.Count; i++)
                    if (all[i]?.SavedObjective != null && string.Equals(all[i].SavedObjective.UniqueName, key, StringComparison.Ordinal)) return all[i];
                return null;
            }
        }
    }
}
