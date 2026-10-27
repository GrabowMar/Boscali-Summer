using System;

namespace BoscaliSummer.Modules.Spooktober.Kaiju.Domain
{
    /// <summary>What the kaiju is doing; every action but Rise has an animator state of the same name
    /// (clips made by modules/Spooktober/Kaiju/Assets/Editor/KaijuAssetsBuilder.cs).</summary>
    internal enum KaijuAction : byte { Rise, Walk, Idle, Roar, LaserCharge, LaserFire, Spike, SwipeL, SwipeR, Flinch, Death }

    internal enum Weakspot : byte { Throat, Eye, Sac, Shoulder, Elbow, Wrist, Knee, Ankle, Tail, Heart }

    /// <summary>What is still standing, counted by the host from the live hardpoint units.</summary>
    internal struct KaijuDamage
    {
        public bool Throat, WristL, WristR;
        public int Eyes, Sacs, Knees, Broken, Total;
        public bool HeartExposed => Broken >= KaijuRules.HeartGuard;
        public bool Raging => Total > 0 && Broken * 2 >= Total;
    }

    /// <summary>The nearest live aircraft as the chest sees it.</summary>
    internal struct KaijuSense
    {
        public float Distance; // metres, PositiveInfinity when there is none
        public float Bearing;  // degrees off the nose, + to the right
    }

    /// <summary>
    /// Host fight rules: the next attack, cooldowns, and what each lost weak spot takes away. Pure (pinned by
    /// tests/BoscaliSummer.Tests/Features/Kaiju); distances assume the 15x model scale.
    /// </summary>
    internal sealed class KaijuRules
    {
        public const float Scale = 15f;
        public const float WalkSpeed = 40f;
        public const float TurnRate = 4f;
        public const float ClawReach = 700f;
        public const float LaserRange = 9000f;
        public const float SpikeRange = 8000f;
        public const float LaserFireSeconds = 5f;
        public const int HeartGuard = 12;

        private float nextLaser = 8f, nextSpike = 4f, nextSwipe, nextRoar = 30f;

        public KaijuAction Choose(float now, KaijuSense s, KaijuDamage d)
        {
            bool right = s.Bearing >= 0f;
            if (s.Distance <= ClawReach && now >= nextSwipe && (right ? d.WristR : d.WristL))
                return right ? KaijuAction.SwipeR : KaijuAction.SwipeL;
            if (d.Throat && s.Distance > ClawReach && s.Distance <= LaserRange && Math.Abs(s.Bearing) <= 70f && now >= nextLaser)
                return KaijuAction.LaserCharge;
            if (d.Sacs > 0 && s.Distance <= SpikeRange && now >= nextSpike) return KaijuAction.Spike;
            if (s.Distance <= SpikeRange && now >= nextRoar) return KaijuAction.Roar;
            return KaijuAction.Walk;
        }

        public void Commit(KaijuAction action, float now, KaijuDamage d)
        {
            float k = d.Raging ? 0.65f : 1f;
            switch (action)
            {
                case KaijuAction.SwipeL:
                case KaijuAction.SwipeR: nextSwipe = now + 6f * k; break;
                case KaijuAction.LaserCharge:
                    nextLaser = now + 24f * k;
                    nextSpike = Math.Max(nextSpike, now + 9f);
                    break;
                case KaijuAction.Spike: nextSpike = now + 14f * k; break;
                case KaijuAction.Roar: nextRoar = now + 50f; break;
            }
        }

        /// <summary>Broken knees slow it to a crawl.</summary>
        public static float Speed(KaijuDamage d) => WalkSpeed * (d.Knees >= 2 ? 1f : d.Knees == 1 ? 0.7f : 0.4f);

        /// <summary>
        /// Seconds the beam aims behind its target: a jet crossing the beam outruns the lag, one flying straight
        /// at or away from the kaiju does not. Blinded, it lags further and is easier to slip.
        /// </summary>
        public static float LaserLag(KaijuDamage d) => d.Eyes >= 2 ? 0.9f : d.Eyes == 1 ? 1.4f : 2.2f;

        /// <summary>Fastest the beam can sweep, deg/s.</summary>
        public const float LaserSweepRate = 25f;

        public static int SpikesPerVolley(KaijuDamage d) => d.Sacs * (d.Raging ? 2 : 1);
    }
}
