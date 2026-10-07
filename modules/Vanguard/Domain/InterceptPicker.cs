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
        public const float MaxRange = 3000f;
        public const float MinRange = 150f;
        public const float Cooldown = 1.5f;
        public const float EngagementMemory = 6f;

        private readonly Dictionary<int, float> engaged = new Dictionary<int, float>();
        private float nextShot;

        /// <returns>Threat id to engage, or -1.</returns>
        public int Pick(float now, IReadOnlyList<ThreatView> threats)
        {
            if (now < nextShot) return -1;
            int best = -1;
            float bestRange = float.MaxValue;
            for (int i = 0; i < threats.Count; i++)
            {
                ThreatView t = threats[i];
                if (t.Closing <= 0f || t.Range > MaxRange || t.Range < MinRange || t.Range >= bestRange) continue;
                if (engaged.TryGetValue(t.Id, out float until) && now < until) continue;
                best = t.Id;
                bestRange = t.Range;
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
