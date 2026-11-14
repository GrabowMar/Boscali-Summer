using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using NOAvionics;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The country strip: the same four tiles on every war, country and news console (STR, MIS, OPS, FAC, EVN), so the header
    /// reads the same wherever a pilot opens it. Each tile reads what is already published and prints a dash when its source is
    /// off. FUNDS is the one place the faction's money shows outside FAC ECONOMY; rows elsewhere link to FAC instead.
    /// </summary>
    internal static class CountryStrip
    {
        public static AvMetric[] Mount(AvConsole console)
        {
            AvMetric[] t = console.Metrics("FUNDS", "PLANTS", "SECTORS", "AIRBASES");
            AvHelpTip.Attach(t[0].Rect.gameObject, "Your faction's funds. Waves, convoys, factories and support are paid from them; FAC ECONOMY shows where they go.");
            AvHelpTip.Attach(t[1].Rect.gameObject, "Factories your faction runs, out of the slots it has. Built and spent on FAC ECONOMY.");
            AvHelpTip.Attach(t[2].Rect.gameObject, "Sectors your side holds, out of every sector the staff classifies. The fronts are on STR BOARD.");
            AvHelpTip.Attach(t[3].Rect.gameObject, "Airbases your faction holds. The register is on FAC POLITICS.");
            console.Ticker.Add(-1, AvTickRate.Slow, () => Refresh(t));
            Refresh(t);
            return t;
        }

        private static void Refresh(AvMetric[] t)
        {
            bool hq = GameAccess.TryGetLocalFaction(out FactionHQ own);
            if (!hq) t[0].Set("—", "NO HQ", 0f, AvState.Inert);
            else t[0].Set(AvNum.Money(own.factionFunds), own.factionFunds < 0f ? "OWED" : "", 0f, own.factionFunds < 0f ? AvState.Caution : AvState.Ready);

            NationView n = ModuleServices.TryGet(out INationView nation) && nation.Active ? nation.Local : null;
            int plants = 0;
            if (n != null) foreach (PlantView p in n.Plants) if (p.Kind != LineKind.Construction) plants++;
            if (n == null) t[1].Set("—", "OFF", 0f, AvState.Inert);
            else t[1].Set(plants + "/" + n.Slots, n.Building ? "BUILDING" : "", n.Slots > 0 ? plants / (float)n.Slots : 0f, AvState.Ready);

            TheaterCensus c = ModuleServices.TryGet(out ITheaterWarView war) && war.Snapshot != null ? war.Snapshot.Census : TheaterCensus.Unmeasured;
            int total = c.Known && c.SectorsFriendly >= 0 && c.SectorsHostile >= 0 && c.SectorsContested >= 0 && c.SectorsNeutral >= 0
                ? c.SectorsFriendly + c.SectorsHostile + c.SectorsContested + c.SectorsNeutral : 0;
            if (total <= 0) t[2].Set("—", "NOT MEASURED", 0f, AvState.Inert);
            // A share, as STR always showed it: the census counts control-field cells, thousands of them.
            else t[2].Set(AvNum.Percent(c.SectorsFriendly / (float)total), "HELD", c.SectorsFriendly / (float)total, AvState.Ready);

            if (!hq) { t[3].Set("—", "", 0f, AvState.Inert); return; }
            int bases = 0;
            foreach (Airbase _ in own.GetAirbases()) bases++;
            t[3].Set(bases.ToString(System.Globalization.CultureInfo.InvariantCulture), "", 0f, bases > 0 ? AvState.Ready : AvState.Caution);
        }
    }
}
