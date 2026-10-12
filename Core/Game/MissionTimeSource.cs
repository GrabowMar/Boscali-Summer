using System;
using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// The one place the game's mission clock is read for gameplay and weather deadlines. Pauses and acceleration
    /// follow the mission; multiplayer uses its shared start. A multiplayer peer (host included) whose
    /// MissionManager is missing or has not started (multiplayerStartTime unset, so the raw value is lobby time)
    /// keeps the last good value; the result is finite, never decreases within a scene, and re-baselines if a
    /// transient ever latched high.
    /// </summary>
    internal static class MissionTimeSource
    {
        private static readonly MissionClock Clock = new MissionClock();
        private static float nextWarning;
        // One read per frame and mode: the clock is constant within a frame.
        private static int heldFrame = -1, requiredFrame = -1;
        private static float heldTime, requiredTime;
        private static bool requiredOk;

        /// <summary>Support's clock: always returns a time, falling back to the last good one.</summary>
        public static float Now()
        {
            int frame = Time.frameCount;
            if (heldFrame != frame) { heldTime = Read(false, out _); heldFrame = frame; }
            return heldTime;
        }

        /// <summary>Weather's clock: false until a mission manager exists, the raw read passes the gate and the
        /// guarded clock has caught up with it. <paramref name="time"/> is still the held value when false.</summary>
        public static bool TryNow(out float time)
        {
            int frame = Time.frameCount;
            if (requiredFrame != frame) { requiredTime = Read(true, out requiredOk); requiredFrame = frame; }
            time = requiredTime;
            return requiredOk;
        }

        /// <summary>A new mission legitimately restarts at zero: call when the scene ends.</summary>
        public static void Reset()
        {
            Clock.Reset();
            heldFrame = requiredFrame = -1;
        }

        private static float Read(bool requireManager, out bool ok)
        {
            float raw = 0f;
            bool valid = false;
            try
            {
                MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
                bool multiplayer = GameManager.gameState == GameState.Multiplayer;
                bool source = mission != null || (!requireManager && !multiplayer);
                if (mission != null) raw = mission.MissionTime;
                else if (source) raw = Time.timeSinceLevelLoad;
                valid = MissionClockGate.IsValid(source, multiplayer,
                    mission != null ? mission.multiplayerStartTime : 0d, raw);
            }
            catch (Exception e)
            {
                valid = false;
                if (Time.unscaledTime >= nextWarning)
                {
                    nextWarning = Time.unscaledTime + 30f;
                    Plugin.Logger?.LogWarning("[Mission clock] read failed; holding the last good time: " + e.Message);
                }
            }
            float held = Clock.Read(raw, valid, Time.frameCount);
            ok = valid && MissionClockGate.Converged(held, raw);
            return held;
        }
    }
}
