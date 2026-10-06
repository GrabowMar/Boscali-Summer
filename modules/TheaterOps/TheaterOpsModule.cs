using System;
using BoscaliSummer.Modules.TheaterOps.Networking;
using BoscaliSummer.Modules.TheaterOps.Patches;
using BoscaliSummer.Modules.TheaterOps.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;

namespace BoscaliSummer.Modules.TheaterOps
{
    internal sealed class TheaterOpsModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("theater-ops", "Theater priority and AI effort direction");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => new[]
        {
            typeof(MissionPositionAdvancePriorityPatch),
            typeof(MissionPositionDeliveryPriorityPatch),
            typeof(GroundFrontDepotPatch),
            typeof(NavalFrontChooseTargetPatch)
        };

        public void Install(ModuleContext context)
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
            context.AddService<IEnemyIntentSource>(priority);
            context.AddService<ITheaterLogisticsView>(logistics);
            context.AddService<ITheaterWarView>(living);
            context.AddService<ITheaterAirStationView>(living);
        }
    }
}
