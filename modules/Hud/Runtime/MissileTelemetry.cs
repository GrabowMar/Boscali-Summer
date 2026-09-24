using System.Collections.Generic;
using BoscaliSummer.Features.Hud.Domain;
using UnityEngine;
namespace BoscaliSummer.Features.Hud.Runtime
{
    // Reads only owned shots and native warning-system detections. Never discovers enemy tracks.
    internal sealed class MissileTelemetry
    {
        private const int MaxOutbound = 3, MaxInbound = 3, MaxTracks = 12;
        private const float ShotSampleSeconds = .25f;
        private float lastShotRead = -99f;
        private int shownOutbound, shownInbound;
        private long nextOrder;
        public IReadOnlyList<ShotEntry> Shown => shown;
        public int Inbound => shownInbound;
        public int Outbound => shownOutbound;
        internal sealed class ShotEntry
        {
            public Missile Missile;
            public bool Outbound;
            public ShotTrack Track;
            public string Seeker = "MSL", Target = "TGT", RangeText = "--";
            public float LastSeen;
            public long DisplayOrder;
        }
        private struct ShotPick { public Missile Missile; public float Range; public string Target; }
        private readonly List<ShotEntry> tracks = new List<ShotEntry>(MaxTracks);
        private readonly List<ShotEntry> shown = new List<ShotEntry>(MaxOutbound + MaxInbound);
        private readonly ShotPick[] outPicks = new ShotPick[MaxOutbound], inPicks = new ShotPick[MaxInbound];

        public void Read(Aircraft aircraft, bool wanted)
        {
            float now = Time.unscaledTime;
            if (!wanted || aircraft == null)
            {
                tracks.Clear();
                shown.Clear();
                ClearPicks(outPicks);
                ClearPicks(inPicks);
                shownOutbound = shownInbound = 0;
                lastShotRead = -99f;
                nextOrder = 0;
                return;
            }
            if (now - lastShotRead < ShotSampleSeconds && lastShotRead >= 0f) return;
            lastShotRead = now;

            for (int i = tracks.Count - 1; i >= 0; i--)
                if (tracks[i].Missile == null) tracks.RemoveAt(i);
            ClearPicks(outPicks);
            ClearPicks(inPicks);

            List<Unit> all = UnitRegistry.allUnits;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    Missile missile = all[i] as Missile;
                    if (missile == null || missile.transform == null || missile.owner != aircraft) continue;
                    if (!UnitRegistry.TryGetPersistentUnit(missile.targetID, out PersistentUnit target) ||
                        target == null || target.unit == null || target.unit.transform == null) continue;
                    float range = Vector3.Distance(missile.transform.position, target.unit.transform.position);
                    if (float.IsNaN(range) || float.IsInfinity(range) || range < 0f) continue;
                    Offer(outPicks, missile, range, ShotCopy.ShortName(target.unitName));
                }
            }

            MissileWarning warning = aircraft.GetMissileWarningSystem();
            if (warning != null && warning.knownMissiles != null)
            {
                Vector3 own = aircraft.transform.position;
                List<Missile> known = warning.knownMissiles;
                for (int i = 0; i < known.Count; i++)
                {
                    Missile missile = known[i];
                    if (missile == null || missile.transform == null) continue;
                    float range = Vector3.Distance(missile.transform.position, own);
                    if (float.IsNaN(range) || float.IsInfinity(range) || range < 0f) continue;
                    Offer(inPicks, missile, range, null);
                }
            }

            shown.Clear();
            shownOutbound = shownInbound = 0;
            for (int i = 0; i < outPicks.Length; i++)
            {
                if (outPicks[i].Missile == null) continue;
                ShotEntry entry = FindOrAddTrack(outPicks[i].Missile, true, outPicks[i].Range, now);
                entry.Target = outPicks[i].Target ?? entry.Target;
                entry.RangeText = UnitConverter.DistanceReading(outPicks[i].Range);
                shown.Add(entry);
                shownOutbound++;
            }
            for (int i = 0; i < inPicks.Length; i++)
            {
                if (inPicks[i].Missile == null) continue;
                ShotEntry entry = FindOrAddTrack(inPicks[i].Missile, false, inPicks[i].Range, now);
                entry.RangeText = UnitConverter.DistanceReading(inPicks[i].Range);
                shown.Add(entry);
                shownInbound++;
            }
            // Pick the nearest threats, but do not swap surviving display rows at every range crossing.
            shown.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
        }

        /// <summary>Nearest-first insertion into a fixed pick array; nothing allocated.</summary>
        private static void Offer(ShotPick[] picks, Missile missile, float range, string target)
        {
            for (int i = 0; i < picks.Length; i++)
            {
                if (picks[i].Missile != null && picks[i].Range <= range) continue;
                for (int j = picks.Length - 1; j > i; j--) picks[j] = picks[j - 1];
                picks[i].Missile = missile;
                picks[i].Range = range;
                picks[i].Target = target;
                return;
            }
        }

        private static void ClearPicks(ShotPick[] picks)
        {
            for (int i = 0; i < picks.Length; i++) picks[i] = default(ShotPick);
        }

        /// <summary>
        /// The track behind a picked missile, started on first sight. Keyed by missile and side,
        /// so the same rock can never serve two countdowns; past the track ceiling the stalest
        /// track is evicted rather than letting a new shot starve.
        /// </summary>
        private ShotEntry FindOrAddTrack(Missile missile, bool outbound, float range, float now)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                ShotEntry entry = tracks[i];
                if (entry.Missile == missile && entry.Outbound == outbound)
                {
                    entry.Track = ShotMath.Update(entry.Track, range, now);
                    entry.LastSeen = now;
                    return entry;
                }
            }
            if (tracks.Count >= MaxTracks) EvictTrack();
            string seeker;
            try { seeker = missile.GetSeekerType(); }
            catch (System.Exception) { seeker = null; }
            var fresh = new ShotEntry
            {
                Missile = missile,
                Outbound = outbound,
                Track = ShotMath.Start(range, now),
                Seeker = ShotCopy.SeekerTag(seeker),
                LastSeen = now,
                DisplayOrder = nextOrder++
            };
            tracks.Add(fresh);
            return fresh;
        }

        /// <summary>Drop the stalest track; the list is never empty when this runs.</summary>
        private void EvictTrack()
        {
            int oldest = 0;
            for (int i = 1; i < tracks.Count; i++)
                if (tracks[i].LastSeen < tracks[oldest].LastSeen) oldest = i;
            tracks.RemoveAt(oldest);
        }

    }
}
