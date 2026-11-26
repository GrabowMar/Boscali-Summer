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
        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(ModuleContext context)
        {
            DisplayGrade grade = context.AddSceneService<DisplayGrade>(49);
            grade.Configure(context.Settings.Effects, context.Logger);
            context.AddClientEffect(grade);

            var fx = context.Settings.Effects;
            Func<float, string> signed = v => Math.Round(v).ToString("+0;-0;0");
            context.AddClientSetting("COLOUR GRADE", "SKY DITHER",
                "Reduce colour banding in sky and fog gradients. Uses a very fine noise pattern at final output.", fx.Dithering);
            context.AddClientChoice("COLOUR GRADE", "EDGE SMOOTHING",
                "GAME: keep the game's anti-aliasing. SMAA: smoother aircraft and cockpit edges, with extra GPU work.",
                fx.EdgeSmoothing, new[] { "GAME", "SMAA" });
            ClientSettingToggle cinematic = context.AddClientSetting("COLOUR GRADE", "CINEMATIC",
                "Realistic, atmospheric or tactical filmic grading, selective bloom, lens vignette and grain.", fx.Cinematic.Enabled);
            context.AddClientChoice("COLOUR GRADE", "CINEMA LOOK",
                "ATMOSPHERIC: moody sun- and weather-reactive film grade with soft bloom. REALISTIC: cleaner, subtler photographic look. TACTICAL: BF4-inspired cool shadows and stronger light streaks.",
                fx.Cinematic.Look, new[] { "ATMOSPHERIC", "REALISTIC", "TACTICAL" }, cinematic);
            context.AddClientLevel("COLOUR GRADE", "CINEMA STRENGTH", "How strongly the cinematic look changes the picture.",
                fx.Cinematic.Strength, 0f, 1f, .1f, requires: cinematic);
            context.AddClientLevel("COLOUR GRADE", "LIGHT GLOW", "Extra bloom and horizontal streaks around bright lights and exhausts.",
                fx.Cinematic.Bloom, 0f, 1f, .1f, requires: cinematic);
            context.AddClientLevel("COLOUR GRADE", "LENS VIGNETTE", "Soft darkening around screen edges.",
                fx.Cinematic.Vignette, 0f, 1f, .1f, requires: cinematic);
            context.AddClientLevel("COLOUR GRADE", "FILM GRAIN", "Subtle organic film grain, strongest in darker areas.",
                fx.Cinematic.Grain, 0f, 1f, .1f, requires: cinematic);
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
