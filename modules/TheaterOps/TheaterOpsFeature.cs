using System;
using BoscaliSummer.Features.TheaterOps.Networking;
using BoscaliSummer.Features.TheaterOps.Patches;
using BoscaliSummer.Features.TheaterOps.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.TheaterOps
{
    internal sealed class TheaterOpsFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("theater-ops", "Theater priority and AI effort direction");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => new[]
        {
            typeof(MissionPositionAdvancePriorityPatch),
            typeof(MissionPositionDeliveryPriorityPatch)
        };

        public void Install(FeatureContext context)
        {
            TheaterOpsNet network = context.AddComponent<TheaterOpsNet>();

            TheaterPriorityService priority = context.AddSceneService<TheaterPriorityService>(50);
            TheaterLogisticsService logistics = context.AddSceneService<TheaterLogisticsService>(50);
            TheaterEffortMarker marker = context.AddSceneService<TheaterEffortMarker>(50);

            priority.Configure(context.Settings.TheaterOps, network, context.Logger);
            logistics.Configure(context.Settings.TheaterOps, context.Logger);
            marker.Configure(context.Settings.TheaterOps, priority);
            network.Configure(priority);

            context.AddService<ITheaterPriorityView>(priority);
            context.AddService<ITheaterLogisticsView>(logistics);
        }
    }
}
