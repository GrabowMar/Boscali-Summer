using System;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    internal enum RopeMode { FastRope, Rappel }

    internal enum RopePhase { Aboard, Sliding, Landing, Moving, Holding, Riding, Gone }

    /// <summary>One trooper at one instant on a fast-rope insertion or a SPIES extraction.</summary>
    internal struct RopeSample
    {
        public RopePhase Phase;
        public Vector3 Position;
        /// <summary>Horizontal unit heading.</summary>
        public Vector3 Heading;
        /// <summary>0..1 through the current stride, for blending the two walk frames.</summary>
        public float Stride;
        /// <summary>0 brand-new on the ground, 1 recovered from the landing crouch.</summary>
        public float Recover;
    }

    /// <summary>
    /// Ibis rope work, decided from replicated inputs so the server's timing and every peer's
    /// picture agree. Pure: no physics. Insertion: two ropes from the side doors, the squad goes in
    /// pairs (fast-rope up to 45 m, controlled rappel up to 120 m), then files off to its
    /// destinations (roof edges, a building entry, or a perimeter in the open). Extraction (SPIES):
    /// the squad walks to the rope feet, clips in at stepped heights, and is reeled up together.
    /// The ropes hang from the live helicopter, so samples take the current door positions.
    /// </summary>
    internal sealed class FastRopePlan
    {
        public const float MinHeight = 2f;
        public const float FastRopeMaxHeight = 45f;
        public const float RappelMaxHeight = 120f;
        public const float FastRopeSpeed = 6f;
        public const float RappelSpeed = 2.6f;
        public const float LandingSeconds = 0.7f;
        public const float WalkSpeed = 2.4f;
        public const float FileGap = 0.45f;
        public const float HoldSeconds = 6f;
        public const float EnterSeconds = 0.6f;
        public const float ReelSpeed = 3f;
        public const float RideSpacing = 1.8f;
        public const float StrideLength = 1.4f;
        public const int MaxTroops = 8;

        private struct Trooper
        {
            public int Rope, Order;
            public float StartAt, LandAt, MoveAt, ArriveAt;
            public Vector3 Foot, Destination, Source;
        }

        private readonly Trooper[] troopers;
        private readonly bool extraction, enter;
        private readonly float reelAt, reelSeconds;

        public RopeMode Mode { get; }
        public int Count => troopers.Length;
        /// <summary>When the last trooper is off the rope: the server resolves the insertion then.</summary>
        public float DescentSeconds { get; }
        public float TotalSeconds { get; }

        /// <summary>Insertion mode for a hover <paramref name="height"/> above the surface, or null if out of reach.</summary>
        public static RopeMode? ModeFor(float height)
        {
            if (!(height >= MinHeight) || height > RappelMaxHeight) return null;
            return height <= FastRopeMaxHeight ? RopeMode.FastRope : RopeMode.Rappel;
        }

        private FastRopePlan(Trooper[] troopers, RopeMode mode, bool extraction, bool enter, float descent, float reelAt, float reelSeconds, float total)
        {
            this.troopers = troopers;
            Mode = mode;
            this.extraction = extraction;
            this.enter = enter;
            DescentSeconds = descent;
            this.reelAt = reelAt;
            this.reelSeconds = reelSeconds;
            TotalSeconds = total;
        }

        /// <summary>When the last of <paramref name="count"/> troopers is off the rope; the server's clock for the outcome.</summary>
        public static float InsertionSeconds(RopeMode mode, float height, int count)
        {
            float slide = height / (mode == RopeMode.FastRope ? FastRopeSpeed : RappelSpeed);
            float gap = mode == RopeMode.FastRope ? 1.4f : slide * 0.6f + 1f;
            int last = Mathf.Clamp(count, 1, MaxTroops) - 1;
            return (last / 2) * gap + (last % 2) * 0.25f + slide;
        }

        /// <param name="height">Door height above the rope feet at release.</param>
        /// <param name="feet">Ground (or roof) point under each of the two ropes.</param>
        /// <param name="destinations">Where each trooper heads once off the rope (one per trooper).</param>
        /// <param name="enter">Destinations are a building entry: troopers go inside instead of holding.</param>
        public static FastRopePlan Insertion(RopeMode mode, float height, Vector3[] feet, Vector3[] destinations, bool enter)
        {
            int count = Mathf.Clamp(destinations.Length, 1, MaxTroops);
            float slide = height / (mode == RopeMode.FastRope ? FastRopeSpeed : RappelSpeed);
            // Fast-ropers follow one another down a rope; a rappeller waits for the rope to clear.
            float gap = mode == RopeMode.FastRope ? 1.4f : slide * 0.6f + 1f;
            var troopers = new Trooper[count];
            float descent = 0f, total = 0f;
            for (int i = 0; i < count; i++)
            {
                int rope = i % 2, order = i / 2;
                float start = order * gap + rope * 0.25f;
                float land = start + slide;
                // Leaders peel off first; the file keeps a step between troopers.
                float move = land + LandingSeconds + order * FileGap;
                Vector3 foot = feet[rope];
                float arrive = move + Flat(destinations[i] - foot).magnitude / WalkSpeed;
                troopers[i] = new Trooper
                {
                    Rope = rope, Order = order, StartAt = start, LandAt = land, MoveAt = move, ArriveAt = arrive,
                    Foot = foot, Destination = destinations[i],
                };
                descent = Math.Max(descent, land);
                total = Math.Max(total, arrive + (enter ? EnterSeconds : HoldSeconds));
            }
            return new FastRopePlan(troopers, mode, false, enter, descent, 0f, 0f, total);
        }

        /// <param name="sources">Where each trooper is when the helicopter arrives (one per trooper).</param>
        public static FastRopePlan Extraction(float height, Vector3[] feet, Vector3[] sources)
        {
            int count = Mathf.Clamp(sources.Length, 1, MaxTroops);
            var troopers = new Trooper[count];
            float clipped = 0f;
            for (int i = 0; i < count; i++)
            {
                int rope = i % 2, order = i / 2;
                float move = order * FileGap;
                float arrive = move + Flat(feet[rope] - sources[i]).magnitude / WalkSpeed;
                troopers[i] = new Trooper
                {
                    Rope = rope, Order = order, MoveAt = move, ArriveAt = arrive,
                    Foot = feet[rope], Source = sources[i],
                };
                clipped = Math.Max(clipped, arrive + 1f);
            }
            float reelSeconds = (height + ((count + 1) / 2) * RideSpacing) / ReelSpeed;
            return new FastRopePlan(troopers, RopeMode.FastRope, true, false, 0f, clipped, reelSeconds, clipped + reelSeconds + 0.5f);
        }

        /// <summary>How much rope is paid out (0 stowed .. 1 at the feet) at <paramref name="time"/>.</summary>
        public float RopeOut(float time) =>
            extraction && time >= reelAt ? Mathf.Clamp01(1f - (time - reelAt) / Math.Max(0.1f, reelSeconds)) : 1f;

        /// <summary>True once the insertion ropes are released to fall away.</summary>
        public bool RopesCut(float time) => !extraction && time > DescentSeconds + 1f;

        public RopeSample Sample(int index, float time, Vector3 doorLeft, Vector3 doorRight)
        {
            Trooper t = troopers[index];
            Vector3 door = t.Rope == 0 ? doorLeft : doorRight;
            var s = new RopeSample { Recover = 1f };
            return extraction ? SampleExtraction(t, time, door, s) : SampleInsertion(t, time, door, s);
        }

        private RopeSample SampleInsertion(Trooper t, float time, Vector3 door, RopeSample s)
        {
            s.Heading = Heading(t.Destination - t.Foot, Heading(t.Foot - door, Vector3.forward));
            if (time < t.StartAt)
            {
                s.Phase = RopePhase.Aboard;
                s.Position = door;
                return s;
            }
            if (time < t.LandAt)
            {
                float u = (time - t.StartAt) / (t.LandAt - t.StartAt);
                if (Mode == RopeMode.Rappel)
                    u = Mathf.Clamp01(u + 0.025f * Mathf.Sin(u * Mathf.PI * 7f) * (1f - u)); // bounding down in the harness
                s.Phase = RopePhase.Sliding;
                s.Position = Vector3.Lerp(door, t.Foot, u);
                return s;
            }
            if (time < t.MoveAt)
            {
                s.Phase = RopePhase.Landing;
                s.Position = t.Foot;
                s.Recover = Mathf.Clamp01((time - t.LandAt) / LandingSeconds);
                return s;
            }
            if (time < t.ArriveAt)
            {
                float u = (time - t.MoveAt) / Math.Max(0.01f, t.ArriveAt - t.MoveAt);
                s.Phase = RopePhase.Moving;
                s.Position = Vector3.Lerp(t.Foot, t.Destination, u);
                s.Stride = Frac((time - t.MoveAt) * WalkSpeed / StrideLength);
                return s;
            }
            s.Position = t.Destination;
            bool inside = enter && time > t.ArriveAt + EnterSeconds;
            s.Phase = inside || time > TotalSeconds ? RopePhase.Gone : RopePhase.Holding;
            return s;
        }

        private RopeSample SampleExtraction(Trooper t, float time, Vector3 door, RopeSample s)
        {
            s.Heading = Heading(t.Foot - t.Source, Vector3.forward);
            if (time < t.MoveAt)
            {
                s.Phase = RopePhase.Holding;
                s.Position = t.Source;
                return s;
            }
            if (time < t.ArriveAt)
            {
                float u = (time - t.MoveAt) / Math.Max(0.01f, t.ArriveAt - t.MoveAt);
                s.Phase = RopePhase.Moving;
                s.Position = Vector3.Lerp(t.Source, t.Foot, u);
                s.Stride = Frac((time - t.MoveAt) * WalkSpeed / StrideLength);
                return s;
            }
            // Clipped on: wait for the rest, then ride the reeling rope up into the door.
            Vector3 bottom = Vector3.Lerp(door, t.Foot, RopeOut(time));
            Vector3 span = door - bottom;
            Vector3 up = span.sqrMagnitude > 0.01f ? span.normalized : Vector3.up;
            Vector3 position = bottom + up * Math.Min(t.Order * RideSpacing, span.magnitude);
            if (time >= TotalSeconds - 0.5f || (time > reelAt && (door - position).magnitude < 1f))
            {
                s.Phase = RopePhase.Gone;
                s.Position = door;
                return s;
            }
            s.Phase = RopePhase.Riding;
            float sway = time > reelAt ? 0.35f : 0f; // a slow pendulum under the rotor wash once airborne
            s.Position = position + new Vector3(Mathf.Sin(time * 1.3f + t.Order), 0f, Mathf.Cos(time * 1.1f + t.Rope)) * sway;
            return s;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static Vector3 Heading(Vector3 v, Vector3 fallback)
        {
            Vector3 f = Flat(v);
            return f.sqrMagnitude > 0.01f ? f.normalized : fallback;
        }

        private static float Frac(float x) => x - Mathf.Floor(x);
    }
}
