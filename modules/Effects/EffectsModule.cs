using System;
using BoscaliSummer.Core.Modules;
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

            context.AddClientSetting("DISPLAY GRADE", "COLOUR GRADE",
                "Contrast, saturation, white balance and an optional .cube LUT. Tune the values in the F1 advanced view, section Effects.",
                context.Settings.Effects.Enabled);
            context.AddClientSetting("DISPLAY GRADE", "SHARPEN",
                "FidelityFX RCAS sharpening in the final blit. Needs COLOUR GRADE on.",
                context.Settings.Effects.SharpenEnabled);
        }
    }
}
