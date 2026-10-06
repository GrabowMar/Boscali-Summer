using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Intel.Domain;
using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Intel.Runtime
{
    /// <summary>
    /// One faction's own knowledge: the hostiles its tracking database holds, the pre-war sites
    /// it was given, the launches its aircraft warned of, the ground its units looked at, and
    /// the rings all of that adds up to. The unit registry is read only to resolve a unit this
    /// faction already knows about: its type, whether it still exists, whether it emits.
    /// </summary>
    internal sealed class FactionPicture
    {
        private readonly KnownHostileTable known = new KnownHostileTable();
        private readonly ObservationGrid grid = new ObservationGrid();
        private readonly LaunchMemory launches = new LaunchMemory();
        private readonly RingSet ringSet = new RingSet();
        private readonly RingInput[] inputs = new RingInput[KnownHostileTable.DefaultCapacity + LaunchMemory.Capacity];
        private readonly Action<PersistentID> onDiscover;
        private readonly Action<PersistentID> onForget;
        private readonly UnitProfiles profiles;

        private AirDefenceRing[] front = new AirDefenceRing[ThreatPictureLimits.MaximumRings];
        private AirDefenceRing[] back = new AirDefenceRing[ThreatPictureLimits.MaximumRings];
        private string name;
        private int expectedTracking;
        private int signature;
        private bool tableFullLogged;
        private bool ringsFullLogged;
        private bool gridFullLogged;
        private bool faultLogged;

        public FactionPicture(UnitProfiles unitProfiles)
        {
            profiles = unitProfiles;
            onDiscover = OnDiscover;
            onForget = OnForget;
        }

        public FactionHQ Hq { get; private set; }

        /// <summary>The observer id every read is keyed by: FactionHQ.GetInstanceID().</summary>
        public int Id { get; private set; }

        public uint Version { get; private set; }

        public bool Ready { get; private set; }

        public bool PreWarSeeded { get; set; }

        public float NextRebuild { get; set; }

        public int RingCount { get; private set; }

        /// <summary>The published buffer; rebuilds write the other one and swap.</summary>
        public AirDefenceRing[] Rings => front;

        public KnownHostileTable Known => known;

        public ObservationGrid Grid => grid;

        public static bool IsStatic(Unit unit, UnitClass unitClass)
        {
            if (unitClass == UnitClass.Building) return true;
            return unitClass == UnitClass.GroundVehicle && unit is GroundVehicle vehicle && vehicle.GetHoldPosition();
        }

        public void Attach(FactionHQ hq, float level)
        {
            Detach();
            Hq = hq;
            Id = hq.GetInstanceID();
            name = hq.faction != null ? hq.faction.factionName : "faction " + Id;
            hq.onDiscoverUnit += onDiscover;
            hq.onForgetUnit += onForget;
            Reseed(level);
        }

        /// <summary>Unsubscribes idempotently, even from an HQ Unity has already destroyed, and forgets everything.</summary>
        public void Detach()
        {
            if (!ReferenceEquals(Hq, null))
            {
                Hq.onDiscoverUnit -= onDiscover;
                Hq.onForgetUnit -= onForget;
            }
            Hq = null;
            Id = 0;
            name = null;
            known.Clear();
            grid.Clear();
            launches.Clear();
            RingCount = 0;
            Version = 0u;
            Ready = false;
            PreWarSeeded = false;
            NextRebuild = 0f;
            expectedTracking = 0;
            signature = 0;
            tableFullLogged = false;
            ringsFullLogged = false;
            gridFullLogged = false;
            faultLogged = false;
        }

        /// <summary>
        /// Admits what trackingDatabase holds (at most 1024 entries) and, after a complete pass,
        /// sweeps tracked entries it no longer holds. Runs on attach and whenever the
        /// event-counted size drifts by more than 16 (vanilla's late-join batch fires no events).
        /// </summary>
        public void Reseed(float level)
        {
            Dictionary<PersistentID, TrackingInfo> database = Hq != null ? Hq.trackingDatabase : null;
            if (database == null) return;
            known.BeginSweep();
            int enumerated = 0;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in database)
            {
                if (++enumerated > KnownHostileFilter.MaximumSeedEnumeration) break;
                Admit(pair.Key, pair.Value, level);
            }
            // Sweep only after a complete enumeration: a capped one has not seen everything.
            if (enumerated <= KnownHostileFilter.MaximumSeedEnumeration) known.SweepUnseenConfirmed();
            expectedTracking = database.Count;
        }

        public void SeedPreWar(uint id, float x, float z, UnitClass unitClass, ForceRole role, short profile, float level)
        {
            Note(known.Upsert(new KnownHostile
            {
                Id = id,
                X = x,
                Z = z,
                SpottedAt = level,
                Priority = KnownHostileFilter.PriorityAirDefence,
                Static = true,
                Class = unitClass,
                Role = role,
                Profile = profile,
                PreWar = true,
                Confirmed = false,
                Emitting = false,
                DeadSince = float.NaN
            }));
        }

        /// <summary>
        /// Refreshes positions and ages from the faction's own tracking, drops pre-war sites
        /// seen dead, ages launch memory, and rebuilds the rings into the back buffer before
        /// swapping. Version moves only when the rings changed.
        /// </summary>
        public void Rebuild(float level)
        {
            if (Hq == null) return;
            try
            {
                Dictionary<PersistentID, TrackingInfo> database = Hq.trackingDatabase;
                if (database != null && KnownHostileFilter.NeedsReseed(expectedTracking, database.Count)) Reseed(level);

                int n = 0;
                for (int i = known.Count - 1; i >= 0; i--)
                {
                    ref KnownHostile entry = ref known[i];
                    Unit unit = null;
                    if (entry.Confirmed && database != null &&
                        database.TryGetValue(new PersistentID { Id = entry.Id }, out TrackingInfo info) && info != null)
                    {
                        entry.X = info.lastKnownPosition.x;
                        entry.Z = info.lastKnownPosition.z;
                        entry.SpottedAt = info.lastSpottedTime;
                        if (entry.Priority == KnownHostileFilter.PriorityAirDefence) info.TryGetUnit(out unit);
                    }
                    else if (entry.Priority == KnownHostileFilter.PriorityAirDefence)
                    {
                        UnitRegistry.TryGetUnit(new PersistentID { Id = entry.Id }, out unit);
                    }

                    if (entry.PreWar && !entry.Confirmed && DeadAndSeen(ref entry, unit, level))
                    {
                        known.Remove(entry.Id);
                        continue;
                    }
                    if (entry.Priority != KnownHostileFilter.PriorityAirDefence || entry.Profile < 0) continue;
                    bool alive = unit != null && !unit.disabled;
                    // An RWR sees an emitter, so reading the emission is fair (spec §7).
                    entry.Emitting = alive && unit.HasRadarEmission();
                    if (alive) entry.Static = IsStatic(unit, entry.Class);
                    if (n < inputs.Length) inputs[n++] = ToInput(in entry, level);
                }

                launches.Prune(level);
                for (int i = 0; i < launches.Count && n < inputs.Length; i++)
                {
                    LaunchEntry launch = launches[i];
                    // A launcher the faction already knows draws its own ring.
                    if (known.TryFind(launch.OwnerId, out _)) continue;
                    inputs[n++] = new RingInput
                    {
                        X = launch.X,
                        Z = launch.Z,
                        MaxRange = launch.MaxRange,
                        MinAltitude = launch.MinAltitude,
                        MaxAltitude = launch.MaxAltitude,
                        Kind = AirDefenceKind.Unknown,
                        OwnRadar = false,
                        Emitting = false,
                        Source = RingSource.Launch,
                        Confirmed = false,
                        AgeSeconds = level - launch.LastLaunch > 0f ? level - launch.LastLaunch : 0f,
                        Stale = false,
                        UnitHash = (int)launch.OwnerId
                    };
                }

                int count = ringSet.Build(inputs, n, back);
                if (ringSet.Dropped > 0 && !ringsFullLogged)
                {
                    ringsFullLogged = true;
                    Plugin.Logger?.LogWarning("Intel: " + name + " knows more than " + ThreatPictureLimits.MaximumRings +
                                       " air-defence rings; gun rings give way first, then IR.");
                }
                if (grid.Overflowed && !gridFullLogged)
                {
                    gridFullLogged = true;
                    Plugin.Logger?.LogWarning("Intel: " + name + " observation grid is full (" + ObservationGrid.DefaultCapacity +
                                       " cells); ground beyond it reads unscouted.");
                }
                AirDefenceRing[] swap = front;
                front = back;
                back = swap;
                RingCount = count;
                int next = Signature(front, count);
                if (!Ready || next != signature)
                {
                    signature = next;
                    Version++;
                }
                Ready = true;
            }
            catch (Exception e)
            {
                Fault(e);
            }
        }

        private void OnDiscover(PersistentID id)
        {
            expectedTracking++;
            try
            {
                FactionHQ hq = Hq;
                if (hq == null || hq.trackingDatabase == null) return;
                if (hq.trackingDatabase.TryGetValue(id, out TrackingInfo info))
                    Admit(id, info, Time.timeSinceLevelLoad);
            }
            catch (Exception e)
            {
                // Never throw into vanilla's tracking RPC.
                Fault(e);
            }
        }

        private void OnForget(PersistentID id)
        {
            expectedTracking--;
            try
            {
                // Vanilla forgets a tracked unit when it dies or changes side. A pre-war site that
                // dies while tracked therefore leaves here: the observer saw it go.
                known.Remove(id.Id);
            }
            catch (Exception e)
            {
                Fault(e);
            }
        }

        private void Admit(PersistentID id, TrackingInfo info, float level)
        {
            if (info == null || !info.TryGetUnit(out Unit unit) || unit == null) return;
            UnitClass unitClass = UnitProfiles.ClassOf(unit);
            if (unitClass == UnitClass.Missile)
            {
                RecordLaunch((Missile)unit, level);
                return;
            }
            if (unitClass == UnitClass.Other) return;
            FactionHQ owner = unit.NetworkHQ;
            if (owner == null || ReferenceEquals(owner, Hq)) return;
            short profileIndex = profiles.IndexOf(unit);
            UnitProfile profile = profileIndex >= 0 ? profiles[profileIndex] : default;
            if (!KnownHostileFilter.Admits(unitClass, profile.HasWeapon || profile.Air.RadarEmitter)) return;
            bool isStatic = IsStatic(unit, unitClass);
            bool airDefence = KnownHostileFilter.IsSurface(unitClass) && profile.Air.IsAirDefence;
            GlobalPosition position = info.lastKnownPosition;
            Note(known.Upsert(new KnownHostile
            {
                Id = id.Id,
                X = position.x,
                Z = position.z,
                SpottedAt = info.lastSpottedTime,
                Priority = KnownHostileFilter.Priority(airDefence, isStatic),
                Static = isStatic,
                Class = unitClass,
                Role = profile.Role,
                Profile = profileIndex,
                PreWar = false,
                Confirmed = true,
                Emitting = false,
                DeadSince = float.NaN
            }));
        }

        /// <summary>
        /// A missile entered this faction's tracking (MissileWarning pushes it there). If a
        /// surface unit of another faction fired an anti-air missile, remember where from.
        /// </summary>
        private void RecordLaunch(Missile missile, float level)
        {
            if (!UnitRegistry.TryGetUnit(missile.ownerID, out Unit owner) || owner == null || owner.disabled) return;
            FactionHQ ownerHq = owner.NetworkHQ;
            if (ownerHq == null || ReferenceEquals(ownerHq, Hq)) return;
            UnitClass ownerClass = UnitProfiles.ClassOf(owner);
            if (!KnownHostileFilter.IsSurface(ownerClass)) return;
            WeaponInfo weapon = missile.GetWeaponInfo();
            if (weapon == null) return;
            TargetRequirements requirements = weapon.targetRequirements;
            if (!(weapon.effectiveness.antiAir >= AirDefenceProfile.MinimumAntiAir) ||
                !(requirements.maxAltitude > AirDefenceProfile.MinimumCeiling))
                return;
            GlobalPosition at = owner.GlobalPosition();
            launches.Record(owner.persistentID.Id, at.x, at.z, IsStatic(owner, ownerClass), requirements.maxRange,
                requirements.minAltitude, requirements.maxAltitude, level);
        }

        private bool DeadAndSeen(ref KnownHostile entry, Unit unit, float level)
        {
            if (unit != null && !unit.disabled)
            {
                entry.DeadSince = float.NaN;
                return false;
            }
            if (float.IsNaN(entry.DeadSince)) entry.DeadSince = level;
            return PreWarRules.ShouldDrop(entry.DeadSince, grid.LastStamp(entry.X, entry.Z, 0f));
        }

        private RingInput ToInput(in KnownHostile entry, float level)
        {
            AirDefenceProfile air = profiles[entry.Profile].Air;
            float age = level - entry.SpottedAt > 0f ? level - entry.SpottedAt : 0f;
            RingSource source = entry.PreWar ? RingSource.PreWar : RingSource.Tracked;
            return new RingInput
            {
                X = entry.X,
                Z = entry.Z,
                MaxRange = air.MaxRange,
                MinAltitude = air.MinAltitude,
                MaxAltitude = air.MaxAltitude,
                Kind = air.HasBand ? air.Kind : AirDefenceKind.Sensor,
                OwnRadar = air.RadarEmitter,
                Emitting = entry.Emitting,
                Source = source,
                Confirmed = entry.Confirmed,
                AgeSeconds = age,
                Stale = RingSet.IsStale(source, entry.Static, age),
                UnitHash = (int)entry.Id
            };
        }

        private void Note(UpsertResult result)
        {
            if ((result != UpsertResult.Evicted && result != UpsertResult.Refused) || tableFullLogged) return;
            tableFullLogged = true;
            Plugin.Logger?.LogWarning("Intel: " + name + " knows more than " + known.Capacity +
                               " hostiles; the oldest static non-air-defence entries give way first.");
        }

        private void Fault(Exception e)
        {
            if (faultLogged) return;
            faultLogged = true;
            Plugin.Logger?.LogError("Intel: " + name + " picture fault (logged once per scene): " + e);
        }

        private static int Signature(AirDefenceRing[] rings, int count)
        {
            unchecked
            {
                int hash = count;
                for (int i = 0; i < count; i++)
                {
                    AirDefenceRing ring = rings[i];
                    hash = hash * 31 + (int)(ring.X / 100f);
                    hash = hash * 31 + (int)(ring.Z / 100f);
                    hash = hash * 31 + (int)ring.Kind;
                    hash = hash * 31 + (int)ring.Source;
                    hash = hash * 31 + ring.Launchers;
                    hash = hash * 31 + (ring.Stale ? 1 : 0) + (ring.Confirmed ? 2 : 0) +
                           (ring.Emitting ? 4 : 0) + (ring.RadarGuided ? 8 : 0);
                }
                return hash;
            }
        }
    }
}
