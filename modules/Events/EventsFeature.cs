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
        }
    }
}
