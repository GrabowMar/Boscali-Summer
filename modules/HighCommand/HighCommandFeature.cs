using System;
using BoscaliSummer.Features.HighCommand.Configuration;
using BoscaliSummer.Features.HighCommand.Networking;
using BoscaliSummer.Features.HighCommand.Patches;
using BoscaliSummer.Features.HighCommand.Presentation;
using BoscaliSummer.Features.HighCommand.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.HighCommand
{
    internal sealed class HighCommandFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("high-command", "Chain of command and VIP staff");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => new[]
        {
            typeof(HighCommandDamagePatch)
        };

        public void Install(FeatureContext context)
        {
            HighCommandNet network = context.AddComponent<HighCommandNet>();
            HighCommandManager manager = context.AddSceneService<HighCommandManager>(46);
            manager.Configure(context.Settings.HighCommand, network, context.Logger);
            network.Configure(manager);
            context.AddService<IHighCommandView>(manager);

            CommandPostMarkers markers = context.AddSceneService<CommandPostMarkers>(46);
            markers.Configure(context.Settings.HighCommand, manager);

            HighCommandSettings settings = context.Settings.HighCommand;
            // The three things a host decides about high command while a mission runs:
            // whether it pays, whether commanders travel far enough to be intercepted, and
            // whether the posts are drawn. Payment sizes and the clocks around a killed
            // commander are balance, and stay in the config file.
            context.AddHostSettings(new HostSettingsTable("HIGH COMMAND")
                .Toggle(1, settings.EconomyEnabled, "STIPENDS AND KILL PAY",
                    "Pay the staff's survival income and the cash for killing an enemy commander through the vanilla faction account. Off keeps the page informational.")
                .Toggle(2, settings.TransfersEnabled, "VIP CONVOYS",
                    "Commanders occasionally travel between friendly bases in ground convoys. Intercepting the lead vehicle kills them.")
                .Toggle(3, settings.MapMarkersEnabled, "MAP MARKERS",
                    "Draw your posts and confirmed enemy posts as diamonds on the map. Off hides the layer only."));
        }
    }
}
