using System;
using System.Collections;
using System.Reflection;
using BoscaliSummer.Core.Math;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// Live readings of an aircraft that several presentation modules show, each read through the game's own live value
    /// instead of a stale or wrong one: fuel is the sum of the tanks (<c>Aircraft.fuelLevel</c> is only the spawn/refuel
    /// target and never moves in flight) and G is the native load factor, not <c>Aircraft.gForce</c>.
    /// </summary>
    internal static class AircraftReadout
    {
        private static readonly FieldInfo CountermeasureStations = AccessTools.Field(typeof(CountermeasureManager), "countermeasureStations");

        /// <summary>
        /// The pilot's active countermeasure; null when there is none. The game's own <c>GetActiveCountermeasure</c> indexes its private station list with <c>activeIndex</c> unchecked,
        /// and that index is 255 after the game auto-chose "none" for a seeker nothing counters and stale after a lost pod took its station out of the list: it throws then.
        /// </summary>
        public static Countermeasure ActiveCountermeasure(Aircraft aircraft)
        {
            CountermeasureManager manager = aircraft != null ? aircraft.countermeasureManager : null;
            if (manager == null) return null;
            if (CountermeasureStations?.GetValue(manager) is ICollection stations) return manager.activeIndex < stations.Count ? manager.GetActiveCountermeasure() : null;
            try { return manager.GetActiveCountermeasure(); }     // the list was renamed: the game's own call, guarded
            catch (ArgumentOutOfRangeException) { return null; }
        }

        private static FieldInfo stationName;

        /// <summary>
        /// The display names of the aircraft's countermeasure stations in the order the game's Next Countermeasure steps through them, written into <paramref name="names"/>; the count written, 0 when the list cannot be read.
        /// </summary>
        public static int CountermeasureOrder(Aircraft aircraft, string[] names)
        {
            CountermeasureManager manager = aircraft != null ? aircraft.countermeasureManager : null;
            if (manager == null || !(CountermeasureStations?.GetValue(manager) is IList stations)) return 0;
            int n = 0;
            for (int i = 0; i < stations.Count && n < names.Length; i++)
            {
                object station = stations[i];
                if (station == null) continue;
                stationName ??= AccessTools.Field(station.GetType(), "displayName");
                names[n++] = stationName?.GetValue(station) as string;
            }
            return n;
        }

        /// <summary>The aircraft's first seat (<c>pilots[0]</c>), or null when there is no aircraft or no seat.</summary>
        public static Pilot PrimaryPilot(Aircraft aircraft) =>
            aircraft != null && aircraft.pilots != null && aircraft.pilots.Length > 0 ? aircraft.pilots[0] : null;

        /// <summary>Live fuel, 0..1 (tank mass over tank capacity). Zero for no aircraft.</summary>
        public static float Fuel(Aircraft aircraft) => aircraft == null ? 0f : Mathf.Clamp01(aircraft.GetFuelLevel());

        /// <summary>The airframe's G limit (<c>AircraftParameters.aircraftGLimit</c>, default 9).</summary>
        public static float GLimit(Aircraft aircraft)
        {
            AircraftParameters parameters = aircraft != null ? aircraft.GetAircraftParameters() : null;
            return parameters != null && parameters.aircraftGLimit > 1f ? parameters.aircraftGLimit : FlightLoad.DefaultLimit;
        }

        /// <summary>The native AoA readout's value (<c>AoADisplay</c>): the cockpit-frame velocity's angle; NaN while slower than <see cref="FlightLoad.AoaMinSpeed"/> or without a cockpit.</summary>
        public static float AngleOfAttack(Aircraft aircraft)
        {
            UnitPart cockpit = aircraft != null ? aircraft.cockpit : null;
            if (cockpit == null || cockpit.rb == null || aircraft.speed <= FlightLoad.AoaMinSpeed) return float.NaN;
            Vector3 v = cockpit.transform.InverseTransformDirection(cockpit.rb.velocity);
            return FlightLoad.AngleOfAttackDeg(v.y, v.z);
        }

        /// <summary>The native G-meter reading: seat acceleration plus gravity along the aircraft's up axis.</summary>
        public static float LoadFactor(Aircraft aircraft)
        {
            if (aircraft == null) return 1f;
            Pilot seat = PrimaryPilot(aircraft);
            Vector3 accel = seat != null ? seat.GetAccel() : aircraft.accel;
            Vector3 up = aircraft.transform.up;
            return FlightLoad.Factor(accel.x, accel.y, accel.z, up.x, up.y, up.z);
        }
    }
}
