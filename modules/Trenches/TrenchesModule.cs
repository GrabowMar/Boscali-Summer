using System;
using BoscaliSummer.Modules.Trenches.Networking;
using BoscaliSummer.Modules.Trenches.Presentation;
using BoscaliSummer.Modules.Trenches.Runtime;
using BoscaliSummer.Modules.Trenches.Visuals;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Trenches
{
    internal sealed class TrenchesModule : IModule
    {
        private static readonly Type[] Patches =
        {
            typeof(TrenchNestClientPatch),
            typeof(TrenchNestServerPatch),
            typeof(TrenchWorksDetectionPatch)
        };

        public ModuleMetadata Metadata { get; } = new ModuleMetadata("trenches", "Natural front-line trench curves", "command");
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            TrenchManager manager = context.AddSceneService<TrenchManager>(60);
            manager.Configure(context.Settings.Trenches, context.Logger, context.Services.GetRequired<ITerritoryIngress>());
            context.AddService<IFieldworksReadiness>(manager);

            TrenchMapOverlay overlay = context.AddSceneService<TrenchMapOverlay>(61);
            overlay.Configure(context.Settings.Trenches, manager, context.Logger);

            TrenchNet net = context.AddSceneService<TrenchNet>(62);
            net.Configure(manager, context.Logger);

            // These bounded host controls limit future trench creation and growth work.
            context.AddHostSettings(new HostSettingsTable("TRENCHES")
                .Number(1, context.Settings.Trenches.MaxTrenchPositions, "MAX NETWORKS",
                    "Maximum concurrent trench positions per theater. Lower values limit future positions; existing lines remain.", 1)
                .Number(2, context.Settings.Trenches.GrowthIntervalSeconds, "GROWTH INTERVAL",
                    "Seconds between trench growth passes. Higher values reduce host work and slow fortification.",
                    15f, v => v.ToString("0") + "s")
                .Toggle(3, context.Settings.Trenches.BarrageEnabled, "HARASSING BARRAGE",
                    "Allow matured opposing trench lines to exchange native shells. Off reduces shell activity."));

            context.Logger.LogInfo("[Trenches] Combat fortifications ready: Command's front traces become owned-side trench curves sited on defensible relief, forest edges, clear of roads and off airfields, with beachheads dug one band landward and opposing mirror pairs facing each other across no-man's-land. Hot sectors dig fast and stand heavy, quiet ones mature slowly behind an MG screen. Each position matures from scrape through fire trench, support line and redoubt to forward saps, with up to eight native MG/ATGM/MANPADS/23mm defenders (the ATGM team watches the nearest road) and infantry works. When enabled, matured pairs trade harassing mortar and artillery fire through vanilla shells. Damage suppresses growth; losses never respawn. The theater director counts observed fieldworks when sizing attacks. Native defenders, works and shells replicate; ditch curves replicate so clients carve the same meshes, wire and map marks locally.");
        }
    }
}
