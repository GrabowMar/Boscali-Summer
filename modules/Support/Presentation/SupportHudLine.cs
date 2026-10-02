using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The support net's cockpit line. Stubbed while the old OPS copy is gone: it declares its
    /// feed and shows nothing until the CALLS rewrite (OPS M0 Task 9) gives it a line again.
    /// </summary>
    internal sealed class SupportHudLine : HudLineWidget
    {
        private const string WidgetOwner = "support-net";

        private SupportManager manager;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "support";
        protected override string ChannelLabel => "Support net";

        internal void Configure(SupportManager source)
        {
            manager = source;
            DeclareFeed();
        }

        protected override bool WantsLine() => false;

        protected override void Write(IHudLine line) { }
    }
}
