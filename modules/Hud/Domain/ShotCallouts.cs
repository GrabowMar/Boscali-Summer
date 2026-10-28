using System;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>A shot callout under the reticle, lightest first: a heavier call is not cut short by a lighter one.</summary>
    internal enum CalloutKind : byte { None, Missed, Decoyed, Hit, Destroyed }

    /// <summary>
    /// The SHOT CALLOUT under the reticle (2026-10-28), pure: HIT / DESTROYED / MISSED / DECOYED the moment the shot tracker resolves a weapon or the game credits a kill.
    /// One call at a time: pop in (1.25 to 1 in <see cref="PopS"/>), hold, fade out. A lighter call does not cut a heavier one younger than <see cref="GuardS"/>
    /// (the HIT and the kill credit of one missile arrive together: DESTROYED wins); kills in a row count up (DESTROYED x2).
    /// </summary>
    internal sealed class ShotCallouts
    {
        public const float PopS = 0.12f, HoldS = 1.5f, FadeS = 0.3f, LifeS = PopS + HoldS + FadeS, GuardS = 1f;

        private static readonly string[] Words = { "", "MISSED", "DECOYED", "HIT", "DESTROYED" };
        private static readonly AvIcon[] Icons = { AvIcon.None, AvIcon.Circle, AvIcon.Unlink, AvIcon.Target, AvIcon.Burst };

        public CalloutKind Kind { get; private set; }
        /// <summary>The line under the word (the unit destroyed), null for none.</summary>
        public string Sub { get; private set; }
        public float At { get; private set; } = float.NegativeInfinity;
        /// <summary>Kills in this run (DESTROYED shown while the last one still showed).</summary>
        public int Streak { get; private set; }
        /// <summary>Bumped on every call taken: the presenter rewrites its text only then.</summary>
        public int Version { get; private set; }

        public void Push(CalloutKind kind, string sub, float now)
        {
            if (kind == CalloutKind.None) return;
            bool live = Live(now);
            if (live && kind < Kind && now - At < GuardS) return;
            Streak = kind == CalloutKind.Destroyed ? (live && Kind == CalloutKind.Destroyed ? Streak + 1 : 1) : 0;
            Kind = kind;
            Sub = sub;
            At = now;
            Version++;
        }

        public bool Live(float now) => Kind != CalloutKind.None && now - At < LifeS;

        public void Clear()
        {
            Kind = CalloutKind.None;
            Streak = 0;
            Version++;
        }

        public static string Word(CalloutKind kind) => Words[(int)kind];

        /// <summary>The call's picture beside its word: a target for a HIT, a burst for a kill, an empty ring for a MISS, a broken link for a decoyed weapon.</summary>
        public static AvIcon Icon(CalloutKind kind) => Icons[(int)kind];

        public static CalloutKind Of(ShotState state) =>
            state == ShotState.Hit ? CalloutKind.Hit : state == ShotState.Miss ? CalloutKind.Missed : state == ShotState.Defeated ? CalloutKind.Decoyed : CalloutKind.None;

        public static float Alpha(float age) => age < LifeS - FadeS ? 1f : Math.Max(0f, (LifeS - age) / FadeS);

        public static float Scale(float age) => age >= PopS ? 1f : 1.25f - 0.25f * Math.Max(0f, age) / PopS;
    }
}
