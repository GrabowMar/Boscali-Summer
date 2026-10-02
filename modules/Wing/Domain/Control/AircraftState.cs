using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
// Filled by the engine sensor (M1c) and the FlightSim; the mod assembly only reads it until then.
#pragma warning disable CS0649

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Per-tick snapshot of one aircraft, filled once by the engine sensor or the sim and then
    /// only read. SI units, angles in degrees, Unity world axes. Signs: bank and roll rate positive
    /// right-wing-down, pitch and pitch rate positive nose-up, yaw rate positive nose-right, sideslip
    /// positive when the aircraft moves toward its right.</summary>
    internal struct AircraftState
    {
        public Vec3 Pos, Vel, Acc;
        public Vec3 Fwd, Up, Right;
        public float BankDeg, PitchDeg, GammaDeg, SideslipDeg;
        public float P, Q, R;
        public float Tas, Qbar, Nz, RadarAlt, Throttle, Dt;
        public bool FbwActive, AirbrakeOpen;
        /// <summary>Main rotor speed over nominal (the mean over the rotor shafts); 0 when the aircraft has no rotor or
        /// the sensor cannot read it.</summary>
        public float RotorRpm;

        public float Speed => Vel.Length;

        /// <summary>Equivalent airspeed √(2·q̄/ρ₀): what stall, lift-limited g and loaded minimum speed scale with.</summary>
        public float Eas => (float)System.Math.Sqrt(2f * System.Math.Max(0f, Qbar) / Isa.SeaLevelDensity);
        public float TrackDeg => Vec3.HeadingDeg(Vel);

        /// <summary>Specific-energy rate V·V̇/g + ḣ in m/s, the quantity the throttle controls.</summary>
        public float EnergyRate
        {
            get
            {
                float v = Vel.Length;
                if (v < 1f) return Vel.Y;
                return v * Vec3.Dot(Acc, Vel / v) / Scalar.G + Vel.Y;
            }
        }
    }
}
