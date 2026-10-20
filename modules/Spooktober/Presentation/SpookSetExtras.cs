using System;
using System.Runtime.CompilerServices;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Spooktober.Runtime;
using NOAvionics;

namespace BoscaliSummer.Modules.Spooktober.Presentation
{
    /// <summary>
    /// What Spooktober adds to SET's PILOT console: the four puzzles disguised as settings on pages 0 to 4 and, once all four are solved,
    /// the SPOOKTOBER tab. Command resolves it through <see cref="ISetExtras"/>; with this module gone SET is exactly as it was.
    /// </summary>
    internal sealed class SpookSetExtras : ISetExtras
    {
        private readonly Func<bool> kaijuAvailable;
        // Weak: a console destroyed with its scene takes its state with it.
        private readonly ConditionalWeakTable<AvConsole, SpookConsole> consoles = new ConditionalWeakTable<AvConsole, SpookConsole>();

        /// <param name="kaijuAvailable">Whether the kaiju can be summoned (its module is on); the first card says so when it cannot.</param>
        public SpookSetExtras(Func<bool> kaijuAvailable)
        {
            this.kaijuAvailable = kaijuAvailable ?? (() => true);
        }

        public string ExtraTabLabel => "SPOOKTOBER";
        public string ExtraTabTitle => "PILOT \u00B7 SPOOKTOBER";
        public bool ExtraTabShown => SpookStore.Current.Unlocked;

        public void BuildPilotPage(int page, AvFlow flow, AvConsole console) =>
            SpookPuzzleViews.Build(page, flow, For(console));

        public void BuildExtraPage(int page, AvFlow flow, AvConsole console) =>
            SpookMenuPage.Build(page, flow, For(console), kaijuAvailable);

        private SpookConsole For(AvConsole console) => consoles.GetValue(console, c => new SpookConsole(c));
    }
}
