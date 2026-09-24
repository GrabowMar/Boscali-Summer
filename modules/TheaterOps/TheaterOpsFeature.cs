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
            typeof(MissionPositionDeliveryPriorityPatch),
            typeof(GroundFrontDepotPatch)
        };

        public void Install(FeatureContext context)
        {
            TheaterOpsNet network = context.AddComponent<TheaterOpsNet>();

            TheaterPriorityService priority = context.AddSceneService<TheaterPriorityService>(50);
            TheaterLogisticsService logistics = context.AddSceneService<TheaterLogisticsService>(50);
            TheaterOperationsService operations = context.AddSceneService<TheaterOperationsService>(50);
            TheaterDirectorService director = context.AddSceneService<TheaterDirectorService>(50);
            GroundFrontService groundFront = context.AddSceneService<GroundFrontService>(50);
            TheaterEffortMarker marker = context.AddSceneService<TheaterEffortMarker>(50);
            Presentation.TheaterOpsHudLine hudLine = context.AddSceneService<Presentation.TheaterOpsHudLine>(51);

            priority.Configure(context.Settings.TheaterOps, network, context.Logger);
            logistics.Configure(context.Settings.TheaterOps, context.Logger);
            director.Configure(context.Settings.TheaterOps, priority, logistics, operations, network, context.Logger);
            operations.Configure(context.Settings.TheaterOps, network, priority, director, context.Logger);
            groundFront.Configure(context.Settings.TheaterOps, priority, operations, director, context.Logger);
            marker.Configure(context.Settings.TheaterOps, priority);
            network.Configure(priority, operations, director);
            hudLine.Configure(priority, operations);

            context.AddService<ITheaterPriorityView>(priority);
            context.AddService<ITheaterLogisticsView>(logistics);
            context.AddService<ITheaterOperationsView>(operations);
        }
    }
}
