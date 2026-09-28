using System;
using BoscaliSummer.Features.Hud.Patches;
using BoscaliSummer.Features.Hud.Presentation;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Hud
{
    /// <summary>The shared cockpit status item and the Wingview third-person camera/HUD: feature
    /// status lines and notices drawn as a small panel parented directly under the native weapons
    /// panel; a screen-fixed flight cluster and target card in third person; and one Harmony
    /// postfix that adjusts the resting pose of the vanilla orbit camera
    /// (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md). The status panel
    /// itself is native's own child and never touches a native object; the other native
    /// visibility changes are <c>Runtime.ExternalHudEnabler</c> (forcing the vanilla HUD open in
    /// orbit/chase), <c>Runtime.NativeFlightNumberHider</c> (hiding specific leaf graphics, third
    /// person only) and <c>Runtime.ThirdPersonHudCenter</c> (levelling <c>FlightHud.HUDCenter</c>
    /// and the velocity vector, third person only).</summary>
    internal sealed class HudFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("hud", "HUD and interface presentation");
        private static readonly Type[] Patches = { typeof(WingviewCameraPatch) };

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            HudBoard board = context.AddSceneService<HudBoard>(80);
            board.Configure(context.Settings.Hud, context.Logger);
            context.AddService<IHudBoard>(board);
            context.Logger.LogInfo("Hud: TargetCamera=" + BoscaliSummer.Runtime.NativeCamera.Available);
        }
    }
}
