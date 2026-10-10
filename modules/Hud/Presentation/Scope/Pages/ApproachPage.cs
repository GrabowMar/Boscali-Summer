using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// APPROACH page (prototype: <c>pages/approach.js</c>). STUB: never live, so it never shows.
    /// Owner: tb-flight. Feed: ApproachScope (an Autopilot provider).
    /// </summary>
    internal sealed class ApproachPage : ScopePageBase
    {
        public override ScopePage Id => ScopePage.Approach;

        public override string Title => "APPROACH";

        public override char Code => 'A';

        public override bool Poll(in ScopeEnv env) => false;

        public override void Paint(ScopeCanvas g, in PagePaint p) { }
    }
}
