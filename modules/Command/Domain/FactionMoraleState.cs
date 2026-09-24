using System.Collections.Generic;

namespace BoscaliSummer.Features.Command.Domain
{
    internal sealed class FactionMoraleState
    {
        internal const float InitialMorale = 50f;
        internal const int MaximumFactions = 8;
        private readonly Dictionary<int, float> values = new Dictionary<int, float>();

        // Contract rewards lock when offered, so later mood changes cannot alter
        // an accepted payout. Full morale grants 10%; zero morale costs 20%.
        internal static float ContractMultiplier(float morale) =>
            morale >= 50f ? 1f + (morale - 50f) * 0.002f : 0.8f + morale * 0.004f;

        internal bool TryGet(int faction, out float morale)
        {
            if (values.TryGetValue(faction, out morale)) return true;
            if (values.Count >= MaximumFactions) return false;
            values.Add(faction, morale = InitialMorale);
            return true;
        }

        internal bool TrySet(int faction, float morale)
        {
            if (float.IsNaN(morale) || float.IsInfinity(morale) || morale < 0f || morale > 100f)
                return false;
            if (!TryGet(faction, out _)) return false;
            values[faction] = morale;
            return true;
        }

        internal void Reset() => values.Clear();
    }
}
