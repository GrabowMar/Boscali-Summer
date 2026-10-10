using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// SHOT page (prototype: <c>pages/shot.js</c>). STUB: never live, so it never shows.
    /// Owner: tb-combat. Feed: ShotScope (a Hud provider over MissileTracker and the ShotLedger).
    /// </summary>
    internal sealed class ShotPage : ScopePageBase
    {
        public override ScopePage Id => ScopePage.Shot;

        public override string Title => "SHOT";

        public override char Code => 'S';

        public override bool Poll(in ScopeEnv env) => false;

        public override void Paint(ScopeCanvas g, in PagePaint p) { }
    }
}
