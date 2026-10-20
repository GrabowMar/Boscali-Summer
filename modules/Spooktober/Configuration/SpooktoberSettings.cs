using BepInEx.Configuration;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Modules.Spooktober.Domain;

namespace BoscaliSummer.Modules.Spooktober.Configuration
{
    internal sealed class SpooktoberSettings
    {
        public readonly ConfigEntry<bool> Enabled;
        /// <summary>The player's saved progress (<see cref="SpookProgress.Save"/>). Not in ConfigMenu and tagged Browsable = false, so the F1 window hides it even in the expert view.</summary>
        public readonly ConfigEntry<string> Progress;
        public readonly ConfigEntry<float> AceDamageScale, AcePhantomRangeKm, AceBlinkCooldown, AceRepairPerSecond, AceGunDamageScale;

        public SpooktoberSettings(ConfigFile config)
        {
            Enabled = config.Bind("Spooktober", "Enabled", true,
                "Seasonal easter egg: a mystery transmission, puzzles hidden in SET and a secret boss menu.");
            // Browsable = false rides ahead of ConfigMenu's own hint, which leaves Browsable unset in the ExpertSettings view, so it stays hidden.
            Progress = config.Bind("Spooktober", "Progress", "", new ConfigDescription(
                "What this player has solved and beaten (written by the game; delete the value to start over).", null,
                new ConfigurationManagerAttributes { Browsable = false }));
            AceDamageScale = config.Bind("Spooktober", "AceDamageScale", 0.35f, new ConfigDescription(
                "How much of the damage it is dealt the fallen ace takes. Host only; applies from the next ace spawn.", new AcceptableValueRange<float>(0.05f, 1f)));
            AcePhantomRangeKm = config.Bind("Spooktober", "AcePhantomRangeKm", 3f, new ConfigDescription(
                "A missile launched at the fallen ace from farther than this is a phantom and veers off. Host only; applies from the next ace spawn.",
                new AcceptableValueRange<float>(0.5f, 10f)));
            AceBlinkCooldown = config.Bind("Spooktober", "AceBlinkCooldown", 20f, new ConfigDescription(
                "Seconds between the fallen ace's blinks away from heavy damage. Host only; applies from the next ace spawn.", new AcceptableValueRange<float>(5f, 120f)));
            AceRepairPerSecond = config.Bind("Spooktober", "AceRepairPerSecond", 0.01f, new ConfigDescription(
                "Hull fraction per second the fallen ace repairs once it has gone a few seconds unhit. Host only; applies from the next ace spawn.",
                new AcceptableValueRange<float>(0f, 0.1f)));
            AceGunDamageScale = config.Bind("Spooktober", "AceGunDamageScale", 1f, new ConfigDescription(
                "How hard the fallen ace's gun rounds hit (1 = the game's own). Host only; applies from the next ace spawn.", new AcceptableValueRange<float>(0.5f, 5f)));
        }

        /// <summary>The ace's defaults with the four knobs applied (range in metres).</summary>
        public AceTuning Tuning()
        {
            AceTuning t = AceTuning.Default;
            t.DamageScale = AceDamageScale.Value;
            t.PhantomRange = AcePhantomRangeKm.Value * 1000f;
            t.BlinkCooldown = AceBlinkCooldown.Value;
            t.RepairPerSecond = AceRepairPerSecond.Value;
            return t;
        }
    }
}
