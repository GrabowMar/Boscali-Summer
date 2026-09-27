using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;

namespace BoscaliSummer.Features.TheaterOps.Presentation
{
    /// <summary>
    /// A compact view of the same host-authored operation and staff choices shown on STR.
    /// </summary>
    internal sealed class TheaterOpsHudLine : HudLineWidget
    {
        private const string WidgetOwner = "theater-effort";

        private ITheaterWarView war;

        private string text;
        private string detail;
        private float bar;
        private HudTone tone;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "theater";
        protected override string ChannelLabel => "Theater effort";
        protected override float RefreshSeconds => 1f;

        internal void Configure(ITheaterWarView view)
        {
            war = view;
            DeclareFeed();
        }

        protected override bool WantsLine()
        {
            text = null;
            if (war == null || !war.Available) return false;
            war.Refresh();
            TheaterLiveOperationView operation = war.ActiveOperation;
            if (operation != null)
            {
                text = operation.Kind + " · " + operation.Label;
                detail = operation.Phase + " · " + operation.Summary;
                bar = 0f;
                tone = operation.Phase == "IN CONTACT" ? HudTone.Caution : HudTone.Info;
            }
            else if (war.Proposals.Count > 0)
            {
                text = "STAFF OPTIONS READY";
                detail = war.Proposals.Count + " choices on STR";
                bar = 0f;
                tone = HudTone.Info;
            }
            else return false;
            return true;
        }

        protected override void Write(IHudLine line) => line.Set(tone, text, detail, bar);
    }
}
