using System;
using BoscaliSummer.Core;

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

        /// <summary>Simulated out-of-theatre arc, seconds, before the next pass begins.</summary>
        public readonly double GapSeconds;

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

        public readonly string Summary;

        public OrbitRegime(byte index, string code, string name, double altitude, double inclinationDeg,
                           double gapSeconds, double nadirGsd, float scanScale, float rodScatter, float empScale,
                           float dragFuelPerSecond, string summary)
        {
            Index = index;
            Code = code;
            Name = name;
            Altitude = altitude;
            InclinationDeg = inclinationDeg;
            GapSeconds = gapSeconds;
            NadirGsd = nadirGsd;
            ScanScale = scanScale;
            RodScatter = rodScatter;
            EmpScale = empScale;
            DragFuelPerSecond = dragFuelPerSecond;
            Summary = summary;
        }
    }

    internal static class OrbitRegimes
    {
        public const byte Low = 0;
        public const byte Mid = 1;
        public const byte High = 2;

        public static readonly OrbitRegime[] All =
        {
            new OrbitRegime(Low, "LOW", "LOW ORBIT", 300000.0, 51.6, 45.0, 0.3, 0.8f, 8f, 0.8f, 0.02f,
                "Sharp optics, tight rods, short passes. Drag burns fuel."),
            new OrbitRegime(Mid, "MID", "MID ORBIT", 450000.0, 51.6, 60.0, 0.5, 1f, 20f, 1f, 0f,
                "Balanced passes, optics and scatter."),
            new OrbitRegime(High, "HIGH", "HIGH ORBIT", 700000.0, 51.6, 90.0, 0.9, 1.4f, 45f, 1.25f, 0f,
                "Long passes, wide scans and EMP. Blurry optics, loose rods.")
        };

        public static bool Valid(int index) => index >= 0 && index < All.Length;

        public static OrbitRegime Get(int index) => All[Math.Max(0, Math.Min(All.Length - 1, index))];
    }

    /// <summary>Timing knob the host decides; clients use the host's value from settings.</summary>
    internal readonly struct OrbitClock
    {
        public readonly double GapScale;

        public OrbitClock(double gapScale) => GapScale = OrbitMath.Clamp(gapScale, 0.25, 4.0);

        public static OrbitClock Default => new OrbitClock(1.0);
    }

    /// <summary>The geometry of one theatre pass: heading, direction and cross-track offset.</summary>
    internal readonly struct PassPlan
    {
        public readonly int Index;
        public readonly bool Ascending;
        public readonly double Heading;
        public readonly double DirX, DirZ;
        public readonly double CrossTrack;

        public PassPlan(int index, bool ascending, double heading, double crossTrack)
        {
            Index = index;
            Ascending = ascending;
            Heading = heading;
            DirX = Math.Sin(heading);
            DirZ = Math.Cos(heading);
            CrossTrack = crossTrack;
        }

        /// <summary>Unit vector to the right of the direction of travel.</summary>
        public double RightX => DirZ;
        public double RightZ => -DirX;
    }

    internal enum OrbitPhase : byte
    {
        /// <summary>Not on a pass cycle yet: insertion, an orbit transfer or a rephase burn.</summary>
        Hold = 0,
        InPass = 1,
        OutOfTheater = 2
    }

    /// <summary>Where a station is at one moment, in theatre metres (x east, z north).</summary>
    internal readonly struct OrbitState
    {
        public readonly OrbitPhase Phase;
        public readonly PassPlan Pass;
        public readonly double Altitude;
        public readonly double SubX, SubZ;
        public readonly double TimeInPass;

        /// <summary>Seconds until the pass window opens (0 while in a pass).</summary>
        public readonly double TimeToPass;

        /// <summary>Seconds until the pass window closes (0 while out of a pass).</summary>
        public readonly double TimeToPassEnd;

        /// <summary>Length of the current (or next) pass window, seconds.</summary>
        public readonly double Window;

        public OrbitState(OrbitPhase phase, PassPlan pass, double altitude, double subX, double subZ,
                          double timeInPass, double timeToPass, double timeToPassEnd, double window)
        {
            Phase = phase;
            Pass = pass;
            Altitude = altitude;
            SubX = subX;
            SubZ = subZ;
            TimeInPass = timeInPass;
            TimeToPass = timeToPass;
            TimeToPassEnd = timeToPassEnd;
            Window = window;
        }

        public bool InPass => Phase == OrbitPhase.InPass;
    }

    /// <summary>A station seen from a ground point.</summary>
    internal readonly struct LookAngles
    {
        public readonly bool Visible;
        public readonly double GroundRange;
        public readonly double Elevation;
        public readonly double OffNadir;
        public readonly double SlantRange;

        /// <summary>Horizontal unit vector from the ground point toward the sub-station point.</summary>
        public readonly double AzimuthX, AzimuthZ;

        public LookAngles(bool visible, double groundRange, double elevation, double offNadir,
                          double slantRange, double azimuthX, double azimuthZ)
        {
            Visible = visible;
            GroundRange = groundRange;
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

        public static LookAngles Hidden => new LookAngles(false, 0.0, -Math.PI * 0.5, 0.0, 0.0, 0.0, 1.0);
    }

    /// <summary>
    /// Theatre passes. A pass is the stretch of a straight ground track, flown at real ground
    /// speed, during which the station is within <see cref="ReachOffNadirDeg"/> of the theatre
    /// centre — the reach every payload shares — followed by a time-compressed out-of-theatre
    /// arc. Heading follows from the inclination at a nominal 40°N; ascending and descending
    /// alternate by seed; each pass gets a deterministic cross-track offset, so geometry varies
    /// from pass to pass while every peer computes the same sky from the same seed and clock.
    /// Time is measured from the start of pass zero; negative time is a hold.
    /// </summary>
    internal static class TheaterTrack
    {
        public static readonly double TheaterLatitude = 40.0 * OrbitMath.Deg;
        public const double ReachOffNadirDeg = 55.0;
        public const double CrossTrackFraction = 0.35;

        public static double HalfWindow(in OrbitRegime regime) =>
            OrbitMath.CentralAngleForOffNadir(regime.Altitude, ReachOffNadirDeg * OrbitMath.Deg) * OrbitMath.EarthRadius;

        /// <summary>Longest possible pass (zero cross-track), seconds.</summary>
        public static double WindowSeconds(in OrbitRegime regime) =>
            2.0 * HalfWindow(regime) / OrbitMath.GroundSpeed(regime.Altitude);

        public static double CycleSeconds(in OrbitRegime regime, in OrbitClock clock) =>
            WindowSeconds(regime) + regime.GapSeconds * clock.GapScale;

        public static PassPlan Plan(int seed, in OrbitRegime regime, int index)
        {
            uint hash = Deterministic.Hash(seed, index, regime.Index, 0x5a7);
            bool ascending = (hash & 1u) == 0u;
            double ratio = Math.Cos(regime.InclinationDeg * OrbitMath.Deg) / Math.Cos(TheaterLatitude);
            double baseHeading = Math.Asin(OrbitMath.Clamp(ratio, -1.0, 1.0));
            double heading = ascending ? baseHeading : Math.PI - baseHeading;
            double offset = (Deterministic.UnitFloat(Deterministic.Hash(seed, index, 0x2c1, regime.Index)) * 2.0 - 1.0) *
                            CrossTrackFraction * HalfWindow(regime);
            return new PassPlan(index, ascending, heading, offset);
        }

        /// <summary>Half the along-track length of a pass with this cross-track offset.</summary>
        public static double HalfChord(in OrbitRegime regime, double crossTrack)
        {
            double half = HalfWindow(regime);
            return Math.Sqrt(Math.Max(0.0, half * half - crossTrack * crossTrack));
        }

        public static OrbitState State(int seed, in OrbitRegime regime, in OrbitClock clock, double cycleSeconds)
        {
            double speed = OrbitMath.GroundSpeed(regime.Altitude);
            double maximum = WindowSeconds(regime);
            if (double.IsNaN(cycleSeconds) || cycleSeconds < 0.0)
            {
                PassPlan first = Plan(seed, regime, 0);
                double wait = double.IsNaN(cycleSeconds) ? 0.0 : -cycleSeconds;
                return new OrbitState(OrbitPhase.Hold, first, regime.Altitude, 0.0, 0.0, 0.0, wait, 0.0,
                    2.0 * HalfChord(regime, first.CrossTrack) / speed);
            }

            double cycle = CycleSeconds(regime, clock);
            int index = (int)Math.Min(int.MaxValue - 1, Math.Floor(cycleSeconds / cycle));
            double tau = cycleSeconds - index * cycle;
            PassPlan plan = Plan(seed, regime, index);
            double halfChord = HalfChord(regime, plan.CrossTrack);
            double window = 2.0 * halfChord / speed;

            // A pass with an offset track is shorter than the slot it sits in: it is centred in
            // the slot, so the station reaches the theatre a little later and leaves earlier.
            double lead = (maximum - window) * 0.5;
            double start = lead;
            double end = lead + window;
            if (tau < start || tau >= end)
            {
                bool before = tau < start;
                PassPlan next = before ? plan : Plan(seed, regime, index + 1);
                double nextWindow = before ? window : 2.0 * HalfChord(regime, next.CrossTrack) / speed;
                double nextStart = before ? start - tau : cycle - tau + (maximum - nextWindow) * 0.5;
                return new OrbitState(OrbitPhase.OutOfTheater, next, regime.Altitude, 0.0, 0.0, 0.0, nextStart, 0.0,
                    nextWindow);
            }

            double along = -halfChord + speed * (tau - start);
            double subX = plan.DirX * along + plan.RightX * plan.CrossTrack;
            double subZ = plan.DirZ * along + plan.RightZ * plan.CrossTrack;
            return new OrbitState(OrbitPhase.InPass, plan, regime.Altitude, subX, subZ, tau - start, 0.0, end - tau,
                window);
        }

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
            return new LookAngles(true, ground, elevation, OrbitMath.OffNadir(state.Altitude, elevation),
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
