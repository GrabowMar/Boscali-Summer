using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// WING page (prototype: <c>pages/wing.js</c>). STUB: never live, so it never shows.
    /// Owner: tb-flight. Feed: WingScope (a Wing provider; host and client mirror).
    /// </summary>
    internal sealed class WingPage : ScopePageBase
    {
        public override ScopePage Id => ScopePage.Wing;

        public override string Title => "WING";

        public override char Code => 'W';

        public override bool Poll(in ScopeEnv env) => false;

        public override void Paint(ScopeCanvas g, in PagePaint p) { }
    }
}
