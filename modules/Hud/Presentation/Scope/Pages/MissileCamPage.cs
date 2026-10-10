using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// MISSILE CAM page (prototype: <c>pages/missilecam.js</c>). STUB: never live, so it never shows.
    /// Owner: tb-combat. Live while the orbit camera rides an own missile (MissileCam.IsOwnMissileView).
    /// </summary>
    internal sealed class MissileCamPage : ScopePageBase
    {
        public override ScopePage Id => ScopePage.MissileCam;

        public override string Title => "MISSILE CAM";

        public override char Code => 'M';

        public override bool Poll(in ScopeEnv env) => false;

        public override void Paint(ScopeCanvas g, in PagePaint p) { }
    }
}
