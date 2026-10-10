using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// OBJECTIVE page (prototype: <c>pages/objective.js</c>). STUB: never live, so it never shows.
    /// Owner: tb-ops. Feed: ObjectiveScope over ISecondaryObjectivesView.
    /// </summary>
    internal sealed class ObjectivePage : ScopePageBase
    {
        public override ScopePage Id => ScopePage.Objective;

        public override string Title => "OBJECTIVE";

        public override char Code => 'O';

        public override bool Poll(in ScopeEnv env) => false;

        public override void Paint(ScopeCanvas g, in PagePaint p) { }
    }
}
