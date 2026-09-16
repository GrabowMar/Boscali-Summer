using System;
using System.Globalization;

namespace BoscaliSummer.Features.Radio.Runtime
{
    /// <summary>
    /// One reception result for a tuned frequency: where the signal came from, how far it
    /// had to travel, what stood in the way, and what a receiver would hear.
    /// </summary>
    internal readonly struct RadioReception
    {
        public static readonly RadioReception Perfect = new RadioReception(1f, 0f, 0f, 0f, -40f);

        public float Quality { get; }
        public float DistanceKm { get; }
        public float HorizonKm { get; }
        public float SignalDbm { get; }
        public float PenaltyDb { get; }

        public RadioReception(float quality, float distanceKm, float horizonKm,
            float penaltyDb, float signalDbm)
        {
            Quality = quality;
            DistanceKm = distanceKm;
            HorizonKm = horizonKm;
            PenaltyDb = penaltyDb;
            SignalDbm = signalDbm;
        }

        public bool BeyondHorizon => HorizonKm > 0f && DistanceKm > HorizonKm;
        public bool Blocked => PenaltyDb > 0f;
        public bool Open(float squelch) => Quality >= squelch;

        /// <summary>S-units: S1..S9, then the over-nine decibels, as a receiver reports it.</summary>
        public float SUnits => Quality * 9f;

        public string SReport
        {
            get
            {
                if (Quality <= 0.01f) return "S0";
                int s = Math.Max(1, (int)Math.Round(SUnits));
                if (s <= 9) return "S" + s.ToString(CultureInfo.InvariantCulture);
                return "S9+" + (int)Math.Round((SUnits - 9f) * 10f);
            }
        }

        public string DbmReport => SignalDbm.ToString("0", CultureInfo.InvariantCulture) + " dBm";
    }

    /// <summary>
    /// A deliberately small, local copy of the NORS propagation model: a link budget with
    /// free-space loss, the radio horizon over a spherical earth, and a terrain penalty when
    /// something blocks the path. AM holds up farther and over ground, FM tolerates blockage
    /// better, and a mode mismatch on the same frequency is garble rather than silence.
    ///
    /// <para>Pure maths on purpose: the caller supplies positions and the line-of-sight
    /// answer, so this is testable without a mission running. Voice, crypto, jamming and
    /// the per-transmitter network live in <see cref="RadioLinkStub"/> for a later pass.</para>
    /// </summary>
    internal static class RadioPropagation
    {
        /// <summary>4.12 * (sqrt(tx metres) + sqrt(rx metres)) km, the usual VHF/UHF horizon.</summary>
        public const float HorizonKilometreFactor = 4.12f;

        public const float TransmitPowerDbm = -18f;
        public const float NoiseFloorDbm = -115f;
        public const float ReferenceLossDb = 32f;
        public const float HorizonPenaltyDb = 18f;
        public const float TerrainPenaltyAmDb = 26f;
        public const float TerrainPenaltyFmDb = 11f;
        public const float ModeMismatchPenaltyDb = 22f;

        /// <summary>Signal-to-noise at which the meter tops out and audio is clean.</summary>
        public const float FullScaleSnrDb = 45f;

        public static float HorizonKilometres(float txMetres, float rxMetres)
        {
            float tx = (float)Math.Sqrt(Math.Max(0f, txMetres));
            float rx = (float)Math.Sqrt(Math.Max(0f, rxMetres));
            return HorizonKilometreFactor * (tx + rx);
        }

        public static float FreeSpaceLossDb(float distanceKm) =>
            ReferenceLossDb + 20f * (float)Math.Log10(Math.Max(0.1f, distanceKm));

        /// <summary>
        /// A link budget for one transmitter/receiver pair. <paramref name="lineOfSight"/> is
        /// false when terrain blocks the path; <paramref name="transmit"/> and
        /// <paramref name="receive"/> are the modulation either end is using.
        /// </summary>
        public static RadioReception Evaluate(
            float distanceKm, float txMetres, float rxMetres, bool lineOfSight,
            RadioModulation transmit, RadioModulation receive)
        {
            float horizon = HorizonKilometres(txMetres, rxMetres);
            float penalty = 0f;
            if (distanceKm > horizon) penalty += HorizonPenaltyDb;
            if (!lineOfSight)
                penalty += receive == RadioModulation.Fm ? TerrainPenaltyFmDb : TerrainPenaltyAmDb;
            if (transmit != receive) penalty += ModeMismatchPenaltyDb;

            float signal = TransmitPowerDbm - FreeSpaceLossDb(distanceKm) - penalty;
            float snr = signal - NoiseFloorDbm;
            float quality = Clamp01(snr / FullScaleSnrDb);
            return new RadioReception(quality, distanceKm, horizon, penalty, signal);
        }

        /// <summary>Reception with no transmitter to measure: a local deck or unknown tower.</summary>
        public static RadioReception Local() => RadioReception.Perfect;

        /// <summary>No transmitter at all: a built-in station whose tower is gone.</summary>
        public static RadioReception OffAir => new RadioReception(0f, 0f, 0f, 0f, NoiseFloorDbm);

        /// <summary>Garble from a wrong-mode transmission, used by the mode override.</summary>
        public static bool ModeMismatch(RadioModulation transmit, RadioModulation receive) =>
            transmit != receive;

        public static float StaticFor(float quality) =>
            Clamp01(1f - Math.Max(0f, quality) * 1.35f);

        private static float Clamp01(float value) =>
            value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
