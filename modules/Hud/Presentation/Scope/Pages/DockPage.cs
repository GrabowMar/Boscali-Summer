using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// DOCK page (prototype: <c>pages/dock.js</c>). STUB: never live, so it never shows.
    /// Owner: tb-flight. Feed: DockScope (a Vanguard provider over SkywellBoard).
    /// </summary>
    internal sealed class DockPage : ScopePageBase
    {
        public override ScopePage Id => ScopePage.Dock;

        public override string Title => "DOCK";

        public override char Code => 'D';

        public override bool Poll(in ScopeEnv env) => false;

        public override void Paint(ScopeCanvas g, in PagePaint p) { }
    }
}
