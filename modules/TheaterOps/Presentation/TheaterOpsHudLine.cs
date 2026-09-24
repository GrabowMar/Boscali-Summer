using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;

namespace BoscaliSummer.Features.TheaterOps.Presentation
{
    /// <summary>
    /// The faction's main effort and the offensive behind it as one cockpit line. It reads the
    /// two host-published views exactly as the STR console does, marks them observed so they
    /// stay fresh, and states the host's own figures — never a local guess.
    /// </summary>
    internal sealed class TheaterOpsHudLine : HudLineWidget
    {
        private const string WidgetOwner = "theater-effort";

        private ITheaterPriorityView priority;
        private ITheaterOperationsView operations;

        private string text;
        private string detail;
        private float bar;
        private HudTone tone;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "theater";
        protected override string ChannelLabel => "Theater effort";
        protected override float RefreshSeconds => 1f;

        internal void Configure(ITheaterPriorityView priorityView, ITheaterOperationsView operationsView)
        {
            priority = priorityView;
            operations = operationsView;
            DeclareFeed();
        }

        protected override bool WantsLine()
        {
            text = null;
            if (priority == null || !priority.Available) return false;

            priority.Refresh();
            operations?.Refresh();

            text = TheaterOpsHudCopy.Effort(priority.HasPriority, priority.PriorityLabel);
            TheaterOperationView operation = FirstRunning();
            detail = operation != null
                ? TheaterOpsHudCopy.Detail(operation.Name, operation.Phase, operation.Countdown)
                : null;
            bar = operation != null ? TheaterOpsHudCopy.Bar(operation.Progress) : 0f;
            tone = operation != null &&
                (operation.Phase == TheaterOperationPhase.Launching ||
                 operation.Phase == TheaterOperationPhase.Assault)
                ? HudTone.Caution : HudTone.Info;
            return true;
        }

        protected override void Write(IHudLine line) => line.Set(tone, text, detail, bar);

        /// <summary>The newest offensive still running, or null when the board is concluded.</summary>
        private TheaterOperationView FirstRunning()
        {
            if (operations == null || !operations.Available) return null;
            System.Collections.Generic.IReadOnlyList<TheaterOperationView> plans = operations.Operations;
            if (plans == null) return null;
            for (int i = 0; i < plans.Count; i++)
                if (plans[i] != null && plans[i].Outcome == TheaterOperationOutcome.None) return plans[i];
            return null;
        }
    }
}
