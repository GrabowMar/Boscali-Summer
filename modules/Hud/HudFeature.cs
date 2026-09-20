using System;
using BoscaliSummer.Features.Hud.Presentation;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Hud
{
    /// <summary>
    /// The common cockpit HUD element: one vanilla-styled stack of pilot-facing lines that every
    /// Boscali presentation feature draws through, instead of each one owning its own canvas and
    /// its own copy of the same typography, palette, sorting order and visibility rules.
    ///
    /// <para>Presentation only. This module reads no game state it does not draw, writes nothing
    /// to the world, sends nothing, and patches nothing; turning it off hides lines and changes
    /// no feature's behaviour.</para>
    /// </summary>
    internal sealed class HudFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("hud", "Common cockpit HUD element");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            // Highest reset order in the build: the element is torn down after every feature
            // that can hold a line on it, so no consumer resets against a dead board.
            HudBoard board = context.AddSceneService<HudBoard>(80);
            board.Configure(context.Settings.Hud, context.Logger);
            context.AddService<IHudBoard>(board);
        }
    }
}
