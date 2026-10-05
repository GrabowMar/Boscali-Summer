using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Support's own read of the local cockpit's warnings for the SPACE feed. It reads only verified native state and has no
    /// dependency on the optional Autopilot module, so disabling that module cannot remove a feed warning.
    ///
    /// <para>Verified against Assembly-CSharp 2026-10-05:</para>
    /// <list type="bullet">
    /// <item><c>Aircraft.onRadarWarning</c> is raised by <c>UserCode_RpcGetRadarWarning</c> for every report of an emitter, so a
    /// steady lock repeats. The probe keeps its own bounded emitter table and sets a short spike only for a new emitter or a rising
    /// detected/target edge. It never calls <c>Aircraft.KnownRadarWarning</c>, which mutates the game's known-emitter set and would
    /// change which sound the vanilla receiver plays.</item>
    /// <item><c>RadarWarning</c> (the native receiver) already plays <c>SoundManager.PlayRadarWarningOneShot</c> on those same
    /// events. The feed adds no sound of its own and mutes nothing; audibility with the feed open is a Task 10 live check.</item>
    /// <item><c>MissileWarning.IsWarning()</c> is polled (no event needed).</item>
    /// <item><c>TerrainWarningSystem.urgency</c> is only refreshed by the autopilot's own tick, so a manually flown aircraft would
    /// show a stale value. The probe asks <c>CheckTerrain()</c> (self-throttled to 2 Hz by the game) before reading it.</item>
    /// <item>Bandits are the local faction's known hostile aircraft in <c>trackingDatabase</c>, spotted within ten seconds.
    /// There is no scan of all enemy units.</item>
    /// </list>
    /// </summary>
    internal static class SpaceCockpitThreatProbe
    {
        public const float SpikeHoldSeconds = 4f;
        private const float PollSeconds = 0.25f, BanditFreshSeconds = 10f, EmitterForgetSeconds = 30f, ReLockSilenceSeconds = 6f;
        private const int MaxEmitters = 32, MaxTracksExamined = 256;

        private struct Emitter { public bool Engaged; public float Seen; }

        private static readonly Dictionary<int, Emitter> Emitters = new Dictionary<int, Emitter>(MaxEmitters);
        private static readonly List<int> Expired = new List<int>(MaxEmitters);
        private static Aircraft subscribed;
        private static float spikeUntil = -1f, nextPoll = -1f;
        private static CockpitThreatSnapshot cached;
        private static bool haveCached;

        /// <summary>
        /// The current cockpit picture. False only when there is no local player at all; a ground operator returns true with
        /// <c>ValidOwnship</c> false and no warnings. Cached for a quarter second, so every frame may call it.
        /// </summary>
        public static bool TryRead(out CockpitThreatSnapshot snapshot)
        {
            float wall = Time.unscaledTime;
            if (haveCached && wall < nextPoll) { snapshot = cached; return true; }
            nextPoll = wall + PollSeconds;
            snapshot = default;
            Player player;
            if (!GameManager.GetLocalPlayer(out player) || player == null || player.HQ == null)
            {
                Unsubscribe();
                cached = new CockpitThreatSnapshot(false, false, false, float.PositiveInfinity, 0f, false);
                haveCached = true;
                snapshot = cached;
                return false;
            }
            Aircraft aircraft = null;
            bool airborne = GameManager.GetLocalAircraft(out aircraft) && aircraft != null && !aircraft.disabled;
            if (!airborne)
            {
                Unsubscribe();
                cached = new CockpitThreatSnapshot(false, false, false, float.PositiveInfinity, 0f, true);
                haveCached = true;
                snapshot = cached;
                return true;
            }
            Subscribe(aircraft);
            float now = SupportManager.MissionNow();
            bool rwr = now < spikeUntil;
            bool missile = false;
            try { MissileWarning warning = aircraft.GetMissileWarningSystem(); missile = warning != null && warning.IsWarning(); }
            catch (Exception) { missile = false; }
            float terrain = 0f;
            try
            {
                TerrainWarningSystem system = aircraft.autopilot != null ? aircraft.autopilot.GetTerrainWarningSystem() : null;
                if (system != null && aircraft.rb != null && aircraft.cockpit != null) { system.CheckTerrain(); terrain = system.urgency; }
            }
            catch (Exception) { terrain = 0f; }
            cached = new CockpitThreatSnapshot(true, rwr, missile, NearestKnownBandit(player, aircraft), terrain, true);
            haveCached = true;
            snapshot = cached;
            return true;
        }

        /// <summary>Scene change or teardown: drop the subscription, the emitter table and every cached value.</summary>
        public static void ResetForScene()
        {
            Unsubscribe();
            Emitters.Clear();
            spikeUntil = -1f;
            nextPoll = -1f;
            haveCached = false;
        }

        // ---- RWR ---------------------------------------------------------------------------------------------

        private static void Subscribe(Aircraft aircraft)
        {
            if (ReferenceEquals(subscribed, aircraft)) return;
            Unsubscribe();
            subscribed = aircraft;
            aircraft.onRadarWarning += OnRadarWarning;
        }

        private static void Unsubscribe()
        {
            if (subscribed != null) subscribed.onRadarWarning -= OnRadarWarning;
            subscribed = null;
            Emitters.Clear();
            spikeUntil = -1f;
        }

        private static void OnRadarWarning(Aircraft.OnRadarWarning report)
        {
            try
            {
                if (report.emitter == null) return;
                float now = SupportManager.MissionNow();
                if (!SpaceRules.MissionTime(now)) return;
                int key = report.emitter.GetInstanceID();
                bool engaged = report.detected || report.isTarget;
                bool spike;
                if (Emitters.TryGetValue(key, out Emitter known))
                {
                    // A rising edge, not a steady repeat; an emitter silent for a while and then re-reported is a new lock.
                    spike = (engaged && !known.Engaged) || now - known.Seen > ReLockSilenceSeconds;
                    Emitters[key] = new Emitter { Engaged = engaged, Seen = now };
                }
                else
                {
                    spike = true; // a newly reported emitter
                    if (Emitters.Count >= MaxEmitters) PruneEmitters(now);
                    if (Emitters.Count < MaxEmitters) Emitters[key] = new Emitter { Engaged = engaged, Seen = now };
                }
                if (spike) spikeUntil = now + SpikeHoldSeconds;
            }
            catch (Exception) { /* a warning read must never throw into the game's event */ }
        }

        /// <summary>Evicts emitters not reported for a while; never clears a table of live ones.</summary>
        private static void PruneEmitters(float now)
        {
            Expired.Clear();
            foreach (KeyValuePair<int, Emitter> pair in Emitters)
                if (now - pair.Value.Seen > EmitterForgetSeconds) Expired.Add(pair.Key);
            for (int i = 0; i < Expired.Count; i++) Emitters.Remove(Expired[i]);
            Expired.Clear();
        }

        // ---- Bandits -----------------------------------------------------------------------------------------

        private static float NearestKnownBandit(Player player, Aircraft own)
        {
            FactionHQ hq = player.HQ;
            if (hq == null || hq.trackingDatabase == null) return float.PositiveInfinity;
            GlobalPosition here = own.transform.GlobalPosition();
            float nativeNow = Time.timeSinceLevelLoad; // TrackingInfo.lastSpottedTime stays in this native domain
            float best = float.PositiveInfinity;
            int examined = 0;
            foreach (TrackingInfo track in hq.trackingDatabase.Values)
            {
                if (track == null) continue;
                if (++examined > MaxTracksExamined) break;
                float age = nativeNow - track.lastSpottedTime;
                if (age < 0f || age > BanditFreshSeconds) continue;
                if (!track.TryGetUnit(out Unit unit) || !(unit is Aircraft) || unit.disabled) continue;
                if (unit.NetworkHQ == null || unit.NetworkHQ == hq) continue;
                float dx = (float)(track.lastKnownPosition.x - here.x), dy = (float)(track.lastKnownPosition.y - here.y), dz = (float)(track.lastKnownPosition.z - here.z);
                float distance = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distance < best) best = distance;
            }
            return best;
        }
    }
}
