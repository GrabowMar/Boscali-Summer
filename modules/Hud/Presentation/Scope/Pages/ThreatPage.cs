using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// THREAT page (prototype: <c>pages/threat.js</c>). STUB: never live, so it never shows.
    /// Owner: tb-combat. Feed: ThreatScope (a Hud provider over ThreatWatch).
    /// </summary>
    internal sealed class ThreatPage : ScopePageBase
    {
        public override ScopePage Id => ScopePage.Threat;

        public override string Title => "THREAT";

        public override char Code => 'T';

        public override bool Poll(in ScopeEnv env) => false;

        public override void Paint(ScopeCanvas g, in PagePaint p) { }
    }
}
