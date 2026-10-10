using System.Collections.Generic;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal readonly struct ThreatView
    {
        public readonly int Id;
        public readonly float Range;
        public readonly float Closing;

        public ThreatView(int id, float range, float closing)
        {
            Id = id;
            Range = range;
            Closing = closing;
        }
    }

    /// <summary>AEGIS fire control: nearest closing threat in range, one interceptor per threat, global cooldown.</summary>
    internal sealed class InterceptPicker
    {
        // Sensor reach. A dart is armed ~0.9 s after the pick (sensor confirm, cell cover, drop and slew), and an AAM
        // closes at 1 km/s and more: a fixed 1.25 km ring let it reach the carrier before the dart armed.
        public const float MaxRange = 3000f;
        public const float EngageSeconds = 2.5f; // launch this long before impact, whatever the range
        public const float MinRange = 120f; // Very close arrivals can beat the drop-and-turn delay.
        public const float Cooldown = 2.2f;
        public const float EngagementMemory = 8f;

        private readonly Dictionary<int, float> engaged = new Dictionary<int, float>();
        private float nextShot;

        /// <returns>Index into <paramref name="threats"/> of the one to engage, or -1. Never the id: Unity instance ids of
        /// objects spawned at runtime are negative, so an id cannot double as "none".</returns>
        public int Pick(float now, IReadOnlyList<ThreatView> threats)
        {
            if (now < nextShot) return -1;
            int best = -1;
            float bestTime = float.MaxValue;
            for (int i = 0; i < threats.Count; i++)
            {
                ThreatView t = threats[i];
                if (t.Closing < 40f || t.Range > MaxRange || t.Range < MinRange) continue;
                float time=t.Range/t.Closing;
                if (time < .45f || time > EngageSeconds || time >= bestTime) continue;
                if (engaged.TryGetValue(t.Id, out float until) && now < until) continue;
                best = i;
                bestTime = time;
            }
            return best;
        }

        public void Fired(float now, int threatId)
        {
            nextShot = now + Cooldown;
            engaged[threatId] = now + EngagementMemory;
        }
    }
}
