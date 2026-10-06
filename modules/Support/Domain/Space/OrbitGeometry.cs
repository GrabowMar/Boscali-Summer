using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    // Orbit geometry value types kept from the retired station model; SarCollector still reads them.
    /// <summary>The geometry of one theatre pass: heading and direction.</summary>
    internal readonly struct PassPlan
    {
        public readonly double Heading;
        public readonly double DirX, DirZ;

        public PassPlan(double heading)
        {
            Heading = heading;
            DirX = Math.Sin(heading);
            DirZ = Math.Cos(heading);
        }
    }

    internal enum OrbitPhase : byte
    {
        /// <summary>Not on a pass cycle yet: insertion, an orbit transfer or a rephase burn.</summary>
        Hold = 0,
        InPass = 1
    }

    /// <summary>Where a station is at one moment, in theatre metres (x east, z north).</summary>
    internal readonly struct OrbitState
    {
        public readonly OrbitPhase Phase;
        public readonly PassPlan Pass;
        public readonly double Altitude;
        public readonly double SubX, SubZ;

        /// <summary>Seconds until the pass window opens (0 while in a pass).</summary>
        public readonly double TimeToPass;

        /// <summary>Seconds until the pass window closes (0 while out of a pass).</summary>
        public readonly double TimeToPassEnd;

        public OrbitState(OrbitPhase phase, PassPlan pass, double altitude, double subX, double subZ,
                          double timeToPass, double timeToPassEnd)
        {
            Phase = phase;
            Pass = pass;
            Altitude = altitude;
            SubX = subX;
            SubZ = subZ;
            TimeToPass = timeToPass;
            TimeToPassEnd = timeToPassEnd;
        }
    }

    /// <summary>A station seen from a ground point.</summary>
    internal readonly struct LookAngles
    {
        public readonly bool Visible;
        public readonly double Elevation;
        public readonly double OffNadir;
        public readonly double SlantRange;

        /// <summary>Horizontal unit vector from the ground point toward the sub-station point.</summary>
        public readonly double AzimuthX, AzimuthZ;

        public LookAngles(bool visible, double elevation, double offNadir,
                          double slantRange, double azimuthX, double azimuthZ)
        {
            Visible = visible;
            Elevation = elevation;
            OffNadir = offNadir;
            SlantRange = slantRange;
            AzimuthX = azimuthX;
            AzimuthZ = azimuthZ;
        }

        public double Incidence => OrbitMath.Incidence(Elevation);

        public static LookAngles Hidden => new LookAngles(false, -Math.PI * 0.5, 0.0, 0.0, 0.0, 1.0);
    }

}
