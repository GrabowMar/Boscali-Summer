using BoscaliSummer.Features.QoL.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.QoL.Presentation
{
    /// <summary>
    /// Own fuel state and the nearest friendly field as one cockpit line, with the field's name
    /// and range as the divert readout. Client-local and read-only: it queries the game's own
    /// nearest-airbase search once a second and never requests, spawns or commands anything.
    /// </summary>
    internal sealed class FuelHudLine : HudLineWidget
    {
        private const string WidgetOwner = "qol-fuel";

        private string text;
        private string detail;
        private HudTone tone;
        private float bar;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "fuel";
        protected override string ChannelLabel => "Fuel and divert";
        protected override float RefreshSeconds => 1f;

        protected override bool WantsLine()
        {
            text = null;
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null ||
                aircraft.disabled || aircraft.HasEjected()) return false;

            float level = aircraft.fuelLevel;
            tone = FuelHudCopy.Tone(level);
            bar = FuelHudCopy.Bar(level);
            text = FuelHudCopy.Fuel(level);

            Airbase field = NearestField(aircraft);
            string distance = field != null && field.center != null
                ? UnitConverter.DistanceReading(
                    Vector3.Distance(aircraft.transform.position, field.center.position))
                : null;
            detail = FuelHudCopy.Divert(field != null ? field.name : null, distance);
            return true;
        }

        protected override void Write(IHudLine line) => line.Set(tone, text, detail, bar);

        private static Airbase NearestField(Aircraft aircraft)
        {
            if (aircraft.NetworkHQ == null) return null;
            var query = new RunwayQuery
            {
                RunwayType = RunwayQueryType.Landing,
                MinSize = 0f,
                LandingSpeed = 0f,
                TailHook = false
            };
            return aircraft.NetworkHQ.GetNearestAirbase(aircraft.transform.position, query);
        }
    }
}
