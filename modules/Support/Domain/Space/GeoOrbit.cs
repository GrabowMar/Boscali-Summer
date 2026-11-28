using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    internal static class GeoOrbit
    {
        private const float EarthRadiusKm = 6371f;
        private const float EarthMu = 398600.44f;
        private const float MissionClockScale = 23.5f;
        private const float ReferenceTheatreKm = 160f;

        // The physical period uses circular orbital elements. A compressed ground-track projection keeps each pass playable on the theatre map.
        public const float TrackRevolutionWidths = 2.8f;
        public const float MinimumNodeChange = 0.01f;
        public const float RephaseStepSeconds = 24f;

        public static float AltitudeKm(BirdKind payload) => payload switch
        {
            BirdKind.Radar => 610f,
            BirdKind.Kinetic => 760f,
            _ => 470f
        };

        public static float InclinationDeg(BirdKind payload) => payload switch
        {
            BirdKind.Radar => 62f,
            BirdKind.Kinetic => 49f,
            _ => 97f
        };

        public static float PeriodSeconds(BirdKind payload)
        {
            float r = EarthRadiusKm + AltitudeKm(payload);
            return 2f * MathF.PI * MathF.Sqrt(r * r * r / EarthMu) / MissionClockScale;
        }

        private static float CircularSpeed(BirdKind payload) => 1000f * MathF.Sqrt(EarthMu / (EarthRadiusKm + AltitudeKm(payload)));

        public static float DeltaVCapacity(SatelliteBus bus) => bus switch
        {
            SatelliteBus.Agile => 1300f,
            SatelliteBus.Wideband => 850f,
            _ => 1000f
        };

        public static float TransferRate(SatelliteBus bus) => bus switch
        {
            SatelliteBus.Agile => 1.35f,
            SatelliteBus.Wideband => 0.75f,
            _ => 1f
        };

        public static float NodeDeltaV(BirdKind payload, float crossWidths)
        {
            float angle = crossWidths * ReferenceTheatreKm / EarthRadiusKm;
            return crossWidths < MinimumNodeChange ? 0f : 12f + 2f * CircularSpeed(payload) * MathF.Sin(angle * 0.5f);
        }

        public static float PhaseDeltaV(BirdKind payload, float shift)
        {
            // Two small burns for a temporary circular reference adjustment, with a compressed coast time.
            float fraction = Math.Abs(shift) / PeriodSeconds(payload);
            return fraction <= 0f ? 0f : CircularSpeed(payload) * fraction * (2f / 3f);
        }

        public static void Direction(BirdKind payload, out float x, out float y)
        {
            float rad = InclinationDeg(payload) * (MathF.PI / 180f);
            x = MathF.Cos(rad); y = -MathF.Sin(rad);
        }

        public static void Components(BirdKind payload, float x, float y, out float along, out float across)
        {
            Direction(payload, out float ux, out float uy);
            along = x * ux + y * uy;
            across = x * uy - y * ux;
        }

        public static GeoBird Launch(BirdKind payload, SatelliteBus bus, float u, float v, float now)
        {
            float epoch = now + 20f + 14f * (byte)payload;
            return new GeoBird(u, v, u, v, now, 100f, bus, payload, epoch, epoch, now, 1);
        }
    }
}
