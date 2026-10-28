using System;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Effects.Runtime;

namespace BoscaliSummer.Modules.Effects
{
    internal sealed class EffectsModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("effects", "Effects");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Type.EmptyTypes;

        public void Install(ModuleContext context)
        {
            DisplayGrade grade = context.AddSceneService<DisplayGrade>(49);
            grade.Configure(context.Settings.Effects, context.Logger);
            context.AddClientEffect(grade);

            var fx = context.Settings.Effects;
            Func<float, string> signed = v => Math.Round(v).ToString("+0;-0;0");
            ClientSettingToggle graded = context.AddClientSetting("COLOUR GRADE", "COLOUR GRADE",
                "Contrast, saturation and white balance over the game's picture (and the optional .cube LUT named in the config file).",
                fx.Enabled);
            ClientSettingToggle sharpen = context.AddClientSetting("COLOUR GRADE", "SHARPEN",
                "FidelityFX RCAS sharpening in the final blit.", fx.SharpenEnabled, requires: graded);
            context.AddClientLevel("COLOUR GRADE", "CONTRAST", "Contrast over the game's own. 0 leaves it as it is.",
                fx.Contrast, -100f, 100f, 5f, signed, graded);
            context.AddClientLevel("COLOUR GRADE", "SATURATION", "Colour strength over the game's own. -100 is grey, 0 leaves it as it is.",
                fx.Saturation, -100f, 100f, 5f, signed, graded);
            context.AddClientLevel("COLOUR GRADE", "WARMTH", "White balance: below 0 cooler (bluer), above 0 warmer.",
                fx.Temperature, -100f, 100f, 5f, signed, graded);
            context.AddClientLevel("COLOUR GRADE", "SHARPNESS", "How hard SHARPEN works.", fx.SharpenStrength, 0f, 1f, .1f,
                requires: sharpen);
        }
    }
}
