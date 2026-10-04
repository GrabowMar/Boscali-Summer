using System;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    internal enum StationPhase : byte { Cutoff, PreSlot, InSlot, Behind }

    /// <summary>Station Board's per-member numbers (spec 2026-10-04 §4.1 MemberStation): phase from the rejoin blend, closure,
    /// ETA, and the byte forms the snapshot carries to clients.</summary>
    internal static class StationMath
    {
        public const float InSlotMetres = 15f;

        public static StationPhase Phase(float sigma, bool fallingBehind, float errorM)
        {
            if (fallingBehind) return StationPhase.Behind;
            if (sigma >= 0.999f && errorM <= InSlotMetres) return StationPhase.InSlot;
            return sigma < 0.34f ? StationPhase.Cutoff : StationPhase.PreSlot;
        }

        public static float Closure(float prevErrorM, float errorM, float dtSeconds) =>
            dtSeconds <= 0f ? 0f : (prevErrorM - errorM) / dtSeconds;

        public static float EtaSeconds(float errorM, float closureMps) =>
            !(closureMps > 0.1f) ? float.PositiveInfinity : Math.Max(0f, errorM) / closureMps;

        /// <summary>Slot error in 10 m steps, 0-2550 m; NaN reads 0.</summary>
        public static byte QuantiseError(float m) => float.IsNaN(m) ? (byte)0 : (byte)Math.Max(0, Math.Min(255, Math.Round(m / 10f)));

        public static float Error(byte q) => q * 10f;

        /// <summary>Closure in whole m/s, ±127 (+ = nearing the slot); NaN reads 0.</summary>
        public static sbyte QuantiseClosure(float mps) => float.IsNaN(mps) ? (sbyte)0 : (sbyte)Math.Max(-127, Math.Min(127, Math.Round(mps)));

        public static string Word(StationPhase p) =>
            p == StationPhase.InSlot ? "IN SLOT" : p == StationPhase.PreSlot ? "PRE-SLOT" : p == StationPhase.Behind ? "BEHIND" : "CUTOFF";
    }
}
