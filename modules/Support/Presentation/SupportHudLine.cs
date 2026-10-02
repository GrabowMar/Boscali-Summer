using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The support net's cockpit line: armed / pending / recent refusal of the CALL flow.
    /// </summary>
    internal sealed class SupportHudLine : HudLineWidget
    {
        private const string WidgetOwner = "support-net";

        private CallsController calls;
        private SupportManager manager;
        private string text, detail;
        private HudTone tone;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "support";
        protected override string ChannelLabel => "Support net";

        internal void Configure(SupportManager manager, CallsController calls)
        {
            this.manager = manager;
            this.calls = calls;
            DeclareFeed();
        }

        protected override bool WantsLine()
        {
            if (calls == null || manager == null) return false;
            if (calls.Armed.HasValue)
            {
                SupportActionId id = calls.Armed.Value;
                text = "CALL ARMED · " + (CallSheet.TryGet(id, out CallRow row) ? row.Label : id.ToString());
                detail = manager.Quote(id).Cost + " CR · " + Aim.Label(calls.AimNow);
                tone = HudTone.Caution;
                return true;
            }
            if (calls.Pending) { text = "CALL PENDING"; detail = ""; tone = HudTone.Info; return true; }
            if (!string.IsNullOrEmpty(calls.LastWords) && calls.LastWords.StartsWith("NEGATIVE")
                && UnityEngine.Time.unscaledTime - calls.LastWordsAt < 4f)
            {
                text = calls.LastWords; detail = ""; tone = HudTone.Warning; return true;
            }
            return false;
        }

        protected override void Write(IHudLine line) => line.Set(tone, text, detail, 0f);
    }
}
