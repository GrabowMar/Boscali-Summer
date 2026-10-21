using System;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Pmc.Patches;
using BoscaliSummer.Modules.Pmc.Runtime;

namespace BoscaliSummer.Modules.Pmc
{
    /// <summary>
    /// Two extra sides, PMC and Pirates, and peace between sides. Always installed and idle: a side exists only in a mission whose faction list names
    /// it (a mission script brings it; the editor offers PMC), and peace only in a script that writes it. Single-player and host only for now.
    /// </summary>
    internal sealed class PmcModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("pmc", "Extra sides (PMC, Pirates) and peace for scripted missions");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => PmcPatches.All;

        public void Install(ModuleContext context)
        {
            ExtraSides.Install(context.Settings.Pmc, context.Logger);
            context.OnTeardown(ExtraSides.Uninstall);
        }
    }
}
