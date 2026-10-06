using System;
using BoscaliSummer.Modules.Hud.Patches;
using BoscaliSummer.Modules.Hud.Presentation;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;

namespace BoscaliSummer.Modules.Hud
{
    /// <summary>The shared cockpit status item and the Wingview third-person camera/HUD: module
    /// status lines and notices drawn as a small panel parented directly under the native weapons
    /// panel; a screen-fixed flight cluster and target card in third person; and one Harmony
    /// postfix that adjusts the resting pose of the vanilla orbit camera
    /// (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md). The status panel
    /// itself is native's own child and never touches a native object; the other native
    /// visibility changes are <c>Runtime.ExternalHudEnabler</c> (forcing the vanilla HUD open in
    /// orbit/chase), <c>Runtime.NativeFlightNumberHider</c> (hiding specific leaf graphics, third
    /// person only) and <c>Runtime.ThirdPersonHudCenter</c> (levelling <c>FlightHud.HUDCenter</c>
    /// and the velocity vector, third person only).</summary>
    internal sealed class HudModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("hud", "HUD and interface presentation");
        private static readonly Type[] Patches = { typeof(WingviewCameraPatch) };

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            HudBoard board = context.AddSceneService<HudBoard>(80);
            board.Configure(context.Settings.Hud, context.Logger);
            context.AddService<IHudBoard>(board);
            context.Logger.LogInfo("Hud: TargetCamera=" + BoscaliSummer.Core.Game.NativeCamera.Available);
        }
    }
}
