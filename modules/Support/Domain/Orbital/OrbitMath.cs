using System;

namespace BoscaliSummer.Features.Support.Domain.Orbital
{
    /// <summary>
    /// Two-body circular-orbit and spherical-Earth viewing geometry. Pure double-precision
    /// arithmetic with textbook constants, so a slant range, a horizon or an off-nadir angle the
    /// console shows is the real number for that altitude, not a gameplay table.
    /// </summary>
    internal static class OrbitMath
    {
        public const double EarthRadius = 6371000.0;
        public const double GravitationalParameter = 3.986004418e14;
        public const double Deg = Math.PI / 180.0;

        public static double Radius(double altitude) => EarthRadius + Math.Max(0.0, altitude);

        public static double Velocity(double altitude) => Math.Sqrt(GravitationalParameter / Radius(altitude));

        /// <summary>Elevation of the satellite seen from a ground point at the given central angle.</summary>
        public static double Elevation(double altitude, double centralAngle)
        {
            double ratio = EarthRadius / Radius(altitude);
            return Math.Atan2(Math.Cos(centralAngle) - ratio, Math.Sin(centralAngle));
        }

        /// <summary>Angle at the satellite between nadir and the line of sight to the ground point.</summary>
        public static double OffNadir(double altitude, double elevation)
        {
            double ratio = EarthRadius * Math.Cos(elevation) / Radius(altitude);
            return Math.Asin(Clamp(ratio, -1.0, 1.0));
        }

        /// <summary>Incidence on level ground: the angle between local vertical and the line of sight.</summary>
        public static double Incidence(double elevation) => Math.PI * 0.5 - elevation;

        public static double SlantRange(double altitude, double centralAngle)
        {
            double r = Radius(altitude);
            return Math.Sqrt(EarthRadius * EarthRadius + r * r - 2.0 * EarthRadius * r * Math.Cos(centralAngle));
        }

        /// <summary>Central angle reached by pointing <paramref name="offNadir"/> away from nadir.
        /// Beyond the Earth limb the result is the limb itself.</summary>
        public static double CentralAngleForOffNadir(double altitude, double offNadir)
        {
            double sine = Radius(altitude) / EarthRadius * Math.Sin(offNadir);
            if (sine >= 1.0) return Math.PI * 0.5 - offNadir;
            return Math.Asin(sine) - offNadir;
        }

        public static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;
    }
}
