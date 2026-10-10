using System;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum LanceTier : byte { Snap, Punch, Through, Full }

    /// <summary>
    /// RG-12 LANCE capacitor: holding the trigger charges the bank, releasing fires one slug whose damage, pierce,
    /// heat and recharge scale with charge. Muzzle velocity is constant (the gun sight leads with the asset value),
    /// so charge never changes the slug speed. Pure math; LanceService owns the trigger state.
    /// </summary>
    internal static class LanceCapacitor
    {
        public const float FullChargeSeconds = 2.5f;
        /// <summary>Trigger-up detection: no Fire call for this long means the pilot let go.</summary>
        public const float ReleaseGap = 0.15f;
        public const float MuzzleVelocity = 3000f;
        /// <summary>WeaponInfo asset values at full power. 3007 is deliberately off-round so no other gun matches the signature.</summary>
        public const float AssetPierce = 3007f;
        public const float AssetBlast = 0.6f;
        /// <summary>Slugs at or above this power punch through whole units.</summary>
        public const float ThroughMinPower = 0.78f;
        public const float AiReleaseCharge = 0.85f;
        public const float CyclingSeconds = 0.25f;
        /// <summary>Server tolerance when bounding a client's claimed charge by the time since its bank was ready.</summary>
        public const float ServerSlack = 0.35f;
        /// <summary>A hit claim with no matching release behaves like this weak snap shot.</summary>
        public const float UnmatchedPower = 0.05f;
        public const float PendingTtl = 4f;

        /// <summary>0..1 charge after holding the trigger this long.</summary>
        public static float Charge(float holdSeconds) => Mathf.Clamp01(holdSeconds / FullChargeSeconds);

        /// <summary>0..1 shot power from charge, tapered at both ends.</summary>
        public static float Power01(float charge01)
        {
            float c = Mathf.Clamp01(charge01);
            return c * c * (3f - 2f * c);
        }

        public static LanceTier TierOf(float charge01) =>
            charge01 < 0.25f ? LanceTier.Snap : charge01 < 0.70f ? LanceTier.Punch : charge01 < 0.995f ? LanceTier.Through : LanceTier.Full;

        public static float Pierce(float power01) => 180f + 2827f * power01;
        public static float Blast(float power01) => 0.05f + 0.55f * power01 * power01;
        public static float Tracer(float power01) => 0.6f + 0.5f * power01;

        /// <summary>Bank recharge after firing at this power, before the next charge can start.</summary>
        public static float RechargeSeconds(float power01) => 1f + 2.5f * power01;

        /// <summary>Highest charge a client can honestly have after its bank became ready this many seconds ago.</summary>
        public static float ServerChargeBound(float secondsSinceReady) => Charge(secondsSinceReady + ServerSlack);

        /// <summary>Back-shove (m/s) and nose-up spin (rad/s) the owner's airframe takes at release.</summary>
        public static void Kick(float power01, out float backSpeed, out float noseUp)
        {
            backSpeed = 0.10f + 0.55f * power01;
            noseUp = 0.04f + 0.22f * power01;
        }

        /// <summary>Camera shake (low, high) at release.</summary>
        public static void Shake(float power01, out float low, out float high)
        {
            low = 0.25f + 0.9f * power01;
            high = 0.40f + 1.4f * power01;
        }

        /// <summary>True when a WeaponInfo's damage triple is the Lance asset (the ArmorPenetrate patch keys on it).</summary>
        public static bool IsSignature(float pierce, float muzzleVelocity, float blast) =>
            Math.Abs(pierce - AssetPierce) < 0.01f && Math.Abs(muzzleVelocity - MuzzleVelocity) < 0.01f && Math.Abs(blast - AssetBlast) < 0.001f;

        /// <summary>Hermite smoothstep, shared by the Lance domain files.</summary>
        public static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
