using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// STRIKE page (prototype: <c>pages/strike.js</c>). STUB: never live, so it never shows.
    /// Owner: tb-ops. Feed: StrikeScope (a Support provider).
    /// </summary>
    internal sealed class StrikePage : ScopePageBase
    {
        public override ScopePage Id => ScopePage.Strike;

        public override string Title => "STRIKE";

        public override char Code => 'K';

        public override bool Poll(in ScopeEnv env) => false;

        public override void Paint(ScopeCanvas g, in PagePaint p) { }
    }
}
