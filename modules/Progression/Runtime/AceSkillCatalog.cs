namespace BoscaliSummer.Features.Progression.Runtime
{
    /// <summary>One shared combat skill. Wing Command owns the effects and gates them per
    /// tier; SQD presents the same names and badges for player, AI and enemy aces.</summary>
    internal readonly struct AceSkillDefinition
    {
        public readonly int Bit;
        public readonly string Code;
        public readonly string Name;
        public readonly string Description;

        public AceSkillDefinition(int bit, string code, string name, string description)
        {
            Bit = bit;
            Code = code;
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// Wing Command's four ace survival skills. This catalogue is presentation metadata only:
    /// Boscali reads the host-confirmed four-bit mask through <c>WingLink.AceAbilityMask</c>
    /// and never grants or applies a skill itself.
    /// </summary>
    internal static class AceSkillCatalog
    {
        public const int MaximumSkills = 4;
        public const int MaskWindow = 15;

        public static readonly AceSkillDefinition[] All =
        {
            new AceSkillDefinition(0, "TOUGH", "Toughness",
                "Reduced pilot damage while seated."),
            new AceSkillDefinition(1, "CM", "Countermeasures",
                "Faster flare bursts and stronger ECM."),
            new AceSkillDefinition(2, "NOTCH", "Notch Expert",
                "Radar-guided missiles lose track while beaming."),
            new AceSkillDefinition(3, "GHOST", "Ghost",
                "Guided missiles veer off at long range.")
        };

        public static bool IsDefined(int index) => index >= 0 && index < All.Length;

        public static bool Has(int abilityMask, int index) =>
            IsDefined(index) && (abilityMask & (1 << All[index].Bit)) != 0;
    }
}
