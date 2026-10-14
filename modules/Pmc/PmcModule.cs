using System;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Pmc.Patches;
using BoscaliSummer.Modules.Pmc.Runtime;

namespace BoscaliSummer.Modules.Pmc
{
    /// <summary>
    /// A third side, PMC, hostile to BDF and PALA. Always installed and idle: it exists only in a mission whose faction list names it (a mission
    /// script brings it) and in the mission editor. Single-player and host only for now.
    /// </summary>
    internal sealed class PmcModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("pmc", "A third side, PMC, for scripted missions and the editor");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => PmcPatches.All;

        public void Install(ModuleContext context)
        {
            PmcSide.Install(context.Settings.Pmc, context.Logger);
            context.OnTeardown(PmcSide.Uninstall);
        }
    }
}
