using System;
using BoscaliSummer.Modules.Autopilot.Configuration;
using BoscaliSummer.Modules.Autopilot.Patches;
using BoscaliSummer.Modules.Autopilot.Runtime;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;

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
            typeof(BoscaliMenuActionPatches)
        };

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            AutopilotSettings settings = context.Settings.Autopilot;
            AutopilotLandController controller = context.AddSceneService<AutopilotLandController>(48);
            controller.Configure(settings);
            context.AddSceneService<Presentation.AutopilotHudLine>(49).Configure(controller);
            context.AddSceneService<Presentation.IlsHudLine>(49).Configure(settings);
            context.AddSceneService<Presentation.AceRadialMenuUi>(50).Configure(settings);
            context.AddSceneService<LandingLight>(51);
            CockpitStateProbe.Initialise();
            RadialMenuAccess.Initialise();
            context.Logger.LogInfo("Autopilot land=local ownship native-autopilot takeover; radial=" +
                (RadialMenuAccess.Available ? "native wheel entry" : "native wheel unavailable") +
                "; interaction menu key=" + settings.AceRadialKey.Value);
        }
    }
}
