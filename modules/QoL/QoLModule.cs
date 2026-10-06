using BoscaliSummer.Core.Game;
using System;
using BoscaliSummer.Modules.QoL.Runtime;
using BoscaliSummer.Modules.QoL.Presentation;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;

namespace BoscaliSummer.Modules.QoL
{
    internal sealed class QoLModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("qol", "Quality of life");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => new[]
        {
            typeof(Patches.NightVisionChoicePatch),
            typeof(Patches.WeaponAimAssistPatch)
        };

        public void Install(ModuleContext context)
        {
            ObservationManager observations = context.AddSceneService<ObservationManager>(54);
            observations.Configure(context.Settings.QoL);
            context.AddService<IObservationSource>(observations);
            context.AddSceneService<FuelHudLine>(55);
            context.Logger.LogInfo("CameraObservation=" + NativeCamera.Available);
        }
    }
}
