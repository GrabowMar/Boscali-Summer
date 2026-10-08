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
        public const float MaxRange = 1100f;
        public const float MinRange = 120f; // Very close arrivals can beat the drop-and-turn delay.
        public const float Cooldown = 2.2f;
        public const float EngagementMemory = 8f;

        private readonly Dictionary<int, float> engaged = new Dictionary<int, float>();
        private float nextShot;

        /// <returns>Threat id to engage, or -1.</returns>
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
                if (time < .45f || time > 6f || time >= bestTime) continue;
                if (engaged.TryGetValue(t.Id, out float until) && now < until) continue;
                best = t.Id;
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
