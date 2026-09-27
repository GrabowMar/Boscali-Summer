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
            typeof(GroundFrontDepotPatch),
            typeof(NavalFrontChooseTargetPatch)
        };

        public void Install(FeatureContext context)
        {
            TheaterOpsNet network = context.AddComponent<TheaterOpsNet>();
            LivingFrontNet livingNetwork = context.AddComponent<LivingFrontNet>();

            TheaterPriorityService priority = context.AddSceneService<TheaterPriorityService>(50);
            TheaterLogisticsService logistics = context.AddSceneService<TheaterLogisticsService>(50);
            NavalFrontService naval = context.AddSceneService<NavalFrontService>(50);
            LivingFrontService living = context.AddSceneService<LivingFrontService>(50);
            GroundFrontService groundFront = context.AddSceneService<GroundFrontService>(50);
            Presentation.TheaterOpsHudLine hudLine = context.AddSceneService<Presentation.TheaterOpsHudLine>(51);

            priority.Configure(context.Settings.TheaterOps, network, context.Logger);
            logistics.Configure(context.Settings.TheaterOps, context.Logger);
            naval.Configure(context.Settings.TheaterOps);
            living.Configure(context.Settings.TheaterOps, priority, logistics, livingNetwork, naval, context.Logger);
            livingNetwork.Configure(living);
            groundFront.Configure(context.Settings.TheaterOps, priority, null, null, context.Logger);
            network.Configure(priority, null, null);
            hudLine.Configure(living);

            context.AddService<ITheaterPriorityView>(priority);
            context.AddService<ITheaterLogisticsView>(logistics);
            context.AddService<ITheaterWarView>(living);
            context.AddService<ITheaterAirStationView>(living);
        }
    }
}
