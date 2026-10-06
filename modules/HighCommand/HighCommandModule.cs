using System;
using BoscaliSummer.Modules.HighCommand.Configuration;
using BoscaliSummer.Modules.HighCommand.Networking;
using BoscaliSummer.Modules.HighCommand.Patches;
using BoscaliSummer.Modules.HighCommand.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.HighCommand
{
    internal sealed class HighCommandModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("high-command", "Chain of command and VIP staff");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => new[]
        {
            typeof(HighCommandDamagePatch)
        };

        public void Install(ModuleContext context)
        {
            HighCommandNet network = context.AddComponent<HighCommandNet>();
            HighCommandManager manager = context.AddSceneService<HighCommandManager>(46);
            manager.Configure(context.Settings.HighCommand, network);
            network.Configure(manager);
            context.AddService<IHighCommandView>(manager);

            HighCommandSettings settings = context.Settings.HighCommand;
            // The three things a host decides about high command while a mission runs:
            // whether it pays and whether commanders travel far enough to be intercepted.
            // Payment sizes and the clocks around a killed
            // commander are balance, and stay in the config file.
            context.AddHostSettings(new HostSettingsTable("HIGH COMMAND")
                .Toggle(1, settings.EconomyEnabled, "STIPENDS AND KILL PAY",
                    "Pay the staff's survival income and the cash for killing an enemy commander through the vanilla faction account. Off keeps the page informational.")
                .Toggle(2, settings.TransfersEnabled, "VIP CONVOYS",
                    "Commanders occasionally travel between friendly bases in ground convoys. Intercepting the lead vehicle kills them."));
        }
    }
}
