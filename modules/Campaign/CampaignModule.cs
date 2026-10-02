using System;
using BoscaliSummer.Modules.Campaign.Runtime;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Campaign
{
    /// <summary>
    /// Ships the authored Boscali Summer campaign mission into the game's user mission list.
    /// No Harmony patches and no scene service: the install is one bounded file write at
    /// startup, and a failure degrades to a log line instead of blocking the module.
    /// </summary>
    internal sealed class CampaignModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("campaign", "Boscali Summer campaign mission");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(ModuleContext context)
        {
            CampaignMissionInstaller.Install(context.Logger);
        }
    }
}
