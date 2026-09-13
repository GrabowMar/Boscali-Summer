using System.Collections.Generic;
using BoscaliSummer.Features.Command.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Observation buffer shared by the faction panels, sampled from mission start rather
    /// than only while a panel is on screen. Opening the panel therefore shows the history
    /// that already accumulated instead of starting from the moment it became visible.
    ///
    /// <para>Still local observations only: no backfill, no simulated production, and a
    /// bounded set of factions. The sampling throttle and rolling ceiling live in
    /// <see cref="MfdResourceHistory"/>.</para>
    /// </summary>
    internal static class FactionResourceHistoryStore
    {
        private const int MaximumFactions = 8;
        private static readonly Dictionary<int, MfdResourceHistory> histories =
            new Dictionary<int, MfdResourceHistory>();

        internal static MfdResourceHistory For(FactionHQ hq)
        {
            if (hq == null) return null;
            int id = hq.GetInstanceID();
            if (histories.TryGetValue(id, out MfdResourceHistory history)) return history;
            if (histories.Count >= MaximumFactions) return null;
            history = new MfdResourceHistory();
            histories.Add(id, history);
            return history;
        }

        internal static void Sample()
        {
            Dictionary<Faction, FactionHQ>.ValueCollection hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            float now = Time.time;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null) continue;
                MfdResourceHistory history = For(hq);
                if (history == null || !history.Due(now)) continue;
                history.Sample(now, hq.factionFunds, hq.GetWarheadStockpile(),
                    Manpower(hq), Morale(hq));
            }
        }

        internal static float Manpower(FactionHQ hq)
        {
            if (hq == null || hq.missionStatsTracker == null) return float.NaN;
            MissionStatsTracker.TypeStat stats = hq.missionStatsTracker.manpower;
            return stats.buildings.current + stats.vehicles.current +
                   stats.ships.current + stats.aircraft.current;
        }

        internal static float Morale(FactionHQ hq) =>
            FactionResources.TryGetMorale(hq, out float stored) ? stored : float.NaN;

        internal static void Clear() => histories.Clear();
    }
}
