using System;
using BoscaliSummer.Features.Trenches.Presentation;
using BoscaliSummer.Features.Trenches.Runtime;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Trenches
{
    internal sealed class TrenchesFeature : IModFeature
    {
        public FeatureMetadata Metadata { get; } = new FeatureMetadata("trenches", "Natural front-line trench curves", "command");
        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            TrenchManager manager = context.AddSceneService<TrenchManager>(60);
            manager.Configure(context.Settings.Trenches, context.Logger, context.Services.GetRequired<ITerritoryIngress>());

            TrenchMapOverlay overlay = context.AddSceneService<TrenchMapOverlay>(61);
            overlay.Configure(context.Settings.Trenches, manager, context.Logger);

            context.AddHostSettings(new HostSettingsTable("TRENCHES")
                .Number(1, context.Settings.Trenches.GrowthIntervalSeconds, "GROWTH INTERVAL",
                    "Seconds between autonomous trench growth and fortification ticks.",
                    5f, v => v.ToString("0") + " s")
                .Number(2, context.Settings.Trenches.MaxTrenchPositions, "MAX NETWORKS",
                    "Maximum concurrent trench positions per theater.", 1));

            context.Logger.LogInfo("[Trenches] Combat fortifications ready: Command's ordered front traces become natural Bezier trench curves on the owning side, settled into the flattest low ground; each ~2.4km position is a field-scale earthwork (parapet, spoil, wire belt) that matures from a scrape through fire trench, support line and reserve redoubt to forward saps, defended by four native MG/ATGM/MANPADS and runtime-filtered infantry works. Water, cliffs and broken ground split a line instead of cancelling it, and all three LODs keep the earthwork silhouette out to cruise altitude. Damage suppresses growth and defenders never respawn. Native defenders and works replicate; carved ditches and map marks remain host-local.");
        }
    }
}
