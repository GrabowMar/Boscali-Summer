using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal enum SuperstructureKind : byte
    {
        /// <summary>A storm wall with a tiered shelf cloud on its leading edge.</summary>
        ShelfLine = 0,
        /// <summary>A tilted cumulonimbus tower with a downwind anvil and mammatus.</summary>
        Supercell = 1,
        /// <summary>A hurricane eyewall: a clear eye inside a stadium of cloud, spiral rain bands.</summary>
        StormEye = 2,
        /// <summary>A downwind train of smooth, stacked lens clouds (mountain wave).</summary>
        Lenticulars = 3,
    }

    /// <summary>One static set-piece cloud formation. Metres in the map frame (x east, z north).</summary>
    internal struct Superstructure
    {
        public SuperstructureKind Kind;
        /// <summary>The console bit that forces this set-piece (see <see cref="Superstructures"/>).</summary>
        public byte Set;
        public float X, Z;
        /// <summary>Radians, math angle from +x toward +z: the line's direction (shelf) or the
        /// anvil's downwind direction (supercell).</summary>
        public float Heading;
        /// <summary>Shelf: half length. Supercell: tower radius.</summary>
        public float Size;
        public float Top;
        /// <summary>0..1: how far the formation has built, from the state's convection.</summary>
        public float Strength;
        /// <summary>Shelf: depth of the storm mass behind the line. Supercell: anvil radius.</summary>
        public float Extent;
    }

    /// <summary>
    /// Scenery storms: at most three static formations placed outside the theater, seen across
    /// it. They never move; the weather state only builds them up or lets them decay, so they
    /// read as distant fronts and storms on the horizon rather than weather you fly through.
    /// </summary>
    internal static class Superstructures
    {
        public const int MaxCount = 5;

        /// <summary>Console bits: force a set-piece on regardless of the weather state. The fog
        /// bank is not a formation; the renderer draws it as a low layer.</summary>
        public const byte SquallLineSet = 1, SupercellSet = 2, DistantCellSet = 4, StormEyeSet = 8,
            LenticularSet = 16, FogBankSet = 32;
        public const byte AllSets = SquallLineSet | SupercellSet | DistantCellSet | StormEyeSet | LenticularSet | FogBankSet;

        public static int Fill(Superstructure[] into, uint layout, StateParams convection,
            float halfX, float halfZ, float prevailingHeading, byte forced = 0,
            bool hasAnchor = false, float anchorX = 0f, float anchorZ = 0f)
        {
            float half = Math.Max(halfX, halfZ);
            int count = 0;
            float conv = Scalar.Clamp01(convection.Convective);

            // A squall line on the upwind horizon, its shelf facing the theater.
            float shelf = Forced(forced, SquallLineSet, WeatherMath.Smoothstep(0.45f, 0.9f, conv));
            if (shelf > 0.001f && count < into.Length)
            {
                float bearing = (prevailingHeading + 180f + WeatherMath.HashRange(layout, 1, 90, 0, -40f, 40f)) * Deg;
                float distance = half + WeatherMath.HashRange(layout, 1, 91, 0, 35000f, 70000f);
                into[count++] = new Superstructure
                {
                    Kind = SuperstructureKind.ShelfLine,
                    Set = SquallLineSet,
                    X = (float)Math.Sin(bearing) * distance,
                    Z = (float)Math.Cos(bearing) * distance,
                    // Math angle (from +x toward +z) of the line's direction: across the bearing.
                    // The shader puts the leading side, the shelf, toward the map centre.
                    Heading = -bearing,
                    Size = WeatherMath.HashRange(layout, 1, 92, 0, 45000f, 70000f),
                    Top = WeatherMath.HashRange(layout, 1, 93, 0, 10500f, 13000f),
                    Extent = WeatherMath.HashRange(layout, 1, 94, 0, 18000f, 30000f),
                    Strength = shelf,
                };
            }

            // A supercell off to one side in storms.
            float cell = Forced(forced, SupercellSet, WeatherMath.Smoothstep(0.55f, 1f, conv));
            if (cell > 0.001f && count < into.Length)
                into[count++] = Tower(layout, 2, SupercellSet, half, 30000f, 75000f, 12500f, 15000f, 5000f, 7000f,
                    14000f, 22000f, prevailingHeading, cell);

            // A lone cumulonimbus far on the horizon whenever the air is convective.
            float distant = Forced(forced, DistantCellSet, WeatherMath.Smoothstep(0.12f, 0.3f, conv));
            if (distant > 0.001f && count < into.Length)
                into[count++] = Tower(layout, 3, DistantCellSet, half, 70000f, 120000f, 9000f, 12000f, 3000f, 5000f,
                    9000f, 14000f, prevailingHeading, distant);

            // Console-only formations. Without an anchor they stand at a default spot of the layout.
            if ((forced & StormEyeSet) != 0 && count < into.Length)
            {
                float bearing = WeatherMath.HashRange(layout, 4, 80, 0, 0f, 360f) * Deg;
                into[count++] = new Superstructure
                {
                    Kind = SuperstructureKind.StormEye,
                    Set = StormEyeSet,
                    X = hasAnchor ? anchorX : (float)Math.Sin(bearing) * half * 0.5f,
                    Z = hasAnchor ? anchorZ : (float)Math.Cos(bearing) * half * 0.5f,
                    Heading = 0f,
                    Size = WeatherMath.HashRange(layout, 4, 81, 0, 9000f, 13000f),   // eye radius
                    Top = WeatherMath.HashRange(layout, 4, 82, 0, 13500f, 15500f),
                    Extent = WeatherMath.HashRange(layout, 4, 83, 0, 11000f, 15000f), // wall width
                    Strength = 1f,
                };
            }
            if ((forced & LenticularSet) != 0 && count < into.Length)
            {
                // Along the wind: the wave train runs downwind of its first crest.
                float windAngle = (90f - prevailingHeading) * Deg;
                float x = hasAnchor ? anchorX : WeatherMath.HashRange(layout, 6, 84, 0, -0.4f, 0.4f) * half;
                float z = hasAnchor ? anchorZ : WeatherMath.HashRange(layout, 6, 85, 0, -0.4f, 0.4f) * half;
                if (hasAnchor && (forced & StormEyeSet) != 0)
                {
                    // Beside a storm eye placed at the same anchor, not inside it.
                    x += (float)Math.Cos(windAngle + Math.PI * 0.5) * 30000f;
                    z += (float)Math.Sin(windAngle + Math.PI * 0.5) * 30000f;
                }
                into[count++] = new Superstructure
                {
                    Kind = SuperstructureKind.Lenticulars,
                    Set = LenticularSet,
                    X = x,
                    Z = z,
                    Heading = windAngle,
                    Size = WeatherMath.HashRange(layout, 6, 86, 0, 5000f, 8000f),     // lens radius
                    Top = WeatherMath.HashRange(layout, 6, 87, 0, 4200f, 5500f),      // lowest lens altitude
                    Extent = WeatherMath.HashRange(layout, 6, 88, 0, 10000f, 15000f), // wave length
                    Strength = 1f,
                };
            }
            return count;
        }

        private static float Forced(byte forced, byte bit, float strength) => (forced & bit) != 0 ? 1f : strength;

        private static Superstructure Tower(uint layout, int id, byte set, float half, float minOut, float maxOut,
            float minTop, float maxTop, float minRadius, float maxRadius, float minAnvil, float maxAnvil,
            float prevailingHeading, float strength)
        {
            float bearing = WeatherMath.HashRange(layout, id, 95, 0, 0f, 360f) * Deg;
            float distance = half + WeatherMath.HashRange(layout, id, 96, 0, minOut, maxOut);
            return new Superstructure
            {
                Kind = SuperstructureKind.Supercell,
                Set = set,
                X = (float)Math.Sin(bearing) * distance,
                Z = (float)Math.Cos(bearing) * distance,
                // Anvils blow downwind: heading is a math angle from +x toward +z.
                Heading = (90f - prevailingHeading) * Deg,
                Size = WeatherMath.HashRange(layout, id, 97, 0, minRadius, maxRadius),
                Top = WeatherMath.HashRange(layout, id, 98, 0, minTop, maxTop),
                Extent = WeatherMath.HashRange(layout, id, 99, 0, minAnvil, maxAnvil),
                Strength = strength,
            };
        }

        private const float Deg = (float)(Math.PI / 180.0);
    }
}
