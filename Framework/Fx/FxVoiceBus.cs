using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Framework.Fx
{
    /// <summary>
    /// The voice bus: caps concurrent procedural loops and one-shots so rain + thunder + creaks
    /// + ambience can never stack into a wall of noise (or voices). Loops register for their
    /// whole Play/Stop span; one-shots hold a slot for their clip length, expired by time.
    /// Refusals return false — the caller stays silent, never queues.
    /// </summary>
    internal static class FxVoiceBus
    {
        private const int MaxLoops = 4;
        private const int MaxOneShots = 6;
        private const int MaxShotSlots = 16;

        private static readonly HashSet<string> loops = new HashSet<string>();
        private static readonly List<float> shotExpiry = new List<float>(MaxShotSlots);

        public static bool TryStartLoop(string effectId)
        {
            if (string.IsNullOrEmpty(effectId)) return false;
            if (loops.Contains(effectId)) return true;
            if (loops.Count >= ScaledMax(MaxLoops)) return false;
            loops.Add(effectId);
            return true;
        }

        public static void EndLoop(string effectId)
        {
            if (!string.IsNullOrEmpty(effectId)) loops.Remove(effectId);
        }

        public static bool TryOneShot(float durationSec)
        {
            Prune();
            if (shotExpiry.Count >= MaxShotSlots) return false;
            if (ActiveShots() >= ScaledMax(MaxOneShots)) return false;
            shotExpiry.Add(Time.unscaledTime + Mathf.Max(0.1f, durationSec));
            return true;
        }

        public static void Describe(IDictionary<string, object> state)
        {
            if (state == null) return;
            Prune();
            state["fxLoops"] = loops.Count;
            state["fxShots"] = ActiveShots();
        }

        internal static void Clear()
        {
            loops.Clear();
            shotExpiry.Clear();
        }

        private static int ActiveShots()
        {
            float now = Time.unscaledTime;
            int active = 0;
            for (int i = 0; i < shotExpiry.Count; i++)
                if (shotExpiry[i] > now) active++;
            return active;
        }

        private static void Prune()
        {
            float now = Time.unscaledTime;
            for (int i = shotExpiry.Count - 1; i >= 0; i--)
                if (shotExpiry[i] <= now) shotExpiry.RemoveAt(i);
        }

        private static int ScaledMax(int full)
        {
            int scaled = Mathf.RoundToInt(full * FxBus.Scales.Voices);
            return Mathf.Max(1, scaled);
        }
    }
}
