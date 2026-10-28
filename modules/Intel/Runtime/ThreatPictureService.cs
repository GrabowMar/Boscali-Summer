using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Intel.Configuration;
using BoscaliSummer.Modules.Intel.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Intel.Runtime
{
    /// <summary>
    /// Publishes IThreatPicture. The host keeps one picture for every non-neutral faction that
    /// owns units (at most eight), whatever its staff does, and rebuilds one faction every 2 s
    /// in turn, never two in one frame. A client keeps only its own faction's picture: it is
    /// attached when something first asks and rebuilt at 1 Hz only while something keeps
    /// asking. Nothing crosses the wire: vanilla already replicates each faction's tracking.
    /// </summary>
    internal sealed class ThreatPictureService : MonoBehaviour, ISceneService, IThreatPicture
    {
        private const int MaximumFactions = 8;
        private const float AuthorityInterval = 1f;
        private const float HostRebuildSeconds = 2f;
        private const float ClientRebuildSeconds = 1f;
        private const float ObservationSeconds = 5f;
        private const float DemandWindowSeconds = 2f;
        private const float RebuildStaggerSeconds = 0.25f;
        private const int MaximumObservationUnits = 4096;
        private const int ObservationUnitsPerFrame = 512;

        private readonly FactionPicture[] pictures = new FactionPicture[MaximumFactions];
        private readonly UnitProfiles profiles = new UnitProfiles();

        private IntelSettings settings;
        private int pictureCount;
        private int rebuildCursor;
        private bool server;
        private bool failed;
        private float nextAuthority;
        private float nextObservation;
        private int observationCursor;
        private float observationLevel;
        private float missionStart = float.NaN;
        private float lastDemand = float.NegativeInfinity;

        public void Configure(IntelSettings config)
        {
            settings = config;
            for (int i = 0; i < pictures.Length; i++) pictures[i] = new FactionPicture(profiles);
        }

        public void ResetForScene()
        {
            DetachAll();
            profiles.Clear();
            server = false;
            failed = false;
            nextAuthority = 0f;
            nextObservation = 0f;
            observationCursor = 0;
            missionStart = float.NaN;
            lastDemand = float.NegativeInfinity;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (settings == null || failed || !MissionManager.IsRunning) return;
            float now = Time.unscaledTime;
            float level = Time.timeSinceLevelLoad;
            try
            {
                if (float.IsNaN(missionStart)) missionStart = level;
                if (now >= nextAuthority)
                {
                    nextAuthority = now + AuthorityInterval;
                    server = GameAccess.IsServer();
                    AttachPictures(level, now);
                }
                if (pictureCount == 0) return;
                if (!server && now - lastDemand > DemandWindowSeconds) return;

                if (level >= missionStart + PreWarRules.SeedDelaySeconds && !AllPreWarSeeded()) SeedPreWar(level);
                if (observationCursor > 0 || now >= nextObservation)
                {
                    if (observationCursor == 0) nextObservation = now + ObservationSeconds;
                    ObservationPass(level);
                }
                RebuildOneDue(now, level);
            }
            catch (Exception e)
            {
                // Fail closed for the rest of the scene: every read now answers "no picture",
                // and every consumer falls back to what it did before Intel existed.
                failed = true;
                DetachAll();
                Plugin.Logger?.LogError("Intel threat picture stopped for this scene: " + e);
            }
        }

        public bool IsReady(int observer)
        {
            FactionPicture picture = Find(observer);
            return picture != null && picture.Ready;
        }

        public int CopyAirDefence(int observer, AirDefenceRing[] into)
        {
            FactionPicture picture = Find(observer);
            if (picture == null || !picture.Ready || into == null) return 0;
            int count = picture.RingCount < into.Length ? picture.RingCount : into.Length;
            Array.Copy(picture.Rings, into, count);
            return count;
        }

        public bool TryGetCoverage(int observer, float x, float z, float agl, out float depthMetres, out int rings)
        {
            depthMetres = float.NaN;
            rings = -1;
            FactionPicture picture = Find(observer);
            if (picture == null || !picture.Ready || !float.IsFinite(x) || !float.IsFinite(z) || !float.IsFinite(agl)) return false;
            RingGeometry.Coverage(picture.Rings, picture.RingCount, x, z, agl, ThreatPictureLimits.AllRingsMask,
                out depthMetres, out rings, out _);
            return true;
        }

        public bool TryGetAreaIntel(int observer, float x, float z, float radius, out AreaIntel intel)
        {
            intel = AreaIntel.Unknown;
            FactionPicture picture = Find(observer);
            if (picture == null || !picture.Ready || !float.IsFinite(x) || !float.IsFinite(z) || !(radius >= 0f)) return false;
            intel = AreaIntelReader.Read(picture.Known, picture.Grid, x, z, radius, Time.timeSinceLevelLoad);
            return true;
        }

        // ---- Pictures ----------------------------------------------------------------------

        /// <summary>
        /// The observer's picture, or null. On a client every read is also a request: the
        /// local faction's picture is attached and kept current only while something asks.
        /// </summary>
        private FactionPicture Find(int observer)
        {
            if (!server) lastDemand = Time.unscaledTime;
            for (int i = 0; i < pictureCount; i++)
                if (pictures[i].Id == observer && pictures[i].Hq != null) return pictures[i];
            return null;
        }

        private void AttachPictures(float level, float now)
        {
            if (server)
            {
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (pictureCount >= MaximumFactions) break;
                    if (!Eligible(hq) || hq.factionUnits.Count == 0 || IndexOf(hq) >= 0) continue;
                    Attach(hq, level, now);
                }
                return;
            }

            // A client: its own faction only, attached on the first request. A faction change
            // detaches and re-attaches, which re-seeds from the new faction's tracking.
            if (pictureCount == 0 && now - lastDemand > DemandWindowSeconds) return;
            FactionHQ local = GameManager.GetLocalHQ(out FactionHQ found) ? found : null;
            if (pictureCount > 0 && !ReferenceEquals(pictures[0].Hq, local)) DetachAll();
            if (pictureCount == 0 && Eligible(local)) Attach(local, level, now);
        }

        private static bool Eligible(FactionHQ hq) =>
            hq != null && hq.faction != null && !FactionHelper.EmptyOrNoFactionOrNeutral(hq.faction.factionName);

        private void Attach(FactionHQ hq, float level, float now)
        {
            FactionPicture picture = pictures[pictureCount];
            picture.Attach(hq, level);
            // Stagger first rebuilds so two factions never rebuild in the same frame.
            picture.NextRebuild = now + RebuildStaggerSeconds * pictureCount;
            pictureCount++;
        }

        private void DetachAll()
        {
            for (int i = 0; i < pictures.Length; i++) pictures[i]?.Detach();
            pictureCount = 0;
            rebuildCursor = 0;
        }

        private int IndexOf(FactionHQ hq)
        {
            for (int i = 0; i < pictureCount; i++)
                if (ReferenceEquals(pictures[i].Hq, hq)) return i;
            return -1;
        }

        private void RebuildOneDue(float now, float level)
        {
            float interval = server ? HostRebuildSeconds : ClientRebuildSeconds;
            for (int k = 0; k < pictureCount; k++)
            {
                int i = (rebuildCursor + k) % pictureCount;
                FactionPicture picture = pictures[i];
                if (now < picture.NextRebuild) continue;
                picture.NextRebuild = now + interval;
                picture.Rebuild(level);
                rebuildCursor = (i + 1) % pictureCount;
                return;
            }
        }

        private bool AllPreWarSeeded()
        {
            for (int p = 0; p < pictureCount; p++)
                if (!pictures[p].PreWarSeeded) return false;
            return true;
        }

        private void SeedPreWar(float level)
        {
            if (settings.PreWarIntel.Value)
            {
                int seeded = PreWarSeeder.Seed(pictures, pictureCount, profiles, level);
                Plugin.Logger?.LogInfo("Intel: pre-war intel gave " + seeded + " fixed air-defence entries to " +
                                pictureCount + " faction picture(s).");
            }
            for (int p = 0; p < pictureCount; p++) pictures[p].PreWarSeeded = true;
        }

        /// <summary>
        /// One shared pass over at most 4096 units every 5 s: each picture's own units stamp the
        /// ground they can see — ground units 6 km, aircraft 10 km, radars 15 km.
        /// </summary>
        private void ObservationPass(float level)
        {
            // Spread over frames, ObservationUnitsPerFrame units at a time. The grids are stamped in place with a
            // max-time rule, so a half-finished pass only leaves older stamps on units not reached yet.
            if (observationCursor == 0)
            {
                for (int p = 0; p < pictureCount; p++) pictures[p].Grid.BeginPass();
                observationLevel = level;
            }
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null) { observationCursor = 0; return; }
            int limit = all.Count < MaximumObservationUnits ? all.Count : MaximumObservationUnits;
            int end = Math.Min(limit, observationCursor + ObservationUnitsPerFrame);
            int start = observationCursor;
            observationCursor = end < limit ? end : 0;
            level = observationLevel;
            for (int i = start; i < end; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled) continue;
                int p = IndexOf(unit.NetworkHQ);
                if (p < 0) continue;
                float radius = StampRadius(unit);
                if (!(radius > 0f)) continue;
                GlobalPosition at = unit.GlobalPosition();
                pictures[p].Grid.Stamp(at.x, at.z, radius, level);
            }
        }

        private static float StampRadius(Unit unit)
        {
            if (unit is Aircraft) return ObservationGrid.AircraftRadius;
            if (unit is Missile) return 0f;
            if (unit.radar is Radar && unit.definition != null && unit.definition.typeIdentity.radar > 0f)
                return ObservationGrid.RadarRadius;
            return unit is GroundVehicle || unit is Ship ? ObservationGrid.GroundRadius : 0f;
        }

    }
}
