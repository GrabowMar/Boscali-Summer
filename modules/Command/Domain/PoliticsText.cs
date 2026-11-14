using System;
using System.Globalization;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>One ORDERS IN FORCE line: whose (0 ours, 1 theirs, 2 next), what it does now, its clock or count, and whether it hurts us.</summary>
    internal struct OrderLine
    {
        public int Side;
        public bool Bad;
        public string Text, Value;
    }

    /// <summary>Pure: the politics view (war support, doctrine, decisions, the other cabinet) as the words FAC POLITICS and STR BOARD print.</summary>
    internal static class PoliticsText
    {
        public const int MaxOrders = 6;

        // What a decision in force does right now, from our side and as the other side's.
        private static readonly string[] Ours =
        {
            "", "PLANTS +50 %", "STAFF BOLD · FREE WAVE ROLLING", "+4 AI JETS IN THE AIR", "", "", "THEIR ASSAULTS NEED x1.5", "",
            "ONE ENEMY PLANT STOPPED", "", "THEIR AI SKILL -10 %",
        };

        private static readonly string[] Theirs =
        {
            "", "THEIR PLANTS +50 %", "THEY GO BOLD", "+4 ENEMY JETS", "", "", "OUR ASSAULTS NEED x1.5", "",
            "ONE OF OUR PLANTS STOPPED", "", "OUR AI SKILL -10 %",
        };

        public static string Clock(float seconds) => StrNationView.Clock(seconds);

        public static string Signed(float delta)
        {
            int d = (int)Math.Round(delta);
            return (d > 0 ? "+" : "") + d.ToString(CultureInfo.InvariantCulture);
        }

        public static string Ago(float seconds) =>
            seconds < 60f ? "JUST NOW" : ((int)(seconds / 60f)).ToString(CultureInfo.InvariantCulture) + " MIN AGO";

        public static int Round(float v) => (int)Math.Round(v);

        /// <summary>The DECISIONS button for one id and whether it can be pressed: IN FORCE, another doctrine, the rank it needs, its cooldown, its price, or DECIDE.</summary>
        public static string Button(NationView v, int id, out bool open)
        {
            open = false;
            if (v.InForce[id] > 0f) return "IN FORCE " + Clock(v.InForce[id]);
            Doctrine own = Decisions.DoctrineOf(id);
            if (own != Doctrine.None && own != v.Doctrine) return "LOCKED";
            if (Decisions.Rank(id) > v.Rank) return "RANK " + Decisions.Rank(id).ToString(CultureInfo.InvariantCulture);
            if (v.Cooldown[id] > 0f) return "AGAIN " + Clock(v.Cooldown[id]);
            if (v.WarSupport < Decisions.Cost(id)) return "NEED " + Decisions.Cost(id).ToString(CultureInfo.InvariantCulture);
            open = true;
            return "DECIDE";
        }

        /// <summary>A rank cell: the decision it opens, and when while it is still ahead.</summary>
        public static string RankLine(Doctrine doctrine, int rank, bool reached)
        {
            string line = "R" + rank.ToString(CultureInfo.InvariantCulture) + " · " + Decisions.Name(Decisions.Of(doctrine, rank));
            return reached || rank == 1 ? line : line + (rank == 2 ? " · 15 MIN" : " · ESCALATION");
        }

        /// <summary>What the doctrine, the decisions, the cabinet and the thresholds are doing right now, ours then theirs, then the next threshold. Returns the count.</summary>
        public static int Orders(NationView v, OrderLine[] into)
        {
            int n = 0;
            for (int id = 0; id < Decisions.Count && n < into.Length; id++)
                if (v.InForce[id] > 0f && Ours[id].Length > 0)
                    into[n++] = new OrderLine { Side = 0, Text = Decisions.Name(id) + " · " + Ours[id], Value = Clock(v.InForce[id]) };
            if (v.PostureFloor == v.PostureCeiling && v.InForce[(int)DecisionId.GeneralOffensive] <= 0f && n < into.Length)
                into[n++] = new OrderLine { Side = 0, Text = "THE CABINET HOLDS THE STAFF " + (v.PostureFloor == 0 ? "CAUTIOUS" : "BOLD"), Value = "" };
            if (v.WarSupport < 25f && n < into.Length)
                into[n++] = new OrderLine { Side = 0, Text = "WAR SUPPORT UNDER 25 · NO BOLD, -2 AI JETS", Bad = true, Value = Round(v.WarSupport).ToString(CultureInfo.InvariantCulture) };
            else if (v.WarSupport > 75f && n < into.Length)
                into[n++] = new OrderLine { Side = 0, Text = "WAR SUPPORT OVER 75 · +2 AI JETS", Value = Round(v.WarSupport).ToString(CultureInfo.InvariantCulture) };
            if (v.EnemyDecided >= 0 && v.EnemyDecidedLeft > 0f && Theirs[v.EnemyDecided].Length > 0 && n < into.Length)
                into[n++] = new OrderLine { Side = 1, Bad = true, Text = Decisions.Name(v.EnemyDecided) + " · " + Theirs[v.EnemyDecided], Value = Clock(v.EnemyDecidedLeft) };
            if (!string.IsNullOrEmpty(v.EnemyIntel) && n < into.Length)
                into[n++] = new OrderLine { Side = 1, Text = "THEIR MAIN EFFORT · " + v.EnemyIntel, Value = "DEFECTOR" };
            if (n < into.Length)
            {
                int ws = Round(v.WarSupport);
                into[n++] = v.WarSupport < 25f
                    ? new OrderLine { Side = 2, Text = "BOLD AND TWO JETS BACK · WAR SUPPORT 25", Value = ws + "/25" }
                    : v.WarSupport <= 75f
                    ? new OrderLine { Side = 2, Text = "+2 JETS IN THE AIR · WAR SUPPORT OVER 75", Value = ws + "/75" }
                    : new OrderLine { Side = 2, Text = "THE STAFF SPENDS ABOVE 60", Value = ws + "/60" };
            }
            return n;
        }

        /// <summary>The cabinet minutes: the doctrine, the mood, the last decision, then the other cabinet in one line.</summary>
        public static string Minutes(NationView v)
        {
            string doctrine = v.Doctrine == Doctrine.None
                ? "No doctrine yet; the war cabinet sits at five minutes."
                : Cap(Doctrines.Name(v.Doctrine)) + " at rank " + v.Rank.ToString(CultureInfo.InvariantCulture) + ", chosen by " + Cap(v.DoctrineBy) + ".";
            string mood = v.WarSupport < 25f ? "The benches are restless; the staff may not go bold."
                : v.WarSupport > 75f ? "The country is behind the war; two more jets fly."
                : v.WarSupport > 60f ? "The flags are out; the staff spends what we earn above 60."
                : "The country holds; every sortie counts.";
            string last = v.Decided >= 0 ? " Last decision: " + Cap(Decisions.Name(v.Decided)) + ", " + Cap(v.DecidedBy) + ", " + Ago(v.DecidedAge).ToLowerInvariant() + "." : "";
            string enemy = string.IsNullOrEmpty(v.EnemyName) ? "" : "\nTHE OTHER CABINET: " + v.EnemyName + " · " +
                (v.EnemyDoctrine == Doctrine.None ? "NO DOCTRINE" : Doctrines.Name(v.EnemyDoctrine) + " RANK " + v.EnemyRank.ToString(CultureInfo.InvariantCulture)) +
                " · WAR SUPPORT " + Round(v.EnemyWarSupport).ToString(CultureInfo.InvariantCulture);
            return "CABINET MINUTES. " + doctrine + " " + mood + last + enemy;
        }

        /// <summary>"TOTAL WAR" as "Total war" for the minutes' prose.</summary>
        private static string Cap(string upper)
        {
            if (string.IsNullOrEmpty(upper)) return "";
            string lower = upper.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }
    }
}
