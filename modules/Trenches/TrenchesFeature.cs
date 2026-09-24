using System;
using BoscaliSummer.Features.Trenches.Presentation;
using BoscaliSummer.Features.Trenches.Runtime;
using BoscaliSummer.Features.Trenches.Visuals;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Trenches
{
    internal sealed class TrenchesFeature : IModFeature
    {
        private static readonly Type[] Patches =
        {
            typeof(TrenchNestClientPatch),
            typeof(TrenchNestServerPatch),
            typeof(TrenchWorksDetectionPatch)
        };

        public FeatureMetadata Metadata { get; } = new FeatureMetadata("trenches", "Natural front-line trench curves", "command");
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            TrenchManager manager = context.AddSceneService<TrenchManager>(60);
            manager.Configure(context.Settings.Trenches, context.Logger, context.Services.GetRequired<ITerritoryIngress>());
            context.AddService<IFieldworksReadiness>(manager);

            TrenchMapOverlay overlay = context.AddSceneService<TrenchMapOverlay>(61);
            overlay.Configure(context.Settings.Trenches, manager, context.Logger);

            // How much of the theater ends up dug in - the one trench decision with a cost
            // a host can feel. How fast a position matures is pacing, and stays in the
            // config file.
            context.AddHostSettings(new HostSettingsTable("TRENCHES")
                .Number(1, context.Settings.Trenches.MaxTrenchPositions, "MAX NETWORKS",
                    "Maximum concurrent trench positions per theater.", 1));

            context.Logger.LogInfo("[Trenches] Combat fortifications ready: Command's front traces become owned-side trench curves sited on defensible relief, forest edges and clear of roads, with beachheads dug one band landward. Each position matures from scrape through fire trench, support line and redoubt to forward saps, with up to eight native MG/ATGM/MANPADS/23mm defenders (the ATGM team watches the nearest road) and infantry works. Damage suppresses growth; losses never respawn. The theater director counts observed fieldworks when sizing attacks. Native defenders and works replicate; ditch meshes, wire and map marks remain host-local.");
        }
    }
}
