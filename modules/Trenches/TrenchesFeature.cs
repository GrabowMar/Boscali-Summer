using System;
using BoscaliSummer.Features.Trenches.Presentation;
using BoscaliSummer.Features.Trenches.Runtime;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Trenches
{
    internal sealed class TrenchesFeature : IModFeature
    {
        public FeatureMetadata Metadata { get; } = new FeatureMetadata("trenches", "Dynamic modular trench system");
        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            TrenchManager manager = context.AddSceneService<TrenchManager>(60);
            manager.Configure(context.Settings.Trenches, context.Logger);

            TrenchMapOverlay overlay = context.AddSceneService<TrenchMapOverlay>(61);
            overlay.Configure(context.Settings.Trenches, manager, context.Logger);

            context.Logger.LogInfo("[Trenches] Dynamic modular trench system initialized; autonomous growth, NATO map symbology, and 3-tier flight LOD active.");
        }
    }
}
