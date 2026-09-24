using BoscaliSummer.Features.Autopilot.Configuration;
using BoscaliSummer.Features.Autopilot.Domain;
using BoscaliSummer.Features.Autopilot.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Autopilot.Presentation
{
    /// <summary>
    /// Gear-down ILS as one HUD line: localizer / glideslope words, inside/out of the
    /// field ring, on-course bar. Geometry only; it never requests a runway slot.
    /// </summary>
    internal sealed class IlsHudLine : HudLineWidget
    {
        private const string WidgetOwner = "autopilot-ils";

        private AutopilotSettings settings;
        private string text;
        private string detail;
        private HudTone tone;
        private float bar;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "ils";
        protected override string ChannelLabel => "ILS approach";
        protected override float RefreshSeconds => 0.2f;

        internal void Configure(AutopilotSettings config)
        {
            settings = config;
            DeclareFeed();
        }

        protected override bool WantsLine()
        {
            text = null;
            if (settings == null || !settings.IlsHud.Value) return false;
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null) return false;
            if (aircraft.disabled || aircraft.HasEjected()) return false;
            if (aircraft.gearState == LandingGear.GearState.LockedRetracted) return false;

            if (!TryFrame(aircraft, out Vector3 origin, out Vector3 dir, out float radius)) return false;

            Vector3 pos = aircraft.transform.position;
            Vector3 flat = pos - origin;
            flat.y = 0f;
            float alongM = Vector3.Dot(flat, dir.normalized);
            if (alongM < 0f) alongM = -alongM;
            float crossM = Vector3.Dot(flat, Vector3.Cross(Vector3.up, dir.normalized));
            float height = aircraft.radarAlt > 0.5f ? aircraft.radarAlt : pos.y - origin.y;

            float slope = settings.Slope;
            float loc = IlsGuidance.Beam(IlsGuidance.LocalizerDegrees(crossM, alongM), IlsGuidance.LocFullScaleDeg);
            float gs = IlsGuidance.Beam(
                IlsGuidance.GlideslopeErrorDegrees(height, alongM, slope), IlsGuidance.GsFullScaleDeg);
            bool inside = IlsGuidance.InsideRadius(flat.magnitude, radius);
            bool caution = IlsGuidance.Caution(loc, gs);

            string distance = UnitConverter.DistanceReading(flat.magnitude);
            text = IlsHudCopy.Text(caution);
            detail = IlsHudCopy.Detail(IlsGuidance.LocWord(loc), IlsGuidance.GsWord(gs), inside, distance);
            bar = IlsHudCopy.Bar(loc);
            tone = caution ? HudTone.Caution : HudTone.Info;
            return true;
        }

        protected override void Write(IHudLine line) => line.Set(tone, text, detail, bar);

        private static bool TryFrame(Aircraft aircraft, out Vector3 origin, out Vector3 dir, out float radius)
        {
            origin = default;
            dir = default;
            radius = 0f;
            AutopilotLandController controller = AutopilotLandController.Instance;
            if (controller != null && controller.TryIlsFrame(out origin, out dir, out radius)) return true;

            if (aircraft.NetworkHQ == null) return false;
            var query = new RunwayQuery
            {
                RunwayType = RunwayQueryType.Landing,
                MinSize = 0f,
                LandingSpeed = 0f,
                TailHook = false
            };
            Airbase field = aircraft.NetworkHQ.GetNearestAirbase(aircraft.transform.position, query);
            if (field == null) return false;
            Airbase.Runway runway = field.GetLandingRunway();
            if (runway == null) return false;
            origin = runway.GetNearestPoint(aircraft.transform.position, false);
            dir = runway.GetDirection(true);
            try { radius = field.GetRadius(); }
            catch { radius = 0f; }
            return dir.sqrMagnitude > 0.01f;
        }
    }
}
