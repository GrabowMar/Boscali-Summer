using System;

namespace BoscaliSummer.Features.Support.Domain.Orbital
{
    /// <summary>Persistent theatre sectors. Protocol 18 uses the existing seed word for
    /// origin * 9 + destination; no random orbit clock can move a station anymore.</summary>
    internal static class StationKeeping
    {
        public const int Count = 9;
        public const int Centre = 4;
        public const float Spacing = 18000f;
        private static readonly string[] Names = { "NORTH WEST", "NORTH", "NORTH EAST", "WEST", "CENTRE", "EAST", "SOUTH WEST", "SOUTH", "SOUTH EAST" };
        public static bool Valid(int sector) => sector >= 0 && sector < Count;
        public static bool ValidRoute(int route) => route >= 0 && route < Count * Count;
        public static int Target(int route) => route >= 0 && route < Count * Count ? route % Count : Centre;
        public static int Origin(int route) => route >= 0 && route < Count * Count ? route / Count : Centre;
        public static int Route(int from, int to) => from * Count + to;
        public static string Name(int sector) => Valid(sector) ? Names[sector] : "UNKNOWN SECTOR";
        public static float X(int sector) => (sector % 3 - 1) * Spacing;
        public static float Z(int sector) => (1 - sector / 3) * Spacing;

        public static OrbitState State(int route, in OrbitRegime regime, double clock)
        {
            if (double.IsNaN(clock) || double.IsInfinity(clock)) clock = -OrbitalPlatform.RephaseLeadSeconds;
            int target = Target(route), origin = Origin(route);
            double progress = OrbitMath.Clamp(1.0 + clock / OrbitalPlatform.RephaseLeadSeconds, 0.0, 1.0);
            double x = X(origin) + (X(target) - X(origin)) * progress;
            double z = Z(origin) + (Z(target) - Z(origin)) * progress;
            bool holding = !double.IsNaN(clock) && clock < 0.0;
            return new OrbitState(holding ? OrbitPhase.Hold : OrbitPhase.InPass,
                new PassPlan(0.0), regime.Altitude, x, z,
                holding ? -clock : 0.0, 0.0);
        }
    }
}
