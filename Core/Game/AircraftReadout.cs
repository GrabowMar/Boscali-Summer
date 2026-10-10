using BoscaliSummer.Core.Math;
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
            Vector3 accel = aircraft.pilots != null && aircraft.pilots.Length > 0 && aircraft.pilots[0] != null
                ? aircraft.pilots[0].GetAccel() : aircraft.accel;
            Vector3 up = aircraft.transform.up;
            return FlightLoad.Factor(accel.x, accel.y, accel.z, up.x, up.y, up.z);
        }
    }
}
