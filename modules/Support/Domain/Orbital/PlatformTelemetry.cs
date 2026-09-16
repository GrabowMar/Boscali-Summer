using System;

namespace BoscaliSummer.Features.Support.Domain.Orbital
{
    /// <summary>
    /// Housekeeping telemetry for the console and the uplink, derived from state the model
    /// already owns: stored energy, sunlight, load, pass geometry and slewing. The figures are
    /// representative of a small station bus (120 V DC, lithium-ion strings, control moment
    /// gyros, Ku-band downlink) and deterministic for a given clock, so every screen showing
    /// the station agrees. They never feed back into gameplay.
    /// </summary>
    internal readonly struct TelemetryFrame
    {
        public readonly double BusVolts;
        public readonly double ArrayAmps;
        public readonly double WheelRpm;
        public readonly double TrussTempC;
        public readonly double LinkMarginDb;
        public readonly double PeriodSeconds;
        public readonly double VelocityMs;
        public readonly double GroundSpeedMs;

        public TelemetryFrame(double busVolts, double arrayAmps, double wheelRpm, double trussTempC,
                              double linkMarginDb, double periodSeconds, double velocityMs, double groundSpeedMs)
        {
            BusVolts = busVolts;
            ArrayAmps = arrayAmps;
            WheelRpm = wheelRpm;
            TrussTempC = trussTempC;
            LinkMarginDb = linkMarginDb;
            PeriodSeconds = periodSeconds;
            VelocityMs = velocityMs;
            GroundSpeedMs = groundSpeedMs;
        }

        public bool HasLink => LinkMarginDb > -50.0;
    }

    internal static class PlatformTelemetry
    {
        public static TelemetryFrame Compute(OrbitalPlatform platform, in PlatformStats stats, in LookAngles station,
                                             bool sunlit, bool slewing, double now)
        {
            double altitude = platform.Orbit.Altitude;
            double wobble = Math.Sin(now * 0.37 + platform.Seed * 0.001);
            double charge = stats.StorageKj > 0f ? platform.Energy / stats.StorageKj : 0.0;

            double busVolts = platform.Brownout ? 96.0 + 4.0 * wobble : 112.0 + 12.0 * charge + 0.4 * wobble;
            double arrayAmps = sunlit ? stats.SolarKw * 1000.0 / 120.0 * (1.0 + 0.02 * Math.Sin(now * 0.05)) : 0.0;
            double wheelRpm = (slewing ? 6400.0 : 4800.0) * (stats.Stabilised ? 1.0 : 0.0) + 40.0 * wobble;
            double trussTemp = (sunlit ? 42.0 : -35.0) + (stats.Overheating ? 38.0 : 0.0) + 3.0 * wobble;

            // Link budget from slant range to the ground station: free-space loss relative to a
            // 1 000 km reference, plus a small elevation-dependent atmospheric term.
            double link = -99.0;
            if (station.Visible && station.SlantRange > 1.0)
            {
                double pathDb = 20.0 * Math.Log10(station.SlantRange / 1000000.0);
                double atmosphereDb = 1.2 / Math.Max(0.1, Math.Sin(station.Elevation));
                link = 16.0 - pathDb - atmosphereDb;
            }

            return new TelemetryFrame(busVolts, arrayAmps, wheelRpm, trussTemp, link,
                OrbitMath.Period(altitude), OrbitMath.Velocity(altitude), OrbitMath.GroundSpeed(altitude));
        }
    }
}
