using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
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

        public static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;
    }
}
