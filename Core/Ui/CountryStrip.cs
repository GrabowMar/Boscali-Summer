using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using NOAvionics;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The country strip: the same four tiles on every war, country and news console (STR, MIS, OPS, FAC, EVN), so the header reads the
    /// same wherever a pilot opens it: WAR SUPPORT · DOCTRINE · FUNDS · PLANTS. Each tile reads what is already published and prints a dash
    /// when its source is off. FUNDS is the one place the faction's money shows outside FAC ECONOMY; rows elsewhere link to FAC instead.
    /// </summary>
    internal static class CountryStrip
    {
        public static AvMetric[] Mount(AvConsole console)
        {
            AvMetric[] t = console.Metrics("WAR SUPPORT", "DOCTRINE", "FUNDS", "PLANTS");
            AvHelpTip.Attach(t[0].Rect.gameObject, "War support, 0-100: earned by fighting (sorties, captures; losses drain it) and spent on decisions. Under 25 no BOLD and two fewer AI jets, over 75 two more. Why it moved is on FAC POLITICS.");
            AvHelpTip.Attach(t[1].Rect.gameObject, "The doctrine the war cabinet chose and its rank (the rank rises by itself). Chosen once on EVN DISPATCH; its decisions are on FAC POLITICS.");
            AvHelpTip.Attach(t[2].Rect.gameObject, "Your faction's funds. Waves, convoys, factories and support are paid from them; FAC ECONOMY shows where they go.");
            AvHelpTip.Attach(t[3].Rect.gameObject, "Factories your faction runs, out of the slots it has. Built and spent on FAC ECONOMY.");
            console.Ticker.Add(-1, AvTickRate.Slow, () => Refresh(t));
            Refresh(t);
            return t;
        }

        private static void Refresh(AvMetric[] t)
        {
            NationView n = ModuleServices.TryGet(out INationView nation) && nation.Active ? nation.Local : null;
            if (n == null)
            {
                t[0].Set("—", "OFF", 0f, AvState.Inert);
                t[1].Set("—", "OFF", 0f, AvState.Inert);
            }
            else
            {
                float ws = n.WarSupport;
                string last = n.Moves.Count > 0 ? (n.Moves[0].Delta > 0f ? "+" : "") + AvNum.Fixed(n.Moves[0].Delta, 0) : "";
                t[0].Set(AvNum.Fixed(ws, 0), last, ws / 100f, ws < 25f ? AvState.Danger : ws > 75f ? AvState.Ready : AvState.Caution);
                t[1].Set(n.Doctrine == Doctrine.None ? "NONE YET" : Doctrines.Name(n.Doctrine), n.Doctrine == Doctrine.None ? "" : "RANK " + n.Rank,
                    n.Rank / 3f, n.Doctrine == Doctrine.None ? AvState.Inert : AvState.Info);
            }

            bool hq = GameAccess.TryGetLocalFaction(out FactionHQ own);
            if (!hq) t[2].Set("—", "NO HQ", 0f, AvState.Inert);
            else t[2].Set(AvNum.Money(own.factionFunds), own.factionFunds < 0f ? "OWED" : "", 0f, own.factionFunds < 0f ? AvState.Caution : AvState.Ready);

            int plants = 0;
            if (n != null) foreach (PlantView p in n.Plants) if (p.Kind != LineKind.Construction) plants++;
            if (n == null) t[3].Set("—", "OFF", 0f, AvState.Inert);
            else t[3].Set(plants + "/" + n.Slots, n.Building ? "BUILDING" : "", n.Slots > 0 ? plants / (float)n.Slots : 0f, AvState.Ready);
        }
    }
}
