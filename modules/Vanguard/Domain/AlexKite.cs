using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Wire values (AlexState.State): do not renumber.</summary>
    internal enum KiteState : byte { Deploy = 0, Towed = 1, Free = 2, Gone = 3 }

    /// <summary>Why the fiber parted (AlexState.Reason): do not renumber.</summary>
    internal enum KiteCut : byte { None = 0, Pilot = 1, Snap = 2, Low = 3, Replaced = 4, Auto = 5 }

    /// <summary>What one WeaponManager.Fire call does while the ALE-X station is selected.</summary>
    internal enum PodPress { Launch, Cut, Swallow }

    /// <summary>ALE-X KITE rules that are not tow geometry (see TowedTrail): layers, the cut, HUD words, pod and fin animation.</summary>
    internal static class AlexKite
    {
        public const string InfoName = "WI_VG_AleX"; // WeaponInfo asset name of the ALE-X pod's decoy
        public const string MountKey = "VG_AleX_Pod"; // WeaponMount jsonKey of the two-decoy pod
        public const float FreeSeconds = 20f;       // a cut decoy keeps radiating, then self-destructs
        public const float FreeFloor = 0.3f;        // it decelerates to this fraction of the host speed it was cut at
        public const float DeflectRange = 8000f;
        public const float DeflectPull = 0.4f;      // aimpoint moved this far toward the decoy
        public const float SuppressSlow = 1.5f;     // turret locks on the host take 50 % longer
        public const float AiCutDelay = 1f;         // an AI host cuts this long after its first capture
        public const float AiDeployRange = 14000f;  // an AI host with a pod deploys when a radar missile is this close
        public const byte PipSup = 1, PipDef = 2, PipSed = 4;

        // ---- layers

        public static bool Deflects(string seekerType, float range) =>
            SeductionRule.IsRadarSeeker(seekerType) && range <= DeflectRange;

        /// <summary>How far the aimpoint moves, given the vector from it to the decoy.</summary>
        public static Vector3 DeflectShift(Vector3 toDecoy) => toDecoy * DeflectPull;

        /// <summary>Lock time given back per physics tick so a lock accrues at 1/SuppressSlow of real time.</summary>
        public static float SuppressRefund(float dt) => dt * (1f - 1f / SuppressSlow);

        public static byte PipMask(bool suppress, bool deflect, bool seduce) =>
            (byte)((suppress ? PipSup : 0) | (deflect ? PipDef : 0) | (seduce ? PipSed : 0));

        // ---- the cut

        public static float FreeSpeedFactor(float secondsFree) => Mathf.Lerp(1f, FreeFloor, Mathf.Clamp01(secondsFree / FreeSeconds));

        public static bool AiCuts(int captured, float sinceFirstCapture) => captured > 0 && sinceFirstCapture >= AiCutDelay;

        public static bool AiDeploys(float threatRange) => threatRange <= AiDeployRange;

        // ---- the fire press

        public const float PressGap = 0.25f; // a held trigger calls WeaponManager.Fire every frame; a new press follows this much silence

        /// <summary>True when a Fire call starts a new press: the trigger was up for longer than <see cref="PressGap"/> (or one long frame).</summary>
        public static bool FreshPress(float sinceLastCall, float frameSeconds) => sinceLastCall > Mathf.Max(PressGap, 2f * frameSeconds);

        /// <summary>A held trigger acts once, on its first call; every later call of the hold is swallowed.</summary>
        public static PodPress Press(bool fresh, bool towing) => !fresh ? PodPress.Swallow : towing ? PodPress.Cut : PodPress.Launch;

        // ---- HUD words (vg.alex)

        public static string PipText(byte mask) =>
            ((mask & PipSup) != 0 ? "SUP" : "---") + " " + ((mask & PipDef) != 0 ? "DEF" : "---") + " " + ((mask & PipSed) != 0 ? "SED" : "---");

        public static string StatusText(byte mask) => "KITE  " + PipText(mask);

        public static string LineDetail(float line, int captured, bool canCut)
        {
            string text = "LINE " + Mathf.RoundToInt(line) + " m";
            if (captured > 0) text += "  ·  " + captured + " ON KITE" + (canCut ? "  ·  FIRE TO CUT" : "");
            return text;
        }

        /// <summary>0 quiet, 1 layers working, 2 a radar missile is in seduce range and not on the kite yet.</summary>
        public static int Severity(byte mask, int captured) =>
            (mask & PipSed) != 0 && captured == 0 ? 2 : mask != 0 || captured > 0 ? 1 : 0;

        public static string CutText(KiteCut reason, int dragged)
        {
            string head = reason == KiteCut.Snap ? "KITE SNAPPED — OVERLOAD" : reason == KiteCut.Low ? "KITE SNAPPED — TOO LOW" : "KITE CUT";
            return dragged > 0 ? head + " — " + dragged + (dragged == 1 ? " MISSILE" : " MISSILES") + " DRAGGED" : head;
        }

        // ---- pod and body animation (client)

        public const float ConeOpenAngle = 70f;
        public const float ConeSeconds = 0.25f;
        public const float FinPopAngle = 85f;
        public const float FinPopStart = 0.05f, FinPopEnd = 0.35f; // seconds after release
        public const float ReelDegPerMps = 12f;                    // 40 m/s of fiber spins the drum at 480 deg/s

        public static float FinPop(float age) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FinPopStart, FinPopEnd, age));

        /// <summary>Drum spin (deg/s) for a fiber that is growing (+) or shrinking (-) at lineRate m/s; still below 1 m/s.</summary>
        public static float ReelSpin(float lineRate) =>
            Mathf.Abs(lineRate) < 1f ? 0f : Mathf.Clamp(lineRate, -TowedTrail.ReelSpeed * 1.5f, TowedTrail.ReelSpeed * 1.5f) * ReelDegPerMps;

        /// <summary>0..1 strobe brightness: a short flash each period, faster once the decoy is cut loose.</summary>
        public static float Strobe(float time, bool free)
        {
            float period = free ? 0.35f : 1f;
            return time % period < 0.09f ? 1f : 0.06f;
        }

        /// <summary>
        /// Which way (+1/-1) a stowed fin must turn about the body axis so its mass ends up farther along
        /// <paramref name="outward"/>. The FBX holds the stowed pose; this finds the pop direction from it.
        /// </summary>
        public static float PopSign(Vector3 pivotToCentroid, Vector3 axis, Vector3 outward)
        {
            if (pivotToCentroid.sqrMagnitude < 1e-6f) return 1f;
            float plus = Vector3.Dot(Rotate(pivotToCentroid, axis, FinPopAngle), outward);
            float minus = Vector3.Dot(Rotate(pivotToCentroid, axis, -FinPopAngle), outward);
            return plus >= minus ? 1f : -1f;
        }

        // Rodrigues; equals Quaternion.AngleAxis(angle, axis) * v without the native call (the domain tests run outside Unity).
        internal static Vector3 Rotate(Vector3 v, Vector3 axis, float degrees)
        {
            Vector3 k = axis.normalized;
            float rad = degrees * Mathf.Deg2Rad;
            return v * Mathf.Cos(rad) + Vector3.Cross(k, v) * Mathf.Sin(rad) + k * (Vector3.Dot(k, v) * (1f - Mathf.Cos(rad)));
        }
    }
}
