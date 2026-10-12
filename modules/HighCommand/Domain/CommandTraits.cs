using System;
using System.Text;

namespace BoscaliSummer.Modules.HighCommand.Domain
{
    [Flags]
    internal enum CommandTrait
    {
        None = 0,
        Logistician = 1 << 0,
        Beloved = 1 << 1,
        Zealot = 1 << 2,
        Veteran = 1 << 3,
        Recluse = 1 << 4,
    }

    /// <summary>
    /// What a commander is worth to the faction that keeps them alive. Every effect is
    /// economic or informational - income, kill value, how far a post's patrols see, how hard
    /// a loss lands - and is stated to the player as a bonus line on the command page. Trait
    /// effects never spawn, retask, damage or move a unit.
    /// </summary>
    internal static class CommandTraits
    {
        public const int All = (1 << 5) - 1;
        public const int MinimumBonusBudget = 96;
        /// <summary>The longest strike line the sortie card carries.</summary>
        public const int MaximumStrikeLine = 64;

        public static bool Has(CommandTrait mask, CommandTrait trait) => (mask & trait) == trait;

        public static float WeightMultiplier(CommandTrait mask)
        {
            float value = 1f;
            if (Has(mask, CommandTrait.Beloved)) value += 0.25f;
            if (Has(mask, CommandTrait.Zealot)) value += 0.15f;
            return value;
        }

        public static float StipendMultiplier(CommandTrait mask) =>
            Has(mask, CommandTrait.Logistician) ? 1.15f : 1f;

        public static float BountyMultiplier(CommandTrait mask) =>
            Has(mask, CommandTrait.Veteran) ? 1.2f : 1f;

        public static float IntelRadiusMultiplier(CommandTrait mask) =>
            Has(mask, CommandTrait.Recluse) ? 0.7f : 1f;

        public static float DisruptionBonus(CommandTrait mask) =>
            Has(mask, CommandTrait.Beloved) ? 30f : 0f;

        public static string Label(CommandTrait trait)
        {
            switch (trait)
            {
                case CommandTrait.Logistician: return "LOGISTICS MIND";
                case CommandTrait.Beloved: return "BELOVED LEADER";
                case CommandTrait.Zealot: return "ZEALOT";
                case CommandTrait.Veteran: return "FIELD VETERAN";
                case CommandTrait.Recluse: return "RECLUSE";
                default: return "UNKNOWN";
            }
        }

        /// <summary>The effect one trait carries, in the console's own words.</summary>
        public static string Effect(CommandTrait trait)
        {
            switch (trait)
            {
                case CommandTrait.Logistician: return "+15% STIPEND";
                case CommandTrait.Beloved: return "+25% SHARE, COSTLIER LOSS";
                case CommandTrait.Zealot: return "+15% SHARE";
                case CommandTrait.Veteran: return "+20% KILL VALUE";
                case CommandTrait.Recluse: return "-30% PATROL SIGHT";
                default: return "";
            }
        }

        /// <summary>
        /// One entry per trait: the name and what it pays. This is the page's answer to
        /// "what is this commander actually worth", so it is the string the board carries.
        /// </summary>
        public static string BonusLine(CommandTrait mask)
        {
            var builder = new StringBuilder(MinimumBonusBudget);
            for (int bit = 0; bit < 5 && builder.Length < MinimumBonusBudget; bit++)
            {
                var trait = (CommandTrait)(1 << bit);
                if (!Has(mask, trait)) continue;
                if (builder.Length > 0) builder.Append(" · ");
                builder.Append(Label(trait)).Append(' ').Append(Effect(trait));
            }
            return builder.Length == 0 ? "NO STAFF BONUS" : builder.ToString();
        }

        /// <summary>What killing a commander with this trait means to the attacker (the friendly side reads Effect).</summary>
        public static string StrikeEffect(CommandTrait trait)
        {
            switch (trait)
            {
                case CommandTrait.Logistician: return "THEIR STIPEND +15%";
                case CommandTrait.Beloved: return "LOSS HURTS THEM 30 S LONGER";
                case CommandTrait.Zealot: return "+15% OF THEIR STAFF";
                case CommandTrait.Veteran: return "BOUNTY +20% TO YOU";
                case CommandTrait.Recluse: return "HARD TO SPOT (1.8 KM)";   // IntelPostRadius 2600 m x 0.7
                default: return "";
            }
        }

        /// <summary>
        /// The enemy post's dossier line: what its commander is worth to the faction that must kill them. Up to two traits
        /// (the most a generated commander has) joined by " · "; a trait that would not fit MaximumStrikeLine is left out.
        /// </summary>
        public static string StrikeLine(CommandTrait mask)
        {
            var builder = new StringBuilder(MaximumStrikeLine);
            for (int bit = 0; bit < 5; bit++)
            {
                var trait = (CommandTrait)(1 << bit);
                if (!Has(mask, trait)) continue;
                string effect = StrikeEffect(trait);
                int separator = builder.Length > 0 ? 3 : 0;
                if (builder.Length + separator + effect.Length > MaximumStrikeLine) break;
                if (separator > 0) builder.Append(" · ");
                builder.Append(effect);
            }
            return builder.Length == 0 ? "NO STAFF BONUS" : builder.ToString();
        }
    }
}
