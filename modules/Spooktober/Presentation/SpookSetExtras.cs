using System;
using System.Runtime.CompilerServices;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Spooktober.Presentation
{
    /// <summary>The SPOOKTOBER tab on MISC. Command resolves it through <see cref="ISetExtras"/>; with this module gone MISC has no such tab.</summary>
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
        public string ExtraTabTitle => "MISC · SPOOKTOBER";

        public void BuildExtraPage(int page, AvFlow flow, AvConsole console) =>
            SpookMenuPage.Build(page, flow, console, consoles.GetValue(console, c => new SpookConsole(c)), kaijuAvailable);
    }
}
