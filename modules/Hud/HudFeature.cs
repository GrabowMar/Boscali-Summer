using System;
using BoscaliSummer.Features.Hud.Runtime;
using BoscaliSummer.Features.Hud.Presentation;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Hud
{
    /// <summary>Client-local status, external instruments, HUD patches and camera presentation.</summary>
    internal sealed class HudFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("hud", "HUD and interface presentation");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => new[]
        {
            typeof(Patches.ThirdPersonHudPatches),
            typeof(Patches.ThirdPersonOrbitPatch),
            typeof(Patches.ThirdPersonChasePatch)
        };

        public void Install(FeatureContext context)
        {
            // Highest reset order in the build: the element is torn down after every feature
            // that can hold a line on it, so no consumer resets against a dead board.
            ThirdPersonHudController external = context.AddSceneService<ThirdPersonHudController>(52);
            external.Configure(context.Settings.Hud);
            context.AddService<IThirdPersonHud>(external);
            context.Logger.LogInfo("Hud: TargetCamera=" + BoscaliSummer.Runtime.NativeCamera.Available +
                "; Presentation=independent instruments; NativeDamageArt=false");
            HudBoard board = context.AddSceneService<HudBoard>(80);
            board.Configure(context.Settings.Hud, context.Logger);
            context.AddService<IHudBoard>(board);
        }
    }
}
