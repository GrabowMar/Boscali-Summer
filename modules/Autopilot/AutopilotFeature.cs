using System;
using BoscaliSummer.Features.Autopilot.Configuration;
using BoscaliSummer.Features.Autopilot.Patches;
using BoscaliSummer.Features.Autopilot.Runtime;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Autopilot
{
    internal sealed class AutopilotFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("autopilot", "Autopilot");
        private static readonly Type[] Patches =
        {
            typeof(AutopilotLandInputPatch),
            typeof(FlightAssistReportPatch),
            typeof(RadialMenuLifecyclePatches),
            typeof(BoscaliMenuActionPatches)
        };

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            AutopilotSettings settings = context.Settings.Autopilot;
            AutopilotLandController controller = context.AddSceneService<AutopilotLandController>(48);
            controller.Configure(settings);
            context.AddSceneService<Presentation.AutopilotHudLine>(49).Configure(controller);
            context.AddSceneService<Presentation.IlsHudLine>(49).Configure(settings);
            RadialMenuAccess.Initialise();
            context.Logger.LogInfo("Autopilot land=local ownship native-autopilot takeover; radial=" +
                (RadialMenuAccess.Available ? "native wheel entry" : "unavailable"));
        }
    }
}
