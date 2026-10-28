using System;
using BoscaliSummer.Modules.Autopilot.Configuration;
using BoscaliSummer.Modules.Autopilot.Patches;
using BoscaliSummer.Modules.Autopilot.Runtime;
using BoscaliSummer.Core.Modules;

namespace BoscaliSummer.Modules.Autopilot
{
    internal sealed class AutopilotModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("autopilot", "Autopilot");
        private static readonly Type[] Patches =
        {
            typeof(AutopilotLandInputPatch),
            typeof(FlightAssistReportPatch),
            typeof(RadialMenuLifecyclePatches),
            typeof(ChaffNextPatch),
            typeof(ChaffTriggerPatch),
            typeof(ChaffActivePatch),
            typeof(FlareLookPatch),
            typeof(CinematicViewPatch)
        };

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            AutopilotSettings settings = context.Settings.Autopilot;
            AutopilotLandController controller = context.AddSceneService<AutopilotLandController>(48);
            controller.Configure(settings);
            context.AddSceneService<ApproachScopeFeed>(49).Configure(settings, controller);
            new AutopilotScopeGauge().Register();      // the TAC BOARD's AUTOPILOT gauge while it flies the landing
            context.AddSceneService<Presentation.AceRadialMenuUi>(50).Configure(settings);
            context.AddClientKey("MENU KEY", "The key that opens the interaction menu: hold it and point, or tap it to keep the menu open.",
                settings.AceRadialKey);
            context.AddClientKey("CHAFF KEY", "A key that pops one chaff salvo.", settings.ChaffKey);
            context.AddClientSetting("FLIGHT AIDS", "ILS APPROACH",
                "Gear down near a field: the TAC BOARD shows the APPROACH page (runway funnel, localizer, glideslope).",
                settings.IlsHud);
            context.AddSceneService<LandingLight>(51);
            context.AddSceneService<ChaffDispenser>(51).Configure(settings);
            CinematicViewPatch.Setting = settings.CinematicCamera;
            CockpitStateProbe.Initialise();
            context.Logger.LogInfo("Autopilot land=local ownship native-autopilot takeover; interaction menu on " + (settings.OnRadialBinding.Value ? "the Radial Menu binding" : "its own key only") + ", extra key=" + settings.AceRadialKey.Value);
        }
    }
}
