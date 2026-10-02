using System;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Visuals
{
    // Retired effect pack. Kept as an inert module boundary while the local
    // renderer-injection experiment remains in this folder.
    internal sealed class VisualsModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("visuals", "Visuals");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Type.EmptyTypes;
        public void Install(ModuleContext context) { }
    }
}
