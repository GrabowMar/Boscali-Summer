using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// The landing autopilot's gauge of the TAC BOARD's row (HUD v8): the chain stage word (INTERCEPT, GLIDE, FLARE, ROLLOUT; APPROACH, DESCENT, TOUCHDOWN over a pad) with the
    /// runway or pad it is flying to under it, while the autopilot is flying the aircraft. It reads the APPROACH page's own feed (<see cref="ApproachScopeFeed"/>), so the words and
    /// the cached runway name are the page's and a steady frame allocates nothing. The player's own NAV / HDG / ALT autopilot is the Wing module's gauge.
    /// </summary>
    internal sealed class AutopilotScopeGauge
    {
        public void Register() => ScopeGauges.Register("land", Read);

        public bool Read(out GaugeSpec gauge)
        {
            gauge = default;
            if (!ScopeFeeds.TryRead(out ApproachScope d) || !d.Autoland) return false;
            gauge = new GaugeSpec { Id = "land", Label = "AUTOPILOT", Kind = GaugeKind.Mode, Text = ApproachWords.Of(d.Stage, d.Pad), Sub = d.Pad ? d.Field : d.Runway, Value = 0f, Tone = ScopeTone.Ink, Rank = 45 };
            return true;
        }
    }
}
