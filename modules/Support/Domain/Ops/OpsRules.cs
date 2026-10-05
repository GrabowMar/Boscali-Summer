using System;

namespace BoscaliSummer.Modules.Support.Domain.Ops
{
    /// <summary>The two domains that run OPERATIONS. Wire value (1 bit); never renumber.</summary>
    internal enum OpDomain : byte { Cyber = 0, Sof = 1 }

    /// <summary>The three operations (core 7a, CYBER/SOF spec 3). Wire value (2 bits); never renumber.</summary>
    internal enum OpKind : byte { None = 0, Asat = 1, ZeroDay = 2, Fob = 3 }

    /// <summary>Operation lifecycle. Wire value (3 bits). BROKEN is a state with a bar at 50 % that FUND resumes.</summary>
    internal enum OpState : byte { Idle = 0, Funding = 1, NeedsFunding = 2, Execute = 3, Done = 4, Broken = 5 }

    /// <summary>What an enemy is told. Half = the bar passed 50 %, Execute = the countdown started, Launch = an ASAT left the rail, Loss = a satellite died.</summary>
    internal enum OpPingPhase : byte { Half = 0, Execute = 1, Launch = 2, Loss = 3 }

    /// <summary>Spec 3 + core 7a numbers in one place. Everything is mission seconds and whole CR.</summary>
    internal static class OpsRules
    {
        public const int FundSmall = 25, FundLarge = 50, HopWork = 8, SofWork = 40, MaxContributors = 16, MaxPings = 6;
        public const float WorkCap = 0.5f, PlayerCap = 0.3f, StallSeconds = 600f, CountdownSeconds = 60f, AsatFlightSeconds = 60f;
        public const float ZeroDaySeconds = 180f, ZeroDayHalvedSeconds = 90f, EwHalveMetres = 18000f, FobSeconds = 1200f;
        public const float DoneLingerSeconds = 45f, CooldownSeconds = 480f, CounterTraceFraction = 0.10f, BrokenRefundFraction = 0.5f, HalfFraction = 0.5f;
        public const float PingSeconds = 90f, LossPingSeconds = 45f;

        public static bool Valid(OpKind kind) => kind == OpKind.Asat || kind == OpKind.ZeroDay || kind == OpKind.Fob;

        public static OpDomain DomainOf(OpKind kind) => kind == OpKind.Fob ? OpDomain.Sof : OpDomain.Cyber;

        public static float BaseGoal(OpKind kind) => kind == OpKind.Asat ? 1500f : kind == OpKind.ZeroDay ? 900f : kind == OpKind.Fob ? 1000f : 0f;

        /// <summary>Core 2b: solo 0.6 x base; otherwise base x (0.5 + n / 16), floor 0.6 x base, at most 1.5 x base from 17 humans. Whole CR, multiple of 5.</summary>
        public static int Goal(OpKind kind, int humans)
        {
            float b = BaseGoal(kind);
            if (b <= 0f) return 0;
            float m = humans <= 1 ? 0.6f : Math.Max(0.6f, 0.5f + humans / 16f);
            if (humans >= 17) m = Math.Min(m, 1.5f);
            return Math.Max(5, (int)Math.Round(b * m / 5f) * 5);
        }

        /// <summary>The same operation may be planned again after this long (renewing a FOB has none).</summary>
        public static float Cooldown(OpKind kind) => kind == OpKind.Fob ? 0f : CooldownSeconds;

        public static bool IsTap(int cr) => cr == FundSmall || cr == FundLarge;

        /// <summary>Seconds a SAM NET FAIL lasts: 3 minutes, halved when an enemy EW truck stands within 18 km of the cluster.</summary>
        public static float ZeroDayEffectSeconds(bool truckNear) => truckNear ? ZeroDayHalvedSeconds : ZeroDaySeconds;

        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
