using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Nation.Domain
{
    /// <summary>Every number of POLITICS v2 (docs/superpowers/specs/2026-11-14-politics-design.md). Pure.</summary>
    internal static class PoliticsRules
    {
        public const float Start = 40f, Max = 100f;
        /// <summary>Under 25 the staff may not go BOLD and the AI fields two fewer jets; over 75 it fields two more.</summary>
        public const float NoBoldBelow = 25f, MoreJetsAbove = 75f;
        public const int ThresholdJets = 2;
        /// <summary>The staff spends war support above 60, and never below 25 (it would cost the side its BOLD and two jets).</summary>
        public const float StaffSpendsAbove = 60f, StaffKeeps = 25f;
        public const float RankTwoSeconds = 900f;
        public const float BaseTaken = 10f, AircraftDown = 1f, PilotDown = -2f, SiteDestroyed = 3f, FactoryLost = -3f;
        public const float OffensiveTaken = 15f, SabotageDestroyed = 5f;
        public const float HeavyLossSeconds = 300f;
        public const int HeavyLossCount = 4;
        public const float ProfiteersFunds = 8000f, PeaceFeelerBelow = 20f;
        public const float WarBondsFunds = 3000f, ParadeFunds = 1000f, WindfallFunds = 3000f;
        /// <summary>A move with the same words as the newest one within this long adds to it (a run of kills is one line).</summary>
        public const float MergeSeconds = 60f;
        public const int MaxMoves = 4;
        public const string Staff = "THE STAFF";

        /// <summary>The staff's mark on WAR CABINET: ahead in airbases TOTAL WAR, behind FORTRESS, even SHADOW WAR (option index).</summary>
        public static int WarCabinetPick(int ours, int theirs) => ours > theirs ? 0 : ours < theirs ? 1 : 2;
    }

    /// <summary>What a decision or an answer asks of the game beyond the book's own numbers; the runtime applies it.</summary>
    internal struct PoliticsEffect
    {
        public float Funds, EnemySupport;
        public int Warheads;
        public bool Wave, Offensive, Counterattack, Fortify, Sabotage;
    }

    internal struct SupportLine
    {
        public float Delta, At;
        public string Text;
    }

    /// <summary>
    /// One faction's politics on the host: war support and why it moved, the doctrine and its self-running rank, the decisions' clocks,
    /// the timed answers of the cabinet and the one open card. Every verb judges itself; "by" is a callsign or <see cref="PoliticsRules.Staff"/>.
    /// </summary>
    internal sealed class PoliticsBook
    {
        public float WarSupport = PoliticsRules.Start;
        public readonly List<SupportLine> Moves = new List<SupportLine>(PoliticsRules.MaxMoves + 1);
        public Doctrine Doctrine;
        public float ChosenAt = -1f;
        public string ChosenBy = "";
        /// <summary>Per decision: when it may be taken again, and until when it is in force (mission time).</summary>
        public readonly float[] ReadyAt = new float[Decisions.Count], Until = new float[Decisions.Count];
        public int Decided = -1, DecidedSerial;
        public float DecidedAt;
        public string DecidedBy = "";
        /// <summary>The cabinet's timed answers.</summary>
        public float BoldUntil, CautiousUntil, RetreatUntil, RotateUntil, LookAwayUntil;
        /// <summary>Mission times our aircraft went down (HEAVY LOSSES).</summary>
        public readonly List<float> Losses = new List<float>(8);
        // The open card (Card -1 = none).
        public int Serial, Card = -1, Staff, Chosen = -1;
        public string Place = "", AnsweredBy = "";
        public float OpenedAt, ClosesAt, AnsweredAt;
        public float LastCardAt = -1e6f;
        public readonly float[] CardAt = { -1e6f, -1e6f, -1e6f, -1e6f, -1e6f, -1e6f };
        /// <summary>Set by anything a client would see; the owner clears it when it has sent a snapshot.</summary>
        public bool Dirty = true;

        // ---- War support -----------------------------------------------------------------------

        public void Earn(float delta, string why, float now)
        {
            float before = WarSupport;
            WarSupport = Clamp(WarSupport + delta, 0f, PoliticsRules.Max);
            float moved = WarSupport - before;
            if (moved == 0f) return;
            if (Moves.Count > 0 && Moves[0].Text == why && now - Moves[0].At < PoliticsRules.MergeSeconds)
                Moves[0] = new SupportLine { Delta = Moves[0].Delta + moved, At = now, Text = why };
            else
            {
                Moves.Insert(0, new SupportLine { Delta = moved, At = now, Text = why ?? "" });
                if (Moves.Count > PoliticsRules.MaxMoves) Moves.RemoveAt(PoliticsRules.MaxMoves);
            }
            Dirty = true;
        }

        public void Lost(float now)
        {
            Losses.Add(now);
            while (Losses.Count > 0 && now - Losses[0] > PoliticsRules.HeavyLossSeconds) Losses.RemoveAt(0);
        }

        public int LossesNow(float now)
        {
            int n = 0;
            for (int i = 0; i < Losses.Count; i++) if (now - Losses[i] <= PoliticsRules.HeavyLossSeconds) n++;
            return n;
        }

        // ---- Doctrine and decisions -------------------------------------------------------------

        /// <summary>Rank 1 when chosen, 2 after 15 minutes, 3 at the mission's first escalation stage (0 before a doctrine).</summary>
        public int Rank(float now, int stage) =>
            Doctrine == Doctrine.None ? 0 : stage >= 1 ? 3 : now - ChosenAt >= PoliticsRules.RankTwoSeconds ? 2 : 1;

        public bool InForce(DecisionId id, float now) => now < Until[(int)id];

        /// <summary>Why a decision cannot be taken now, or Ok: another doctrine's (Locked), its rank not reached (NotOpen), cooling (Busy), too dear.</summary>
        public NationOutcome Check(int id, float now, int stage)
        {
            if (!Decisions.Valid(id)) return NationOutcome.Unknown;
            Doctrine own = Decisions.DoctrineOf(id);
            if (own != Doctrine.None && own != Doctrine) return NationOutcome.Locked;
            if (Decisions.Rank(id) > Rank(now, stage)) return NationOutcome.NotOpen;
            if (now < ReadyAt[id]) return NationOutcome.Busy;
            if (WarSupport < Decisions.Cost(id)) return NationOutcome.NoSupport;
            return NationOutcome.Ok;
        }

        public NationOutcome Decide(int id, string by, float now, int stage, out PoliticsEffect effect)
        {
            effect = default;
            NationOutcome outcome = Check(id, now, stage);
            if (outcome != NationOutcome.Ok) return outcome;
            by = by ?? "";
            Earn(-Decisions.Cost(id), Decisions.Name(id) + (by.Length > 0 ? " · " + by : ""), now);
            ReadyAt[id] = now + Decisions.CooldownSeconds;
            Until[id] = now + Decisions.Lasts(id);
            Decided = id;
            DecidedSerial++;
            DecidedAt = now;
            DecidedBy = by;
            switch ((DecisionId)id)
            {
                case DecisionId.WarBonds: effect.Funds = PoliticsRules.WarBondsFunds; break;
                case DecisionId.GeneralOffensive: effect.Offensive = true; effect.Wave = true; break;
                case DecisionId.StrategicStrike: effect.Warheads = 1; break;
                case DecisionId.HoldTheLine: effect.Fortify = true; break;
                case DecisionId.Counterattack: effect.Counterattack = true; effect.Wave = true; break;
                case DecisionId.Sabotage: effect.Sabotage = true; break;
                case DecisionId.Propaganda: effect.EnemySupport = -20f; break;
            }
            Dirty = true;
            return NationOutcome.Ok;
        }

        /// <summary>The decision the staff takes now, or -1: only above 60 and never below 25 after paying; the doctrine's highest rank first, then SURGE PRODUCTION, then WAR BONDS.</summary>
        public int StaffPick(float now, int stage)
        {
            if (WarSupport <= PoliticsRules.StaffSpendsAbove) return -1;
            for (int rank = 3; rank >= 1; rank--)
            {
                int id = Decisions.Of(Doctrine, rank);
                if (id >= 0 && Affordable(id, now, stage)) return id;
            }
            if (Affordable((int)DecisionId.SurgeProduction, now, stage)) return (int)DecisionId.SurgeProduction;
            return Affordable((int)DecisionId.WarBonds, now, stage) ? (int)DecisionId.WarBonds : -1;
        }

        private bool Affordable(int id, float now, int stage) =>
            Check(id, now, stage) == NationOutcome.Ok && now >= Until[id] && WarSupport - Decisions.Cost(id) >= PoliticsRules.StaffKeeps;

        // ---- The cabinet ------------------------------------------------------------------------

        /// <summary>One card at a time; WAR CABINET only before a doctrine (and past the gap rules), the rest one per 8 minutes and not the same within 15.</summary>
        public bool CanOpen(int card, float now)
        {
            if (Card >= 0 || !CabinetCards.Valid(card)) return false;
            if (card == (int)CardId.WarCabinet) return Doctrine == Doctrine.None;
            return now - LastCardAt >= CabinetCards.GapSeconds && now - CardAt[card] >= CabinetCards.RepeatSeconds;
        }

        public bool Open(int card, string place, int staff, float now)
        {
            if (!CanOpen(card, now)) return false;
            Serial++;
            Card = card;
            Place = place ?? "";
            Staff = staff >= 0 && staff < CabinetCards.OptionCount(card) ? staff : 0;
            Chosen = -1;
            AnsweredBy = "";
            OpenedAt = now;
            ClosesAt = now + CabinetCards.AnswerSeconds;
            LastCardAt = now;
            CardAt[card] = now;
            Dirty = true;
            return true;
        }

        /// <summary>The condition cards due now (WAR CABINET, HEAVY LOSSES, WAR PROFITEERS, PEACE FEELER), or -1. The event cards open on their event.</summary>
        public int DueCard(float now, float funds, float enemySupport)
        {
            if (now >= CabinetCards.WarCabinetAt && CanOpen((int)CardId.WarCabinet, now)) return (int)CardId.WarCabinet;
            if (LossesNow(now) >= PoliticsRules.HeavyLossCount && CanOpen((int)CardId.HeavyLosses, now)) return (int)CardId.HeavyLosses;
            if (funds > PoliticsRules.ProfiteersFunds && CanOpen((int)CardId.WarProfiteers, now)) return (int)CardId.WarProfiteers;
            if (enemySupport < PoliticsRules.PeaceFeelerBelow && CanOpen((int)CardId.PeaceFeeler, now)) return (int)CardId.PeaceFeeler;
            return -1;
        }

        public bool StaffDue(float now) => Card >= 0 && Chosen < 0 && now >= ClosesAt;

        /// <summary>The first answer wins; the staff answers with its mark when nobody does (<see cref="StaffDue"/>).</summary>
        public NationOutcome Answer(int serial, int option, string by, float now, out PoliticsEffect effect)
        {
            effect = default;
            if (Card < 0 || serial != Serial) return NationOutcome.NotOpen;
            if (Chosen >= 0) return NationOutcome.Busy;
            if (option < 0 || option >= CabinetCards.OptionCount(Card)) return NationOutcome.Unknown;
            Chosen = option;
            AnsweredBy = by ?? "";
            AnsweredAt = now;
            switch ((CardId)Card)
            {
                case CardId.WarCabinet:
                    Doctrine = (Doctrine)(option + 1);
                    ChosenAt = now;
                    ChosenBy = AnsweredBy;
                    break;
                case CardId.FallOf:
                    if (option == 0) { Earn(5f, "VOW REVENGE", now); BoldUntil = now + 300f; }
                    else { Earn(-5f, "ORDERLY RETREAT", now); CautiousUntil = now + 300f; RetreatUntil = now + 300f; }
                    break;
                case CardId.VictoryAt:
                    if (option == 0) { Earn(5f, "PARADE IN THE CAPITAL", now); effect.Funds = -PoliticsRules.ParadeFunds; }
                    else effect.Wave = true;
                    break;
                case CardId.HeavyLosses:
                    if (option == 0) { Earn(5f, "CREWS ROTATED", now); RotateUntil = now + 600f; }
                    else Earn(-5f, "FIGHT ON", now);
                    break;
                case CardId.WarProfiteers:
                    if (option == 0) { Earn(-5f, "WINDFALL TAX", now); effect.Funds = PoliticsRules.WindfallFunds; }
                    else LookAwayUntil = now + 600f;
                    break;
                case CardId.PeaceFeeler:
                    if (option == 0) Earn(10f, "PEACE FEELER REFUSED", now);
                    else { Earn(-10f, "PEACE TALKS", now); effect.EnemySupport = -10f; }
                    break;
            }
            Dirty = true;
            return NationOutcome.Ok;
        }

        /// <summary>Takes an answered card off once it has stood <see cref="CabinetCards.AnsweredSeconds"/>.</summary>
        public void Tick(float now)
        {
            if (Card < 0 || Chosen < 0 || now - AnsweredAt < CabinetCards.AnsweredSeconds) return;
            Card = -1;
            Dirty = true;
        }

        // ---- What it does to the war -------------------------------------------------------------

        /// <summary>The posture band as bytes (0 cautious .. 2 bold): a cabinet CAUTIOUS, else a forced BOLD, else no BOLD under 25.</summary>
        public void Band(float now, out byte floor, out byte ceiling)
        {
            if (now < CautiousUntil) { floor = ceiling = 0; return; }
            if (now < BoldUntil || InForce(DecisionId.GeneralOffensive, now)) { floor = ceiling = 2; return; }
            floor = 0;
            ceiling = WarSupport < PoliticsRules.NoBoldBelow ? (byte)1 : (byte)2;
        }

        public int Jets(float now) =>
            (WarSupport < PoliticsRules.NoBoldBelow ? -PoliticsRules.ThresholdJets : WarSupport > PoliticsRules.MoreJetsAbove ? PoliticsRules.ThresholdJets : 0) +
            (InForce(DecisionId.AirSurge, now) ? 4 : 0) + (now < RetreatUntil ? 2 : 0) - (now < RotateUntil ? 2 : 0);

        public float Output(float now) => (InForce(DecisionId.SurgeProduction, now) ? 1.5f : 1f) * (now < LookAwayUntil ? 1.25f : 1f);

        public float Defence(float now) => InForce(DecisionId.Shield, now) ? 1.5f : 1f;

        /// <summary>The skill the other side's AI spawns with while our DEFECTOR runs.</summary>
        public float EnemySkill(float now) => InForce(DecisionId.Defector, now) ? 0.9f : 1f;

        // ---- The view -----------------------------------------------------------------------------

        /// <summary>This side's politics into the view a screen draws (times as seconds left or ago).</summary>
        public void Fill(NationView v, float now, int stage)
        {
            v.WarSupport = WarSupport;
            v.Moves.Clear();
            for (int i = 0; i < Moves.Count; i++) v.Moves.Add(new SupportMove { Delta = Moves[i].Delta, Text = Moves[i].Text, Age = now - Moves[i].At });
            v.Doctrine = Doctrine;
            v.Rank = Rank(now, stage);
            v.DoctrineAge = Doctrine == Doctrine.None ? -1f : now - ChosenAt;
            v.DoctrineBy = ChosenBy;
            Band(now, out v.PostureFloor, out v.PostureCeiling);
            v.JetsDelta = Jets(now);
            for (int i = 0; i < Decisions.Count; i++)
            {
                v.Cooldown[i] = ReadyAt[i] > now ? ReadyAt[i] - now : 0f;
                v.InForce[i] = Until[i] > now ? Until[i] - now : 0f;
            }
            v.Decided = Decided;
            v.DecidedSerial = DecidedSerial;
            v.DecidedBy = DecidedBy;
            v.DecidedAge = Decided >= 0 ? now - DecidedAt : 0f;
            v.CabinetSerial = Card >= 0 ? Serial : 0;
            v.CabinetId = Card;
            v.CabinetPlace = Place;
            v.CabinetStaff = Staff;
            v.CabinetChosen = Card >= 0 ? Chosen : -1;
            v.CabinetChosenBy = Card >= 0 ? AnsweredBy : "";
            v.CabinetLeft = Card >= 0 && Chosen < 0 ? Clamp(ClosesAt - now, 0f, CabinetCards.AnswerSeconds) : 0f;
        }

        /// <summary>The other side as this side's view shows it (its name and DEFECTOR intel come from the runtime).</summary>
        public void FillEnemy(NationView v, float now, int stage)
        {
            v.EnemyDoctrine = Doctrine;
            v.EnemyRank = Rank(now, stage);
            v.EnemyWarSupport = WarSupport;
            v.EnemyDecided = Decided;
            v.EnemyDecidedSerial = DecidedSerial;
            v.EnemyDecidedAge = Decided >= 0 ? now - DecidedAt : 0f;
            v.EnemyDecidedLeft = Decided >= 0 && Until[Decided] > now ? Until[Decided] - now : 0f;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
