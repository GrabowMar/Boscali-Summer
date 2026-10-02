using System.Globalization;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>The dossier's rank and XP bar (spec WMC rebuild §WING): five equal fifths, one per rank, each filled by the progress through
    /// that rank; the rank line names the next rank and its threshold.</summary>
    internal static class PilotXp
    {
        public static float Fill(int xp)
        {
            if (xp <= 0) return 0f;
            WingRank rank = PilotPerks.RankFor(xp);
            if (rank == WingRank.Legend) return 1f;
            int lo = PilotPerks.XpForRank(rank), hi = PilotPerks.XpForRank(rank + 1);
            float within = hi > lo ? (float)(xp - lo) / (hi - lo) : 0f;
            return ((int)rank + within) / 5f;
        }

        public static float Tick(int rank) => rank / 5f;

        public static string RankLine(int xp)
        {
            WingRank rank = PilotPerks.RankFor(xp);
            string head = PilotPerks.RankName(rank) + " · XP " + (xp < 0 ? 0 : xp).ToString("N0", CultureInfo.InvariantCulture);
            if (rank == WingRank.Legend) return head + " · TOP RANK";
            return head + " / " + PilotPerks.XpForRank(rank + 1).ToString("N0", CultureInfo.InvariantCulture) + " · NEXT " + PilotPerks.RankName(rank + 1);
        }
    }
}
