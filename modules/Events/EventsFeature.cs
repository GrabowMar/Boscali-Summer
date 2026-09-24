using System;
using BoscaliSummer.Features.Events.Networking;
using BoscaliSummer.Features.Events.Presentation;
using BoscaliSummer.Features.Events.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Events
{
    /// <summary>
    /// Rotating mission-wide world events on their own EVN bezel screen. Installs
    /// independently like HighCommand and owns no Harmony patches.
    /// </summary>
    internal sealed class EventsFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("events", "World events and their modifiers");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            EventsNet network = context.AddComponent<EventsNet>();
            EventsManager manager = context.AddSceneService<EventsManager>(62);
            manager.Configure(context.Settings.Events, network, context.Logger);
            network.Configure(manager);
            context.AddService<IActiveEventsView>(manager);

            EventsMfdPanel panel = context.AddSceneService<EventsMfdPanel>(63);
            panel.Configure(context.Settings.Events, manager, context.Logger);

            SuperEventAlert alert = context.AddSceneService<SuperEventAlert>(67);
            alert.Configure(context.Settings.Events, manager, context.Logger);

            // How hard events bite, and whether the scripted ones fire at all. The calm
            // period between them is rotation pacing, set once in the config file.
            context.AddHostSettings(new HostSettingsTable("WORLD EVENTS")
                .Toggle(1, context.Settings.Events.SuperEventsEnabled, "SUPEREVENTS",
                    "Allow scripted, faction-targeted superevents when the theater leans.")
                .Number(2, context.Settings.Events.EffectStrength, "EFFECT STRENGTH",
                    "Scales every event modifier without editing the catalog. 0 makes events flavour only.",
                    0.05f, v => v.ToString("P0")));
        }
    }
}
