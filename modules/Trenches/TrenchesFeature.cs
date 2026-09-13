using System;
using BoscaliSummer.Features.Trenches.Presentation;
using BoscaliSummer.Features.Trenches.Runtime;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Trenches
{
    internal sealed class TrenchesFeature : IModFeature
    {
        public FeatureMetadata Metadata { get; } = new FeatureMetadata("trenches", "Dynamic modular trench system", "command");
        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            TrenchManager manager = context.AddSceneService<TrenchManager>(60);
            manager.Configure(context.Settings.Trenches, context.Logger, context.Services.GetRequired<ITerritoryIngress>());

            TrenchMapOverlay overlay = context.AddSceneService<TrenchMapOverlay>(61);
            overlay.Configure(context.Settings.Trenches, manager, context.Logger);

            context.Logger.LogInfo("[Trenches] Combat fortifications ready: connected lines, native MG/AT/AA defenders (6/site, 96 total), damage suppresses growth, no defender respawns. Native defenders replicate; procedural earthworks/map remain host-local.");
        }
    }
}
