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

            context.AddHostSettings(new HostSettingsTable("WORLD EVENTS")
                .Number(1, context.Settings.Events.EffectStrength, "EFFECT STRENGTH",
                    "Scales every event modifier without editing the catalog. 0 makes events flavour only.",
                    0.05f, v => v.ToString("P0"))
                .Number(2, context.Settings.Events.RotationGapMinSeconds, "MIN CALM",
                    "Shortest calm period between events.", 10, v => v.ToString("0") + " s")
                .Number(3, context.Settings.Events.RotationGapMaxSeconds, "MAX CALM",
                    "Longest calm period between events.", 10, v => v.ToString("0") + " s")
                .Toggle(4, context.Settings.Events.SuperEventsEnabled, "SUPEREVENTS",
                    "Allow scripted, faction-targeted superevents when the theater leans."));
        }
    }
}
