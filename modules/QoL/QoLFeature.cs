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
            typeof(Patches.ThirdPersonChasePatch)
        };

        public void Install(FeatureContext context)
        {
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
