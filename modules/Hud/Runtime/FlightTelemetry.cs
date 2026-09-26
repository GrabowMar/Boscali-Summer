using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BoscaliSummer.Features.Hud.Runtime
{
    internal struct FlightReading
    {
        public bool Valid;
        public float Speed, Altitude, RadarAltitude, Climb, Heading, Pitch, Roll, Fuel, Throttle, G, Mach, AoA;
    }

    internal struct SystemsReading
    {
        public bool Valid, FaultsAvailable;
        public int Damaged, Detached, Failures;
        public bool Affected => Damaged > 0 || Detached > 0 || Failures > 0;
    }

    /// <summary>Read-only ownship data. No UI, subscriptions, prefab copies or aircraft mutation.</summary>
    internal sealed class FlightTelemetry
    {
        private static readonly FieldInfo Lamps = typeof(StatusDisplay).GetField("failureIndicatorsLookup", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo Owner = typeof(StatusDisplay).GetField("aircraft", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly List<UnitPart> parts = new List<UnitPart>(128);
        private Aircraft owner;
        private StatusDisplay nativeStatus;
        private float nextSystems, nextLookup;
        public FlightReading Flight { get; private set; }
        public SystemsReading Systems { get; private set; }

        public void Read(Aircraft aircraft)
        {
            if (owner != aircraft)
            {
                Reset(); owner = aircraft;
                if (aircraft != null && aircraft.partLookup != null)
                    foreach (UnitPart part in aircraft.partLookup)
                    { if (parts.Count == 128) break; if (part != null) parts.Add(part); }
            }
            if (aircraft == null || aircraft.cockpit == null || aircraft.cockpit.rb == null) { Flight = default; return; }
            Transform cockpit = aircraft.cockpit.transform;
            Vector3 airflow = cockpit.InverseTransformDirection(aircraft.cockpit.rb.velocity);
            float load = aircraft.pilots != null && aircraft.pilots.Length > 0 && aircraft.pilots[0] != null
                ? Vector3.Dot(aircraft.pilots[0].GetAccel() + Vector3.up, aircraft.transform.up) : float.NaN;
            Flight = new FlightReading
            {
                Valid = true, Speed = aircraft.speed, Altitude = aircraft.transform.position.GlobalY(),
                RadarAltitude = aircraft.radarAlt, Climb = aircraft.cockpit.rb.velocity.y,
                Heading = cockpit.eulerAngles.y, Pitch = -Mathf.DeltaAngle(0, cockpit.eulerAngles.x),
                Roll = Mathf.DeltaAngle(0, cockpit.eulerAngles.z), Fuel = Mathf.Clamp01(aircraft.GetFuelLevel()),
                Throttle = Mathf.Clamp01(aircraft.GetInputs().throttle), G = load,
                Mach = aircraft.speed / Mathf.Max(1, LevelInfo.GetSpeedOfSound(aircraft.transform.position.GlobalY())),
                AoA = aircraft.speed > 10 ? -Mathf.Atan2(airflow.y, airflow.z) * Mathf.Rad2Deg : float.NaN
            };
            if (Time.unscaledTime < nextSystems) return;
            nextSystems = Time.unscaledTime + .25f;
            var reading = new SystemsReading { Valid = parts.Count > 0 };
            foreach (UnitPart part in parts)
            {
                if (part == null || part.IsDetached()) reading.Detached++;
                else if (part.hitPoints < 98f) reading.Damaged++;
            }
            if (nativeStatus == null && Time.unscaledTime >= nextLookup)
            {
                nextLookup = Time.unscaledTime + 2f;
                FlightHud hud = SceneSingleton<FlightHud>.i;
                nativeStatus = hud != null && hud.statusAnchor != null ? hud.statusAnchor.GetComponentInChildren<StatusDisplay>(true) : null;
                if (nativeStatus != null && (Owner == null || !ReferenceEquals(Owner.GetValue(nativeStatus), aircraft))) nativeStatus = null;
            }
            if (nativeStatus != null && Lamps?.GetValue(nativeStatus) is IDictionary lamps)
            {
                reading.FaultsAvailable = true;
                int count = 0;
                foreach (DictionaryEntry item in lamps)
                { if (++count > 64) break; if (item.Value is GameObject lamp && lamp.activeSelf) reading.Failures++; }
            }
            Systems = reading;
        }

        public void Reset()
        { owner = null; nativeStatus = null; parts.Clear(); nextSystems = nextLookup = 0; Flight = default; Systems = default; }
    }
}
