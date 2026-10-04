using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>One faction's host sensor admission. Opaque ids never authorize a registry search.</summary>
    internal sealed class SpaceObservations : IDisposable
    {
        private const int MaximumIndexed = SpaceContacts.MaxReveals + SpaceContacts.MaxMarks;
        private const int MaximumNativeUnits = 4096, MaximumTypes = 128;
        private sealed class Entry
        {
            public int Id;
            public Unit Unit;
            public TrackingInfo Track;
            public Action Spotted;
            public float NativeStamp;
        }

        private readonly FactionHQ owner;
        private readonly Dictionary<PersistentID, Entry> indexed = new Dictionary<PersistentID, Entry>();
        private readonly Dictionary<int, Entry> ids = new Dictionary<int, Entry>();
        private readonly Dictionary<UnitDefinition, int> types = new Dictionary<UnitDefinition, int>();
        private readonly SpaceRevealWindow[] windows = new SpaceRevealWindow[2];
        private readonly List<PersistentID> expired = new List<PersistentID>(MaximumIndexed);
        private int nextId, nextType;
        private bool disposed;
        public SpaceContacts Contacts { get; } = new SpaceContacts();

        public SpaceObservations(FactionHQ faction)
        {
            owner = faction;
            owner.onDiscoverUnit += Discover;
            owner.onForgetUnit += Forget;
            owner.onRemoveUnit += RemoveOwn;
        }

        /// <summary>Called only by a paid, host-validated sensor task; UI entry never calls this.</summary>
        public int Open(GlobalPosition center, float radius, BirdKind source, float minimumSpeed, float maximumSpeed)
        {
            float now = SupportManager.MissionNow();
            if (disposed || owner == null || !GameAccess.IsServer() ||
                !SpaceRevealWindow.TryCreate(source, center.x, center.z, radius, now, SpaceRevealWindow.ObservationSeconds,
                    minimumSpeed, maximumSpeed, out SpaceRevealWindow window)) return -1;
            var units = UnitRegistry.allUnits;
            if (units == null || units.Count > MaximumNativeUnits) return -1;
            // The RADAR bird stays busy for BirdBusySeconds (SupportCatalog), so one window per bird never
            // overwrites a live scan/MTI footprint.
            windows[(int)source] = window;
            Tick(now);
            int admitted = 0;
            for (int i = 0; i < units.Count && Contacts.Count < SpaceContacts.MaxReveals; i++)
            {
                Unit unit = units[i];
                if (!Ground(unit)) continue;
                GlobalPosition point = unit.transform.position.ToGlobalPosition();
                if (!window.Contains(point.x, point.z, unit.speed, now) || !VisibleSurface(unit)) continue;
                // This accepted task is an actual new sensor observation, including indexed native scenery.
                if (unit.NetworkHQ != null && unit.NetworkHQ != owner)
                {
                    try { owner.RpcUpdateTrackingInfo(unit.persistentID); }
                    catch (Exception e) { Plugin.Logger?.LogWarning("[Support.Space] Sensor observation refused: " + e.Message); continue; }
                }
                if (Observe(unit, point, now, window.Source)) admitted++;
            }
            return admitted;
        }

        public void Tick(float now)
        {
            if (disposed || owner == null || !GameAccess.IsServer()) return;
            Contacts.Prune(now);
            expired.Clear();
            foreach (var pair in indexed)
            {
                Entry entry = pair.Value;
                if (Contacts.TryReveal(entry.Id, now, out SpaceContact contact))
                {
                    SpaceRevealWindow window = windows[(int)contact.Source];
                    if (!Ground(entry.Unit) || !window.Contains(contact.X, contact.Z,
                        contact.Moving ? Math.Max(4f, window.MinimumSpeed) : 0f, now)) Contacts.Forget(entry.Id);
                    else if (entry.Unit.NetworkHQ == owner && contact.Classification != ContactClass.Friendly)
                        Contacts.Reveal(entry.Id, ContactClass.Friendly, contact.TypeId, contact.X, contact.Z,
                            contact.Moving, contact.ObservedAt, contact.Source);
                }
                if (!Contacts.TryReveal(entry.Id, now, out _) && !Contacts.TryMark(entry.Id, now, out _)) expired.Add(pair.Key);
            }
            for (int i = 0; i < expired.Count; i++) RemoveIndex(expired[i]);
            expired.Clear();
            // Native late-join batches can insert without either event. Attach handlers without fabricating a sighting.
            if (owner.trackingDatabase == null || owner.trackingDatabase.Count > MaximumNativeUnits) return;
            foreach (var pair in owner.trackingDatabase)
            {
                if (!indexed.TryGetValue(pair.Key, out Entry entry)) continue;
                Attach(entry, pair.Value);
                TrackingInfo track = entry.Track;
                if (track != null && track.lastSpottedTime != entry.NativeStamp) Spotted(entry);
            }
        }

        public MarkVerdict Mark(ulong player, int id, bool recentInput)
        {
            if (disposed || !GameAccess.IsServer()) return MarkVerdict.NoContact;
            float now = SupportManager.MissionNow();
            Tick(now);
            return Contacts.Mark(player, id, now, recentInput);
        }

        /// <summary>The native ground PersistentID.Id (uint) behind an admitted contact id; 0 when it has none. Never an aircraft.</summary>
        public uint UnitIdOf(int id) =>
            ids.TryGetValue(id, out Entry entry) && entry.Unit != null && Ground(entry.Unit) ? entry.Unit.persistentID.Id : 0u;

        /// <summary>Only currently admitted units may contribute pixels. A surviving MARK is a ground point.</summary>
        public bool TryApprovedUnit(int id, float now, out Unit unit)
        {
            unit = null;
            if (disposed || !Contacts.TryReveal(id, now, out _) || !ids.TryGetValue(id, out Entry entry) || !Ground(entry.Unit)) return false;
            unit = entry.Unit;
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (owner != null)
            {
                owner.onDiscoverUnit -= Discover;
                owner.onForgetUnit -= Forget;
                owner.onRemoveUnit -= RemoveOwn;
            }
            foreach (Entry entry in indexed.Values) Detach(entry);
            indexed.Clear(); ids.Clear(); types.Clear(); expired.Clear(); Contacts.Clear();
        }

        private void Discover(PersistentID id)
        {
            if (disposed || owner.trackingDatabase == null || !owner.trackingDatabase.TryGetValue(id, out TrackingInfo track) ||
                track == null || !track.TryGetUnit(out Unit unit)) return;
            ObserveKnown(unit, track);
        }

        private void Forget(PersistentID id)
        {
            if (!indexed.TryGetValue(id, out Entry entry)) return;
            Contacts.Forget(entry.Id); Detach(entry);
        }

        private void RemoveOwn(Unit unit) { if (unit != null) Forget(unit.persistentID); }

        private void Spotted(Entry entry)
        {
            TrackingInfo track = entry.Track;
            if (track == null) return;
            entry.NativeStamp = track.lastSpottedTime;
            ObserveKnown(entry.Unit, track);
        }

        private void ObserveKnown(Unit unit, TrackingInfo track)
        {
            if (disposed || !GameAccess.IsServer() || !Ground(unit) ||
                !SpaceRevealWindow.NativeFresh(Time.timeSinceLevelLoad, track.lastSpottedTime)) return;
            float now = SupportManager.MissionNow();
            GlobalPosition point = track.lastKnownPosition; // Never replace an aged sighting with hidden live coordinates.
            for (int i = 0; i < windows.Length; i++)
                if (windows[i].Contains(point.x, point.z, unit.speed, now))
                { Observe(unit, point, now, windows[i].Source); return; }
        }

        /// <summary>True only when this call newly admitted a contact (a refreshed live reveal is not new).</summary>
        private bool Observe(Unit unit, GlobalPosition point, float now, BirdKind source)
        {
            if (!Ground(unit) || unit.persistentID.Id == 0) return false;
            UnitDefinition definition = unit.definition;
            int type = 0;
            if (definition != null && !types.TryGetValue(definition, out type))
            {
                if (types.Count >= MaximumTypes) return false;
                types.Add(definition, type = ++nextType);
            }
            bool newEntry = !indexed.TryGetValue(unit.persistentID, out Entry entry);
            if (newEntry)
            {
                if (indexed.Count >= MaximumIndexed || nextId == int.MaxValue) return false;
                entry = new Entry { Id = ++nextId, Unit = unit };
            }
            ContactClass classification = unit.NetworkHQ == owner ? ContactClass.Friendly :
                unit.NetworkHQ == null ? ContactClass.Neutral : ContactClass.EnemyGround;
            bool fresh = newEntry || !Contacts.TryReveal(entry.Id, now, out _);
            if (!Contacts.Reveal(entry.Id, classification, type, point.x, point.z, unit.speed >= 4f, now, source)) return false;
            if (newEntry) { indexed.Add(unit.persistentID, entry); ids.Add(entry.Id, entry); }
            if (owner.trackingDatabase != null && owner.trackingDatabase.TryGetValue(unit.persistentID, out TrackingInfo track)) Attach(entry, track);
            return fresh;
        }

        private void Attach(Entry entry, TrackingInfo track)
        {
            if (track == null || ReferenceEquals(entry.Track, track)) return;
            Detach(entry);
            entry.Track = track; entry.NativeStamp = track.lastSpottedTime;
            entry.Spotted = () =>
            {
                try { Spotted(entry); }
                catch (Exception e) { Plugin.Logger?.LogWarning("[Support.Space] Observation update refused: " + e.Message); }
            };
            track.OnSpotted += entry.Spotted;
        }

        private static void Detach(Entry entry)
        {
            if (entry.Track != null && entry.Spotted != null) entry.Track.OnSpotted -= entry.Spotted;
            entry.Track = null; entry.Spotted = null;
        }

        private void RemoveIndex(PersistentID id)
        {
            if (!indexed.TryGetValue(id, out Entry entry)) return;
            Detach(entry); indexed.Remove(id); ids.Remove(entry.Id);
        }

        private static bool Ground(Unit unit) => unit != null && !unit.disabled && unit.gameObject.activeInHierarchy &&
            !(unit is Aircraft) && !(unit is Missile);

        private static bool VisibleSurface(Unit unit)
        {
            Vector3 point = unit.transform.position;
            Vector3 start = point + Vector3.up * 20000f;
            // Reuse vanilla's target-radius-aware occlusion. Magnification models the orbital sensor,
            // while the bounded host footprint supplies its acquisition range. Task7 adds optical weather.
            return unit.LineOfSight(start, 1000f);
        }
    }
}
