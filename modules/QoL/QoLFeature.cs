using BoscaliSummer.Runtime;
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
            typeof(Patches.NightVisionChoicePatch),
            typeof(Patches.WeaponAimAssistPatch)
        };

        public void Install(FeatureContext context)
        {
            ObservationManager observations = context.AddSceneService<ObservationManager>(54);
            observations.Configure(context.Settings.QoL);
            context.AddService<IObservationSource>(observations);
            context.AddSceneService<FuelHudLine>(55);
            context.Logger.LogInfo("CameraObservation=" + NativeCamera.Available);
        }
    }
}
