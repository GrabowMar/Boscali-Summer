using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Wing.Domain
{
    internal enum WingRank { Rookie, Wingman, Veteran, Ace, Legend }
    internal enum PilotRecoveryStatus { None, Downed, Missing, Captured }
    internal enum PilotPerk
    {
        // Core Surviving
        Toughness, Commando, NotchExpert, FastLearner, QuickDraw, Standoff,
        // Merged & Special
        Countermeasures, Ghost, Talented,
        // Tactical Combat Skills (every one has an effect; a perk the game's own AI would override is not offered)
        Snapshot, HeadOnJoust, EnergyFighter, ApexHunter, SalvoSpecialist, TargetMaster,
        WingmanInstinct, TerrainHugger, BreakTurn, EarlyWarning
    }

    /// <summary>Engine-free progression and survival balance. Each promotion grants one unique perk, with
    /// Talented expanding available slots.</summary>
    internal static class PilotPerks
    {
        public static readonly int Count = Enum.GetValues(typeof(PilotPerk)).Length;
        public const float GuidanceOffset = 350f;
        public const float CommandoChance = 0.60f;
        public const float EscapeDelay = 90f;

        public static int XpForRank(WingRank rank) =>
            WingTuning.XpPerRank * (int)rank * ((int)rank + 1) / 2;

        public static WingRank RankFor(int xp)
        {
            WingRank rank = WingRank.Rookie;
            while (rank < WingRank.Legend && xp >= XpForRank(rank + 1)) rank++;
            return rank;
        }

        public static int AddXp(int current, int award) =>
            (int)Math.Min(int.MaxValue, (long)Math.Max(0, current) + Math.Max(0, award));

        public static int DesiredPerkCount(List<PilotPerk> owned, WingRank rank)
        {
            int extra = (owned != null && owned.Contains(PilotPerk.Talented)) ? 3 : 0;
            return Math.Min((int)rank + extra, Count);
        }

        public static void GrantThroughRank(List<PilotPerk> owned, WingRank rank, Func<int, int> random)
        {
            // Read again each draw: TALENTED, once drawn, adds its slots to this same call.
            while (owned.Count < DesiredPerkCount(owned, rank))
            {
                var available = new List<PilotPerk>();
                for (int i = 0; i < Count; i++)
                    if (!owned.Contains((PilotPerk)i)) available.Add((PilotPerk)i);
                if (available.Count == 0) return;
                owned.Add(available[random(available.Count)]);
            }
        }

        /// <summary>The rank's word (moved from WingPilotRoster for WING's Pure words).</summary>
        public static string RankName(WingRank rank)
        {
            switch (rank)
            {
                case WingRank.Wingman: return "WINGMAN";
                case WingRank.Veteran: return "VETERAN";
                case WingRank.Ace: return "ACE";
                case WingRank.Legend: return "LEGEND";
                default: return "ROOKIE";
            }
        }

        public static string Name(PilotPerk perk)
        {
            switch (perk)
            {
                case PilotPerk.Toughness: return "TOUGHNESS";
                case PilotPerk.Commando: return "COMMANDO";
                case PilotPerk.NotchExpert: return "NOTCH EXPERT";
                case PilotPerk.FastLearner: return "FAST LEARNER";
                case PilotPerk.QuickDraw: return "QUICK DRAW";
                case PilotPerk.Standoff: return "STANDOFF";
                case PilotPerk.Countermeasures: return "COUNTERMEASURES";
                case PilotPerk.Ghost: return "GHOST";
                case PilotPerk.Talented: return "TALENTED";
                case PilotPerk.Snapshot: return "SNAPSHOT";
                case PilotPerk.HeadOnJoust: return "HEAD-ON JOUST";
                case PilotPerk.EnergyFighter: return "ENERGY FIGHTER";
                case PilotPerk.ApexHunter: return "APEX HUNTER";
                case PilotPerk.SalvoSpecialist: return "SALVO SPECIALIST";
                case PilotPerk.TargetMaster: return "TARGET MASTER";
                case PilotPerk.WingmanInstinct: return "WINGMAN INSTINCT";
                case PilotPerk.TerrainHugger: return "TERRAIN HUGGER";
                case PilotPerk.BreakTurn: return "BREAK TURN";
                case PilotPerk.EarlyWarning: return "EARLY WARNING";
                default: return "UNKNOWN";
            }
        }

        public static string Description(PilotPerk perk)
        {
            switch (perk)
            {
                case PilotPerk.Toughness: return "50% less seated-pilot damage; does not strengthen the airframe.";
                case PilotPerk.Commando: return "One 60% escape chance after landing alive; return after 90 seconds if still free and alive.";
                case PilotPerk.NotchExpert: return "40% guidance-error chance against radar missiles first encountered within 15 degrees of a beam approach, while moving over 30m/s.";
                case PilotPerk.FastLearner: return "50% more XP from every award, reaching ranks and survival perks sooner.";
                case PilotPerk.QuickDraw: return "40% shorter interval between wing standing-fire shots from formation.";
                case PilotPerk.Standoff: return "30% longer reach for wing standing fire from formation; never beyond a missile's own range.";
                case PilotPerk.Countermeasures: return "Halves dispenser burst intervals and doubles ECM jamming intensity.";
                case PilotPerk.Ghost: return "35% guidance disruption chance against all incoming guided missiles (ARH, SARH, and IR seekers).";
                case PilotPerk.Talented: return "Natural aviation prodigy. Grants 3 additional skill slots, immediately unlocking extra abilities.";
                case PilotPerk.Snapshot: return "40% wider off-boresight tolerance for standing fire and Splash from formation.";
                case PilotPerk.HeadOnJoust: return "Standing fire and Splash launch from 25% farther when the target closes head-on faster than 350 m/s.";
                case PilotPerk.EnergyFighter: return "Preserves airspeed above corner speed in sustained maneuvers, trading altitude to prevent dogfight stalls.";
                case PilotPerk.ApexHunter: return "Radar missiles in standing fire and Splash launch from 40% farther above 4,000 m; a little more aggressive flying.";
                case PilotPerk.SalvoSpecialist: return "Splash also fires from a second missile type in reach (up to two missiles).";
                case PilotPerk.TargetMaster: return "Holds an attack order's target 50% longer through a short loss (a target saturated with missiles in flight).";
                case PilotPerk.WingmanInstinct: return "30% faster rejoin acceleration and 25% tighter station-keeping in escort formations.";
                case PilotPerk.TerrainHugger: return "25 m lower pull-up margin and low-level bank limit (the formation's height stays the wing's).";
                case PilotPerk.BreakTurn: return "Reacts at once to a missile inside 2,500 m while flying with the wing.";
                case PilotPerk.EarlyWarning: return "Reacts 1.5 seconds sooner to an incoming missile while flying with the wing.";
                default: return "Unknown perk.";
            }
        }

        // Pure mathematical and deterministic multipliers.

        public static float PilotDamage(float amount, bool enabled) => amount > 0f && enabled ? amount * 0.5f : amount;

        public static float EscapeChance(bool commando) => commando ? CommandoChance : 0f;

        public static float MissileErrorChance(bool ghost, bool notching) =>
            (float)Math.Min(0.85d, 1d - (ghost ? 0.65d : 1d) * (notching ? 0.55d : 1d));

        public static int Experience(int amount, bool fastLearner) => amount <= 0 ? 0 :
            (int)Math.Min(int.MaxValue, fastLearner ? ((long)amount * 3 + 1) / 2 : amount);
    }
}
