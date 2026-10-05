using System;

namespace BoscaliSummer.Core.Game
{
    /// <summary>Cosmetic faction identity from native tags/names, independent of registry order.</summary>
    internal static class PortraitFactions
    {
        public static int Of(FactionHQ hq) => hq != null ? Of(hq.faction) : -1;

        public static int Of(Faction faction)
        {
            if (faction == null) return -1;
            string tag = (faction.factionTag ?? string.Empty).Trim();
            if (tag.Equals("BDF", StringComparison.OrdinalIgnoreCase)) return 0;
            if (tag.Equals("PALA", StringComparison.OrdinalIgnoreCase)) return 1;
            string name = (faction.factionName ?? string.Empty) + " " + (faction.factionExtendedName ?? string.Empty);
            if (name.IndexOf("Boscali", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (name.IndexOf("Primeva", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("PALA", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            return -1;
        }

        public static int Local => GameAccess.TryGetLocalFaction(out FactionHQ hq) ? Of(hq) : -1;

        /// <summary>Existing enemy snapshots omit HQ identity. Resolve only an unambiguous opposing faction.</summary>
        public static int OpposingLocal
        {
            get
            {
                if (!GameAccess.TryGetLocalFaction(out FactionHQ own)) return -1;
                int ownFaction = Of(own);
                if (ownFaction < 0) return -1;
                int result = -1, inspected = 0;
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (++inspected > 16) return -1;
                    if (hq == null || hq == own || hq.faction == null ||
                        FactionHelper.EmptyOrNoFactionOrNeutral(hq.faction.factionName)) continue;
                    int faction = Of(hq);
                    if (faction < 0 || faction == ownFaction || (result >= 0 && result != faction)) return -1;
                    result = faction;
                }
                return result;
            }
        }
    }
}
