using BoscaliSummer.Features.Radio.Domain;
using BoscaliSummer.Features.Radio.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;

namespace BoscaliSummer.Features.Radio.Presentation
{
    /// <summary>
    /// What the radio is playing as one cockpit line: the station or deck, the track, and its
    /// progress. A read-only mirror of the RAD screen's own state while the pilot is flying
    /// with the map closed; it tunes, starts and stops nothing.
    /// </summary>
    internal sealed class RadioHudLine : HudLineWidget
    {
        private const string WidgetOwner = "radio-now-playing";

        private RadioManager manager;

        private string text;
        private string detail;
        private float bar;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "radio";
        protected override string ChannelLabel => "Radio now playing";
        protected override float RefreshSeconds => 0.5f;

        internal void Configure(RadioManager source)
        {
            manager = source;
            DeclareFeed();
        }

        protected override bool WantsLine()
        {
            text = null;
            if (manager == null) return false;

            bool deck = manager.DeckEngaged;
            bool receiver = !deck && manager.IsEngaged;
            if (!deck && !receiver) return false;

            text = RadioHudCopy.Text(deck ? "MUSIC" : manager.CurrentChannelName);
            detail = deck
                ? RadioHudCopy.Detail(manager.DeckCurrentTitle, manager.DeckPaused)
                : RadioHudCopy.Detail(manager.CurrentTrackTitle, manager.IsPaused);
            bar = RadioHudCopy.Bar(deck ? manager.DeckProgress : manager.Progress);
            return true;
        }

        protected override void Write(IHudLine line) => line.Set(HudTone.Info, text, detail, bar);
    }
}
