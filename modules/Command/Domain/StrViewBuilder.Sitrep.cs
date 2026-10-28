using System;
using System.Globalization;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Domain
{
    internal static partial class StrViewBuilder
    {
        private partial struct Worker
        {
            // Sector classes of the census, or false when any is unknown: the tile and the SITREP bar share this one denominator (panel-6).
            private static bool SectorShares(in TheaterCensus c, out float own, out float contested, out float hostile, out float unclaimed, out int total)
            {
                own = contested = hostile = unclaimed = float.NaN; total = 0;
                if (!c.Known || c.SectorsFriendly < 0 || c.SectorsHostile < 0 || c.SectorsContested < 0 || c.SectorsNeutral < 0) return false;
                total = c.SectorsFriendly + c.SectorsHostile + c.SectorsContested + c.SectorsNeutral;
                if (total <= 0) return false;
                own = c.SectorsFriendly / (float)total; contested = c.SectorsContested / (float)total;
                hostile = c.SectorsHostile / (float)total; unclaimed = c.SectorsNeutral / (float)total;
                return true;
            }

            // ---- rule 10: the four header tiles ------------------------------------------------------------------------------

            private void Tiles()
            {
                TheaterCensus c = f.Census;
                bool sectors = SectorShares(c, out float own, out _, out _, out _, out int total);
                // "More than two points" is judged on the counts: the float shares of an exact two-point lead round either way.
                long gap = sectors ? (long)(c.SectorsFriendly - c.SectorsHostile) * 50L : 0L;
                bool ahead = sectors && gap > total, behind = sectors && -gap > total;
                PutTile(0, "SECTORS",
                    sectors ? TheaterReadout.Percent(own) : StrWords.Dash,
                    !sectors ? CensusCaption(c) : ahead ? "LEAD" : behind ? "LAG" : "EVEN",
                    sectors ? own : float.NaN,
                    !sectors ? AvState.Inert : ahead ? AvState.Ready : behind ? AvState.Caution : AvState.Info,
                    "Share of the map's sectors your side holds, out of every sector the staff classifies: held, contested, hostile and unclaimed.");

                bool air = c.Known && c.AirFriendly >= 0 && c.AirHostileTracked >= 0;
                PutTile(1, "AIR",
                    air ? c.AirFriendly + " : " + c.AirHostileTracked : StrWords.Dash, air ? "TRK" : CensusCaption(c),
                    air ? TheaterReadout.AlliedShare(c.AirFriendly, c.AirHostileTracked) : float.NaN,
                    !air ? AvState.Inert : c.AirHostileTracked > c.AirFriendly ? AvState.Caution : AvState.Info,
                    "Your airborne aircraft : hostile aircraft your faction tracks. Unseen hostiles are not counted.");

                bool ground = c.Known && c.GroundFriendly >= 0 && c.GroundHostileTracked >= 0;
                PutTile(2, "GROUND",
                    ground ? c.GroundFriendly + " : " + c.GroundHostileTracked : StrWords.Dash, ground ? "TRK" : CensusCaption(c),
                    ground ? TheaterReadout.AlliedShare(c.GroundFriendly, c.GroundHostileTracked) : float.NaN,
                    !ground ? AvState.Inert : c.GroundHostileTracked > c.GroundFriendly ? AvState.Caution : AvState.Info,
                    "Your ground vehicles : hostile ground vehicles your faction tracks. Ships are not counted.");

                int pool = m.Wave.Present ? m.Wave.Pool : -1;
                PutTile(3, "POOL",
                    pool >= 0 ? N(pool) : StrWords.Dash, string.IsNullOrEmpty(f.FactionCode) ? "FUNDS" : f.FactionCode, float.NaN,
                    pool < 0 ? AvState.Inert : m.Wave.Held ? AvState.Caution : AvState.Info,
                    "Funds in your faction's pool, which pay for reinforcement waves.");
            }

            private static string CensusCaption(in TheaterCensus c) => c.Known ? StrWords.Dash : "NOT MEASURED";

            private void PutTile(int i, string key, string value, string caption, float bar, AvState state, string help)
            {
                StrTile t = m.Tiles[i];
                if (t.Key == key && t.Value == value && t.Caption == caption && t.Help == help && t.State == state && t.Bar.Equals(bar)) return;
                m.Tiles[i] = new StrTile { Key = key, Value = value, Caption = caption, Bar = bar, State = state, Help = help };
                Changed = true;
            }

            // ---- rule 11: SITREP ---------------------------------------------------------------------------------------------

            private void Sitrep()
            {
                TheaterCensus c = f.Census;

                if (SectorShares(c, out float own, out float contested, out float hostile, out float unclaimed, out int total))
                    PutBalance(ref m.Sectors, "SECTORS",
                        TheaterReadout.Percent(own) + " OWN" + StrWords.Dot + TheaterReadout.Percent(contested) + " CONTESTED" + StrWords.Dot + TheaterReadout.Percent(hostile) + " HOSTILE" +
                        (c.SectorsNeutral > 0 ? StrWords.Dot + TheaterReadout.Percent(unclaimed) + " UNCLAIMED" : ""),
                        total + " SECTORS", own, contested, hostile);
                else PutBalance(ref m.Sectors, "SECTORS", StrWords.Dash, CensusCaption(c), float.NaN, float.NaN, float.NaN);

                PairBalance(ref m.Air, "AIR", c.Known ? c.AirFriendly : -1, c.Known ? c.AirHostileTracked : -1, c, "AIRBORNE : TRACKED HOSTILE");
                PairBalance(ref m.Ground, "GROUND", c.Known ? c.GroundFriendly : -1, c.Known ? c.GroundHostileTracked : -1, c, "VEHICLES : TRACKED HOSTILE");

                if (c.Known && c.BasesFriendly >= 0 && c.BasesNeutral >= 0 && c.BasesHostile >= 0 && c.BasesFriendly + c.BasesNeutral + c.BasesHostile > 0)
                {
                    float all = c.BasesFriendly + c.BasesNeutral + c.BasesHostile;
                    PutBalance(ref m.Airbases, "AIRBASES",
                        c.BasesFriendly + " OWN" + StrWords.Dot + c.BasesNeutral + " NEUTRAL" + StrWords.Dot + c.BasesHostile + " HOSTILE", "AIRBASES",
                        c.BasesFriendly / all, c.BasesNeutral / all, c.BasesHostile / all);
                }
                else PutBalance(ref m.Airbases, "AIRBASES", StrWords.Dash, CensusCaption(c), float.NaN, float.NaN, float.NaN);

                Put(ref m.RadarsText, c.Known && c.Radars >= 0 ? c.Radars.ToString(CultureInfo.InvariantCulture) : StrWords.Dash);
                Put(ref m.KnownAdText, f.KnownAirDefence >= 0 ? f.KnownAirDefence + (f.KnownAirDefence == 1 ? " SITE" : " SITES") : "NOT AVAILABLE");
                Put(ref m.ContestedBasesText, c.Known && c.BasesContested >= 0 ? c.BasesContested.ToString(CultureInfo.InvariantCulture) : StrWords.Dash);
                Put(ref m.FrontText, FrontLine(c));

                int level = c.Known && c.Threat >= 1 && c.Threat <= 5 ? c.Threat : 0;
                Put(ref m.AlertLevel, level);
                Put(ref m.AlertWord, StrWords.Alert(level));
                Put(ref m.AlertRule, level > 0 ? TheaterWords.ThreatTitle(c.ThreatWhy) : "");

                SortieBoard(c);
            }

            private string FrontLine(in TheaterCensus c)
            {
                string metres = c.Known && c.FrontMetres >= 0f && !float.IsInfinity(c.FrontMetres) ? TheaterReadout.Kilometres(c.FrontMetres).ToUpperInvariant() : StrWords.Dash;
                if (snap?.Fronts == null) return metres;
                int n = Math.Min(snap.Fronts.Length, StrViewModel.MaxFronts), unseen = 0;
                for (int i = 0; i < n; i++) if (snap.Fronts[i] != null && !snap.Fronts[i].Observed) unseen++;
                return metres + StrWords.Dot + n + (n == 1 ? " PLATFORM" : " PLATFORMS") + (unseen > 0 ? StrWords.Dot + unseen + " NOT OBSERVED" : "");
            }

            // A two-sided count: own against tracked hostile. The parts are the allied share and its remainder; a known 0 : 0 has no share.
            private void PairBalance(ref StrBalance dst, string key, int own, int hostile, in TheaterCensus c, string caption)
            {
                if (own < 0 || hostile < 0) { PutBalance(ref dst, key, StrWords.Dash, CensusCaption(c), float.NaN, float.NaN, float.NaN); return; }
                float share = TheaterReadout.AlliedShare(own, hostile);
                PutBalance(ref dst, key, own + " : " + hostile, caption, share, float.NaN, float.IsNaN(share) ? float.NaN : 1f - share);
            }

            private void PutBalance(ref StrBalance dst, string key, string text, string caption, float own, float middle, float hostile)
            {
                if (dst.Key == key && dst.Text == text && dst.Caption == caption && dst.Own.Equals(own) && dst.Middle.Equals(middle) && dst.Hostile.Equals(hostile)) return;
                dst = new StrBalance { Key = key, Text = text, Caption = caption, Own = own, Middle = middle, Hostile = hostile };
                Changed = true;
            }

            // The AI sortie board: friendly AI sorties by kind and their share of the total. A share needs every count; one unknown makes all unknown.
            private void SortieBoard(in TheaterCensus c)
            {
                int cap = c.Known ? c.SortieCap : -1, sead = c.Known ? c.SortieSead : -1, cas = c.Known ? c.SortieCas : -1,
                    strike = c.Known ? c.SortieStrike : -1, transit = c.Known ? c.SortieTransit : -1;
                bool all = cap >= 0 && sead >= 0 && cas >= 0 && strike >= 0 && transit >= 0;
                int sum = all ? cap + sead + cas + strike + transit : 0;
                SortieRow(0, cap, all, sum); SortieRow(1, sead, all, sum); SortieRow(2, cas, all, sum);
                SortieRow(3, strike, all, sum); SortieRow(4, transit, all, sum);
                Put(ref m.SortieBoardCaption, "FRIENDLY AI ONLY" + StrWords.Dot + "NOT YOU OR YOUR WING");
            }

            private void SortieRow(int i, int count, bool all, int sum)
            {
                Put(ref m.SortieCounts[i], count < 0 ? -1 : count);
                Put(ref m.SortieShares[i], all && sum > 0 ? count / (float)sum : float.NaN);
            }        }
    }
}
