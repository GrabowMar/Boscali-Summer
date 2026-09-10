using System;
using BoscaliSummer.Features.QoL.Runtime;
using BoscaliSummer.Features.QoL.Presentation;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.QoL
{
    internal sealed class QoLFeature : IModFeature
    {
        public FeatureMetadata Metadata => new FeatureMetadata("qol", "Quality of life");
        public Type[] PatchTypes => new[]
        {
            typeof(Patches.ThirdPersonHudPatches),
            typeof(Patches.ThirdPersonOrbitPatch),
            typeof(Patches.ThirdPersonChasePatch),
            typeof(Patches.GunAimSolutionPatch),
            typeof(Patches.GunAimInputPatch)
        };

        public void Install(FeatureContext context)
        {
            context.AddSceneService<GunAimAssist>(53).Configure(context.Settings.QoL);
            context.Logger.LogInfo("GunAimAssist=native HUD solution / local player input; enabled=" + context.Settings.QoL.GunAimAssist.Value);
            ThirdPersonHudController hud = context.AddSceneService<ThirdPersonHudController>(52);
            hud.Configure(context.Settings.QoL);
            context.AddService<IThirdPersonHud>(hud);
            ObservationManager observations = context.AddSceneService<ObservationManager>(54);
            observations.Configure(context.Settings.QoL);
            context.AddService<IObservationSource>(observations);
            context.Logger.LogInfo("CameraObservation=" + NativeCamera.Available);
            context.Logger.LogInfo("ThirdPersonCameraFeed=" + ThirdPersonCameraPanel.Available);
        }
    }
}
