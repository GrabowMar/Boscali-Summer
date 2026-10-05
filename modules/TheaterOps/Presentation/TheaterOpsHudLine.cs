using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.TheaterOps.Presentation
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
            bar = 0f;
            if (!war.HasSnapshot)
            {
                bool unconfirmed = (war.CommandStatus ?? "").StartsWith("UNCONFIRMED", System.StringComparison.OrdinalIgnoreCase);
                text = unconfirmed ? "STAFF COMMAND UNCONFIRMED" : "WAITING FOR STAFF";
                detail = unconfirmed ? war.CommandStatus : "Host faction report requested";
                tone = unconfirmed ? HudTone.Caution : HudTone.Info;
                return true;
            }
            if (war.SnapshotAgeSeconds > 15f)
            {
                text = "STALE STAFF REPORT";
                detail = "Recovering host report · commands disabled";
                tone = HudTone.Caution;
                return true;
            }
            if (war.CommandPending)
            {
                text = "STAFF COMMAND PENDING";
                detail = war.CommandStatus;
                tone = HudTone.Info;
                return true;
            }
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
