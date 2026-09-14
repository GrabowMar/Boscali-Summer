using System;
using System.Text;

namespace BoscaliSummer.Features.HighCommand.Domain
{
    [Flags]
    internal enum CommandTrait
    {
        None = 0,
        Logistician = 1 << 0,
        Beloved = 1 << 1,
        Zealot = 1 << 2,
        Veteran = 1 << 3,
        Political = 1 << 4,
        Recluse = 1 << 5,
    }

    /// <summary>
    /// Trait effects are intentionally small, additive and economic-only. They change how
    /// much a live staff is worth and how easily it is found — never spawns, AI or damage.
    /// </summary>
    internal static class CommandTraits
    {
        public const int All = (1 << 6) - 1;
        public const int MinimumLabelBudget = 48;

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
                case CommandTrait.Political: return "POLITICAL ANIMAL";
                case CommandTrait.Recluse: return "RECLUSE";
                default: return "UNKNOWN";
            }
        }

        /// <summary>Display form for the console, bounded so a dossier line never overprints.</summary>
        public static string Labels(CommandTrait mask)
        {
            var builder = new StringBuilder(MinimumLabelBudget);
            for (int bit = 0; bit < 6 && builder.Length < MinimumLabelBudget; bit++)
            {
                var trait = (CommandTrait)(1 << bit);
                if (!Has(mask, trait)) continue;
                if (builder.Length > 0) builder.Append(", ");
                builder.Append(Label(trait));
            }
            return builder.Length == 0 ? "NO NOTABLE TRAITS" : builder.ToString();
        }
    }
}
