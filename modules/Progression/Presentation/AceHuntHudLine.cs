using BoscaliSummer.Features.Progression.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;

namespace BoscaliSummer.Features.Progression.Presentation
{
    /// <summary>
    /// The active ace hunt as one cockpit line: the ace, its tier, how much of the wing is
    /// still up and its own status word. A read-only view of <see cref="ISquadView"/>; the SQD
    /// dossier stays the full reading, and no reward or hunt state is touched here.
    /// </summary>
    internal sealed class AceHuntHudLine : HudLineWidget
    {
        private const string WidgetOwner = "progression-ace-hunt";

        private ISquadView squad;

        private string text;
        private string detail;
        private float bar;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "ace-hunt";
        protected override string ChannelLabel => "Ace hunt";

        internal void Configure(ISquadView view)
        {
            squad = view;
            DeclareFeed();
        }

        protected override bool WantsLine()
        {
            text = null;
            if (squad == null || !squad.HuntActive || squad.ActiveEnemyWingIndex < 0 ||
                squad.ActiveEnemyWingIndex >= squad.EnemyWingCount) return false;

            EnemyWingView wing = squad.GetEnemyWing(squad.ActiveEnemyWingIndex);
            text = AceHuntHudCopy.Text(wing.AceName, wing.WingName);
            detail = AceHuntHudCopy.Detail(wing.Tier, wing.MembersAlive, wing.MemberCount, wing.Status);
            bar = AceHuntHudCopy.Bar(wing.MembersAlive, wing.MemberCount);
            return true;
        }

        protected override void Write(IHudLine line) => line.Set(HudTone.Caution, text, detail, bar);
    }
}
