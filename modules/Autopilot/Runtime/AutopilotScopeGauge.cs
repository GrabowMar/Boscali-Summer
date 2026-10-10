using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// The landing autopilot's gauge of the TAC BOARD's row (HUD v8): the mode word (the controller's own phase: JOINING, FINAL, ROLLOUT, a carrier leg...) with the
    /// runway or pad it is flying to under it, while the autopilot is flying the aircraft. The phase and target strings are the controller's own constants and
    /// the runway's name, so a steady frame allocates nothing. The player's own NAV / HDG / ALT autopilot is the Wing module's gauge.
    /// </summary>
    internal sealed class AutopilotScopeGauge
    {
        private readonly AutopilotLandController controller;

        public AutopilotScopeGauge(AutopilotLandController controller) { this.controller = controller; }

        public void Register() => ScopeGauges.Register("land", Read);

        public bool Read(out GaugeSpec gauge)
        {
            gauge = default;
            if (controller == null || !controller.IsEngaged) return false;
            string phase = controller.HudPhaseWord;
            if (phase == null) return false;
            gauge = new GaugeSpec { Id = "land", Label = "AUTOPILOT", Kind = GaugeKind.Mode, Text = phase, Sub = controller.HudTargetName, Value = 0f, Tone = ScopeTone.Ink, Rank = 40 };
            return true;
        }
    }
}
