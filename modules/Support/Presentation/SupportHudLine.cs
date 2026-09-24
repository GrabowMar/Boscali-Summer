using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Networking;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// The support net as one cockpit line: ready, cooling with the retask clock, or a request
    /// in flight, plus the local allocation. It reads the local peer's own view and states
    /// nothing the host has not already accepted; the same numbers the OPS panel shows.
    /// </summary>
    internal sealed class SupportHudLine : HudLineWidget
    {
        private const string WidgetOwner = "support-net";

        private SupportManager manager;

        private string text;
        private string detail;
        private HudTone tone;
        private float bar;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "support";
        protected override string ChannelLabel => "Support net";

        internal void Configure(SupportManager source)
        {
            manager = source;
            DeclareFeed();
        }

        protected override bool WantsLine()
        {
            text = null;
            if (manager == null || !GameManager.GetLocalPlayer<Player>(out Player player) || player == null)
                return false;

            bool pending = manager.RequestPending;
            float remaining = manager.LocalCooldownRemaining;
            float total = manager.LocalCooldownTotal;
            float allocation = manager.LocalAllocation;

            tone = SupportHudCopy.Tone(pending, remaining);
            bar = SupportHudCopy.Bar(remaining, total, allocation);
            if (pending)
            {
                text = "SUPPORT · REQUEST PENDING";
                detail = "AWAITING HOST";
            }
            else if (remaining > SupportHudCopy.CoolingSeconds)
            {
                text = "SUPPORT · NET COOLING";
                detail = "RETASK " + SupportHudCopy.Cooldown(remaining);
            }
            else
            {
                text = "SUPPORT · NET READY";
                detail = "ALLOCATION " + SupportHudCopy.Percent(allocation);
            }
            return true;
        }

        protected override void Write(IHudLine line) => line.Set(tone, text, detail, bar);
    }
}
