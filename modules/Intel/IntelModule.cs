using System;
using BoscaliSummer.Modules.Intel.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;

namespace BoscaliSummer.Modules.Intel
{
    /// <summary>
    /// Each faction's fog-of-war threat picture. No hard dependencies, no Harmony patches and
    /// no wire: it reads each faction's own vanilla tracking and publishes IThreatPicture.
    /// </summary>
    internal sealed class IntelModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("intel", "Fog-of-war threat picture");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(ModuleContext context)
        {
            // Resets before TheaterOps (50), whose director reads the picture.
            ThreatPictureService picture = context.AddSceneService<ThreatPictureService>(49);
            picture.Configure(context.Settings.Intel, context.Logger);
            context.AddService<IThreatPicture>(picture);
            context.Logger.LogInfo("Intel: threat picture installed (PreWarIntel=" +
                                   context.Settings.Intel.PreWarIntel.Value + ").");
        }
    }
}
