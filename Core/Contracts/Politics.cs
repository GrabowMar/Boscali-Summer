using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    // POLITICS v2, engine-free (linked by tests/Nation.Tests): the decisions a side can take, the doctrines and the six cabinet cards, in
    // the words every screen shares. The host's rules over them live in modules/Nation/Domain/PoliticsBook.

    /// <summary>Two shared decisions, then three per doctrine in rank order.</summary>
    internal enum DecisionId : byte
    {
        WarBonds, SurgeProduction,
        GeneralOffensive, AirSurge, StrategicStrike,
        HoldTheLine, Shield, Counterattack,
        Sabotage, Propaganda, Defector,
    }

    internal static class Decisions
    {
        public const int Count = 11;
        /// <summary>A side sees five: its doctrine's three and the two shared.</summary>
        public const int PerSide = 5;
        /// <summary>A decision can be taken again this long after it was.</summary>
        public const float CooldownSeconds = 600f;

        private static readonly string[] Names =
        {
            "WAR BONDS", "SURGE PRODUCTION",
            "GENERAL OFFENSIVE", "AIR SURGE", "STRATEGIC STRIKE",
            "HOLD THE LINE", "SHIELD", "COUNTERATTACK",
            "SABOTAGE", "PROPAGANDA", "DEFECTOR",
        };

        private static readonly string[] Effects =
        {
            "+3 000 FUNDS", "PLANTS +50 % FOR 10 MIN",
            "STAFF BOLD 10 MIN · FREE WAVE · TAKEN: +15 BACK", "+4 AI JETS FOR 10 MIN", "+1 NUCLEAR WARHEAD",
            "STRONGPOINTS AT THE MOST THREATENED BASE", "THEIR ASSAULTS NEED x1.5 FOR 10 MIN", "STAFF OFFENSIVE AND A FREE WAVE ON THE LAST BASE LOST",
            "ONE ENEMY PLANT STOPS 5 MIN · DESTROY IT: +5", "ENEMY WAR SUPPORT -20", "THEIR MAIN EFFORT SHOWN · THEIR AI SKILL -10 % 10 MIN",
        };

        private static readonly byte[] Costs = { 15, 20, 30, 25, 60, 20, 25, 30, 20, 25, 40 };

        /// <summary>Seconds a decision stays in force (0: it acts at once).</summary>
        private static readonly short[] Durations = { 0, 600, 600, 600, 0, 0, 600, 0, 300, 0, 600 };

        public static bool Valid(int id) => id >= 0 && id < Count;
        public static string Name(int id) => Valid(id) ? Names[id] : "";
        public static string Effect(int id) => Valid(id) ? Effects[id] : "";
        public static int Cost(int id) => Valid(id) ? Costs[id] : 0;
        public static float Lasts(int id) => Valid(id) ? Durations[id] : 0f;

        /// <summary>None for the two shared.</summary>
        public static Doctrine DoctrineOf(int id) => !Valid(id) || id < 2 ? Doctrine.None : (Doctrine)(1 + (id - 2) / 3);

        /// <summary>The doctrine rank that opens it (0 for the shared two).</summary>
        public static int Rank(int id) => !Valid(id) || id < 2 ? 0 : 1 + (id - 2) % 3;

        public static int Of(Doctrine doctrine, int rank) => doctrine == Doctrine.None || rank < 1 || rank > 3 ? -1 : 2 + ((int)doctrine - 1) * 3 + rank - 1;

        /// <summary>The ids a side sees in screen order: its doctrine's three by rank, then the shared two. Returns the count.</summary>
        public static int Shown(Doctrine doctrine, int[] into)
        {
            int n = 0;
            for (int rank = 1; rank <= 3 && doctrine != Doctrine.None; rank++) into[n++] = Of(doctrine, rank);
            into[n++] = (int)DecisionId.WarBonds;
            into[n++] = (int)DecisionId.SurgeProduction;
            return n;
        }
    }

    internal static class Doctrines
    {
        private static readonly string[] Names = { "NO DOCTRINE", "TOTAL WAR", "FORTRESS", "SHADOW WAR" };
        private static readonly string[] Art = { "", "doctrine_total_war", "doctrine_fortress", "doctrine_shadow_war" };

        public static string Name(Doctrine d) => (int)d < Names.Length ? Names[(int)d] : "";

        /// <summary>The emblem key for <c>NationArt</c>; empty before a doctrine.</summary>
        public static string ArtKey(Doctrine d) => (int)d < Art.Length ? Art[(int)d] : "";
    }

    internal enum CardId : byte { WarCabinet, FallOf, VictoryAt, HeavyLosses, WarProfiteers, PeaceFeeler }

    /// <summary>The six cabinet cards: words, options, what each option does (as chips) and the option the staff marks.</summary>
    internal static class CabinetCards
    {
        public const int Count = 6;
        /// <summary>The staff answers a card nobody answered after this long.</summary>
        public const float AnswerSeconds = 90f;
        /// <summary>At most one card in this long per side (WAR CABINET excepted), and the same card not again within RepeatSeconds.</summary>
        public const float GapSeconds = 480f, RepeatSeconds = 900f;
        /// <summary>WAR CABINET sits this far into the mission.</summary>
        public const float WarCabinetAt = 300f;
        /// <summary>An answered card stays on the dispatch this long, with the answer and who gave it.</summary>
        public const float AnsweredSeconds = 30f;

        private static readonly string[] Titles = { "WAR CABINET", "FALL OF {0}", "VICTORY AT {0}", "HEAVY LOSSES", "WAR PROFITEERS", "PEACE FEELER" };
        private static readonly string[] Kickers = { "THE CABINET MEETS", "BASE LOST", "BASE TAKEN", "THE SQUADRONS", "THE TREASURY", "THE ENEMY WRITES" };
        private static readonly string[] Art = { "cabinet_war_cabinet", "cabinet_fall_of", "cabinet_victory_at", "cabinet_heavy_losses", "cabinet_war_profiteers", "cabinet_peace_feeler" };

        private static readonly string[] Texts =
        {
            "The Premier calls the war cabinet. One doctrine, for the rest of the war; the other side will know by tonight.",
            "{0} has fallen. The papers want someone to blame; the Air Staff wants it back.",
            "The flag is up over {0}. The Premier wants a parade; the Air Staff wants to keep rolling.",
            "Four of our aircraft down in five minutes. The crews are tired and the papers count the empty bunks.",
            "The treasury is fat and the contractors fatter. The opposition asks who pays for this war.",
            "{0} sends word through a neutral embassy: talks, if we stop now.",
        };

        // One string per option: the label, then its chips. A chip's first character is its tone for the reader: + good, - bad, = neutral.
        private static readonly string[][] Options =
        {
            new[] { "TOTAL WAR|=R1 GENERAL OFFENSIVE|=R2 AIR SURGE|=R3 STRATEGIC STRIKE", "FORTRESS|=R1 HOLD THE LINE|=R2 SHIELD|=R3 COUNTERATTACK",
                    "SHADOW WAR|=R1 SABOTAGE|=R2 PROPAGANDA|=R3 DEFECTOR" },
            new[] { "VOW REVENGE|+WAR SUPPORT +5|=STAFF BOLD 5 MIN", "ORDERLY RETREAT|-WAR SUPPORT -5|=CAUTIOUS 5 MIN|+AI JETS +2" },
            new[] { "PARADE|+WAR SUPPORT +5|-FUNDS -1 000", "PRESS ON|+FREE WAVE|=NO COST" },
            new[] { "ROTATE CREWS|+WAR SUPPORT +5|-AI JETS -2 FOR 10 MIN", "FIGHT ON|-WAR SUPPORT -5|=JETS STAY UP" },
            new[] { "WINDFALL TAX|+FUNDS +3 000|-WAR SUPPORT -5", "LOOK AWAY|+PLANTS +25 % FOR 10 MIN|=NO COST" },
            new[] { "REFUSE|+WAR SUPPORT +10", "TALK|-WAR SUPPORT -10|+THEIRS -10 TOO" },
        };

        /// <summary>The answer the staff marks (WAR CABINET's is the host's call).</summary>
        private static readonly int[] StaffChoices = { 0, 0, 1, 0, 1, 0 };

        public static bool Valid(int card) => card >= 0 && card < Count;
        public static int OptionCount(int card) => Valid(card) ? Options[card].Length : 0;
        public static int StaffChoice(int card) => Valid(card) ? StaffChoices[card] : 0;
        public static string Title(int card, string place) => Valid(card) ? Titles[card].Replace("{0}", place ?? "") : "";
        public static string ArtKey(int card) => Valid(card) ? Art[card] : "";

        public static string OptionLabel(int card, int option)
        {
            if (!Valid(card) || option < 0 || option >= Options[card].Length) return "";
            string s = Options[card][option];
            int bar = s.IndexOf('|');
            return bar < 0 ? s : s.Substring(0, bar);
        }

        /// <summary>Fills <paramref name="into"/> (reused) from the view's open card. False when there is none.</summary>
        public static bool Fill(NationView v, CabinetCard into)
        {
            int card = v.CabinetId;
            if (v.CabinetSerial <= 0 || !Valid(card)) return false;
            into.Serial = v.CabinetSerial;
            into.Card = card;
            into.ArtKey = Art[card];
            into.EmblemKey = card == (int)CardId.WarCabinet && v.CabinetChosen >= 0 ? Doctrines.ArtKey((Doctrine)(v.CabinetChosen + 1)) : "";
            into.Kicker = Kickers[card];
            into.Title = Title(card, v.CabinetPlace);
            into.Text = Texts[card].Replace("{0}", v.CabinetPlace ?? "");
            into.StaffChoice = v.CabinetStaff;
            into.Chosen = v.CabinetChosen;
            into.ChosenBy = v.CabinetChosenBy ?? "";
            into.SecondsLeft = v.CabinetLeft;
            into.SecondsTotal = AnswerSeconds;
            string[] options = Options[card];
            while (into.Options.Count < options.Length) into.Options.Add(new CabinetOption());
            while (into.Options.Count > options.Length) into.Options.RemoveAt(into.Options.Count - 1);
            for (int i = 0; i < options.Length; i++)
            {
                string[] parts = options[i].Split('|');
                CabinetOption o = into.Options[i];
                o.Label = parts[0];
                o.Chips.Clear();
                for (int c = 1; c < parts.Length; c++)
                    o.Chips.Add(new CabinetChip { Text = parts[c].Substring(1), Tone = (sbyte)(parts[c][0] == '+' ? 1 : parts[c][0] == '-' ? -1 : 0) });
            }
            return true;
        }
    }

    internal struct CabinetChip
    {
        public string Text;
        /// <summary>+1 good for the reader's side, -1 bad, 0 neutral.</summary>
        public sbyte Tone;
    }

    internal sealed class CabinetOption
    {
        public string Label = "";
        public readonly List<CabinetChip> Chips = new List<CabinetChip>(4);
    }

    /// <summary>One cabinet card as the dispatch shows it. The same <see cref="Serial"/> shown again updates it in place (timer, answer).</summary>
    internal sealed class CabinetCard
    {
        public int Serial, Card = -1;
        /// <summary>Bundled poster key (EventArtCache) and, once WAR CABINET is answered, the doctrine emblem (NationArt).</summary>
        public string ArtKey = "", EmblemKey = "";
        public string Kicker = "", Title = "", Text = "";
        public readonly List<CabinetOption> Options = new List<CabinetOption>(3);
        public int StaffChoice, Chosen = -1;
        public string ChosenBy = "";
        public float SecondsLeft, SecondsTotal;
    }

    /// <summary>Client-local (Events): shows the cabinet card on EVN DISPATCH and in the shared map event notice.</summary>
    internal interface IPoliticalDispatch
    {
        /// <summary>Shows or updates a card. <paramref name="pick"/> receives the option a local press chose; the host decides.</summary>
        void Show(CabinetCard card, Action<int> pick);

        /// <summary>Takes the card off screen.</summary>
        void Close(int serial);
    }

    /// <summary>Political headlines (doctrines, decisions, answers, for both sides) for the game log's NEWS. Client-local; Command listens.</summary>
    internal static class PoliticsWire
    {
        public static event Action<string> Posted;

        public static void Post(string headline)
        {
            if (string.IsNullOrEmpty(headline)) return;
            try { Posted?.Invoke(headline); }
            catch (Exception) { /* a log fault never stops politics */ }
        }
    }
}
