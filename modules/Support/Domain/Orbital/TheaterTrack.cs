using System;

namespace BoscaliSummer.Features.Support.Domain.Orbital
{
    /// <summary>One orbit band a station can fly. Wire-stable by <see cref="Index"/>.</summary>
    internal readonly struct OrbitRegime
    {
        public readonly byte Index;
        public readonly string Code;
        public readonly string Name;
        public readonly double Altitude;
        public readonly double InclinationDeg;

        /// <summary>Imager ground sample distance straight down, metres.</summary>
        public readonly double NadirGsd;

        /// <summary>Radar-scan and ELINT footprint multiplier: a higher orbit sees wider.</summary>
        public readonly float ScanScale;

        /// <summary>Kinetic rod circular error at release from this band, metres.</summary>
        public readonly float RodScatter;

        /// <summary>EMP radius multiplier: a higher burst illuminates more ground.</summary>
        public readonly float EmpScale;

        /// <summary>Propellant atmospheric drag costs per second at this band.</summary>
        public readonly float DragFuelPerSecond;

        public OrbitRegime(byte index, string code, string name, double altitude, double inclinationDeg,
                           double nadirGsd, float scanScale, float rodScatter, float empScale,
                           float dragFuelPerSecond)
        {
            Index = index;
            Code = code;
            Name = name;
            Altitude = altitude;
            InclinationDeg = inclinationDeg;
            NadirGsd = nadirGsd;
            ScanScale = scanScale;
            RodScatter = rodScatter;
            EmpScale = empScale;
            DragFuelPerSecond = dragFuelPerSecond;
        }
    }

    internal static class OrbitRegimes
    {
        public const byte Standard = 0;
        public const byte Low = 0;
        public const byte Mid = 0;
        public const byte High = 0;

        public static readonly OrbitRegime[] All =
        {
            new OrbitRegime(Standard, "LEO", "LOW EARTH ORBIT", 500000.0, 51.6, 0.4, 1f, 15f, 1f, 0f)
        };

        public static bool Valid(int index) => index == 0;

        public static OrbitRegime Get(int index) => All[0];
    }

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

        public bool InPass => Phase == OrbitPhase.InPass;
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

        /// <summary>Compass bearing from the ground point to the station, degrees 0..360.</summary>
        public double AzimuthDeg
        {
            get
            {
                double deg = Math.Atan2(AzimuthX, AzimuthZ) / OrbitMath.Deg;
                return deg < 0.0 ? deg + 360.0 : deg;
            }
        }

        public static LookAngles Hidden => new LookAngles(false, -Math.PI * 0.5, 0.0, 0.0, 0.0, 1.0);
    }

    /// <summary>
    /// Line-of-sight geometry from a ground point to a station, and the imager ground sample
    /// distance that follows from it.
    /// </summary>
    internal static class TheaterTrack
    {
        public static LookAngles Look(in OrbitState state, double x, double z)
        {
            if (!state.InPass) return LookAngles.Hidden;
            double dx = state.SubX - x;
            double dz = state.SubZ - z;
            double ground = Math.Sqrt(dx * dx + dz * dz);
            double central = ground / OrbitMath.EarthRadius;
            double elevation = OrbitMath.Elevation(state.Altitude, central);
            if (elevation <= 0.0) return LookAngles.Hidden;
            double azX = ground > 1.0 ? dx / ground : 0.0;
            double azZ = ground > 1.0 ? dz / ground : 1.0;
            return new LookAngles(true, elevation, OrbitMath.OffNadir(state.Altitude, elevation),
                OrbitMath.SlantRange(state.Altitude, central), azX, azZ);
        }

        /// <summary>Imager ground sample distance for a look, metres; infrared is three times coarser.</summary>
        public static double GroundSample(in OrbitRegime regime, in LookAngles look, bool infrared)
        {
            if (!look.Visible) return 0.0;
            double gsd = regime.NadirGsd * (look.SlantRange / regime.Altitude) / Math.Max(0.2, Math.Cos(look.OffNadir));
            return infrared ? gsd * 3.0 : gsd;
        }
    }
}
