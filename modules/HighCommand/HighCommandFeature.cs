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
            context.AddHostSettings(new HostSettingsTable("HIGH COMMAND")
                .Toggle(1, settings.EconomyEnabled, "STIPENDS AND KILL PAY",
                    "Pay the staff's survival income and the cash for killing an enemy commander through the vanilla faction account. Off keeps the page informational.")
                .Number(2, settings.StipendIntervalSeconds, "STIPEND INTERVAL",
                    "Seconds between survival stipend payments. 0 disables the stipend.",
                    15, v => v.ToString("0") + " s")
                .Number(3, settings.MaximumStipends, "MAXIMUM STIPENDS",
                    "Stipends one faction can collect in a single mission.", 1)
                .Toggle(4, settings.TransfersEnabled, "VIP CONVOYS",
                    "Commanders occasionally travel between friendly bases in ground convoys. Intercepting the lead vehicle kills them.")
                .Number(5, settings.DisruptionSeconds, "DISRUPTION",
                    "How long a successor runs the post at reduced effectiveness after a commander dies.",
                    10, v => v.ToString("0") + " s")
                .Number(6, settings.PostRespawnSeconds, "POST RESPAWN",
                    "Delay before a killed commander's post is re-established.",
                    10, v => v.ToString("0") + " s")
                .Toggle(7, settings.MapMarkersEnabled, "MAP MARKERS",
                    "Draw your posts and confirmed enemy posts as diamonds on the map. Off hides the layer only."));
        }
    }
}
