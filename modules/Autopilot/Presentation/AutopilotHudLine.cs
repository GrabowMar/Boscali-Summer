using BoscaliSummer.Features.Autopilot.Domain;
using BoscaliSummer.Features.Autopilot.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;

namespace BoscaliSummer.Features.Autopilot.Presentation
{
    /// <summary>
    /// The landing autopilot as one cockpit line while it is flying the aircraft: the phase it
    /// is in, the runway or pad it is aiming at, and the range to the field. It reads the
    /// controller's own phase and adds nothing to it; disengaging drops the line.
    /// </summary>
    internal sealed class AutopilotHudLine : HudLineWidget
    {
        private const string WidgetOwner = "autopilot-landing";

        private AutopilotLandController controller;

        private string text;
        private string detail;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "autopilot";
        protected override string ChannelLabel => "Landing autopilot";

        internal void Configure(AutopilotLandController source)
        {
            controller = source;
            DeclareFeed();
        }

        protected override bool WantsLine()
        {
            text = null;
            if (controller == null || !controller.IsEngaged) return false;

            text = AutopilotHudCopy.Text(controller.HudPhaseWord);
            float metres = controller.HudDistanceMeters;
            detail = AutopilotHudCopy.Detail(controller.HudTargetName,
                metres >= 0f ? UnitConverter.DistanceReading(metres) : null);
            return true;
        }

        protected override void Write(IHudLine line) => line.Set(HudTone.Info, text, detail, 0f);
    }
}
