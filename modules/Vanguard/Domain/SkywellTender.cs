using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// What the SKYWELL tender is doing, host-authoritative and published to every peer. Stowed -> Deploying -> Tow -> Approach ->
    /// Station -> Service -> Return -> Tow ... -> Stowing -> Stowed. Stowing is Deploying run backwards.
    /// </summary>
    internal enum TenderState : byte { Stowed = 0, Deploying = 1, Tow = 2, Approach = 3, Station = 4, Service = 5, Return = 6, Stowing = 7 }

    /// <summary>
    /// The SKYWELL tender: a winged VTOL drone on a tether (cable + fuel hose) from a deck winch. Pure maths for every peer's
    /// animation and for the host's timing: the deploy timeline, the critically damped follow in the tanker's frame, where the
    /// working joints sit under a receiver, the tether curve, the hand-off clock and the magazine. Nothing here moves a receiver.
    /// </summary>
    internal static class SkywellTender
    {
        public const float DeploySeconds = 9f;     // deck out, tender off the deck, wings and nacelles unfolded, fans up, tether paid out to the tow position
        public const float DeckSlide = 3.2f;       // deck cradle travel aft
        public const float RollOff = 1.6f;         // the tender rolls this far further aft off the deck before it flies
        public const float Under = 3.5f;           // the tender holds station this far under the receiver's belly
        public const float BellyClear = 1.8f;      // the working joints are never closer than this to the belly
        public const float ArmClearance = 2.5f;    // hand-off: loader shoulder this far under the pylon
        public const float FuelClearance = 3.0f;   // refuel: boom hinge this far under the receptacle
        public const float MaxRelSpeed = 12f;      // closing speed on the receiver, m/s
        public const float SmoothSeconds = 0.45f;  // critically damped follow
        public const float TurnDegPerSecond = 90f;
        public const float ReturnSpeed = 14f;      // m/s, deck frame: tow and return
        public const float HotSpeed = 28f;         // m/s snapping back to the tow position after a hot break
        public const float ReachGap = 1.5f;        // the loader and boom work only once the tender is this close to its pose
        public const float TargetSmoothSeconds = 0.1f; // low-pass on a replicated (remote) receiver's pose
        public const float StationSeconds = 0.8f;  // settle on station before the first job starts
        public const float DrumRadius = 0.2f;      // winch drum, m: the drum turns length / radius radians

        // ---- states

        /// <summary>The kit is out (anything but stowed): it holds the ramp open and the HUD shows it.</summary>
        public static bool Deployed(TenderState s) => s != TenderState.Stowed;

        /// <summary>The tender is flying on its tether, not on the deck.</summary>
        public static bool Flying(TenderState s) => s >= TenderState.Tow && s <= TenderState.Return;

        /// <summary>The tender is working a receiver (or on its way to one).</summary>
        public static bool Serving(TenderState s) => s >= TenderState.Approach && s <= TenderState.Service;

        /// <summary>The deck is moving: toggles are locked until it is done.</summary>
        public static bool Moving(TenderState s) => s == TenderState.Deploying || s == TenderState.Stowing;

        // ---- deploy timeline (p: 0 stowed .. 1 deployed)

        /// <param name="elapsed">Seconds since the state began.</param>
        public static float Progress(TenderState s, float elapsed)
        {
            switch (s)
            {
                case TenderState.Stowed: return 0f;
                case TenderState.Deploying: return Mathf.Clamp01(elapsed / DeploySeconds);
                case TenderState.Stowing: return 1f - Mathf.Clamp01(elapsed / DeploySeconds);
                default: return 1f;
            }
        }

        private static float S(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

        /// <summary>Deck cradle slid out, 0..1 (first quarter).</summary>
        public static float DeckOut(float p) => S(p / 0.25f);

        /// <summary>Extra roll aft off the deck, 0..1.</summary>
        public static float RollAft(float p) => S((p - 0.20f) / 0.15f);

        /// <summary>How much the tender is still held by the deck (1 = on the cradle, 0 = flying free).</summary>
        public static float Attach(float p) => 1f - S((p - 0.30f) / 0.10f);

        /// <summary>Where the free-flying tender is headed between the deck (0) and the tow position (1).</summary>
        public static float TowBlend(float p) => S((p - 0.30f) / 0.45f);

        /// <summary>Wings spread, 0 folded .. 1 spread.</summary>
        public static float Spread(float p) => S((p - 0.45f) / 0.25f);

        /// <summary>Nacelles out of the stowed (forward) tilt, 0..1.</summary>
        public static float NacelleOut(float p) => S((p - 0.55f) / 0.25f);

        /// <summary>Fans spooled up, 0..1.</summary>
        public static float Spool(float p) => S((p - 0.60f) / 0.35f);

        public const float FoldedTilt = 90f;       // nacelle tilt in the stowed pose (forward, flat)

        /// <summary>Nacelle tilt the flight wants, degrees (0 = lift fans vertical, 90 = thrust forward): forward in tow, vertical on station.</summary>
        public static float FlightTilt(TenderState s, float speed)
        {
            switch (s)
            {
                case TenderState.Approach: return Mathf.Clamp01(speed / MaxRelSpeed) * 60f;
                case TenderState.Station:
                case TenderState.Service: return 0f;
                case TenderState.Return: return 60f;
                default: return 75f; // tow (and the deck states, which blend in through NacelleOut)
            }
        }

        public static float Tilt(TenderState s, float p, float speed) => Mathf.Lerp(FoldedTilt, FlightTilt(s, speed), NacelleOut(p));

        /// <summary>Fan power 0..1: spooled by the deploy timeline, harder when the tender is working.</summary>
        public static float FanPower(TenderState s, float p)
        {
            float level = s == TenderState.Approach ? 1f : s == TenderState.Return ? 0.8f : s == TenderState.Station || s == TenderState.Service ? 0.85f : 0.55f;
            return Spool(p) * level;
        }

        // ---- timing

        /// <summary>Seconds the host gives the tender to cover <paramref name="distance"/> to a receiver's belly: the follow's own top speed plus its ease-in.</summary>
        public static float ArrivalSeconds(float distance) => Mathf.Clamp(distance / MaxRelSpeed + 2f, 2.5f, 12f);

        /// <summary>Seconds a tender needs to fly home from <paramref name="from"/> (deck axes) to the tow position.</summary>
        public static float ReturnSeconds(Vector3 from) => Mathf.Clamp(Vector3.Distance(from, SkywellZone.Tow) / ReturnSpeed + 1f, 1.5f, 12f);

        /// <summary>Where a tender that let go of a receiver at <paramref name="from"/> is <paramref name="seconds"/> later, flying home to the tow position.</summary>
        public static Vector3 HomeEstimate(Vector3 from, float seconds) => Vector3.MoveTowards(from, SkywellZone.Tow, ReturnSpeed * Mathf.Max(0f, seconds));

        // ---- follow

        /// <summary>Exponential low-pass (time constant <paramref name="tau"/>) on a pose that arrives with sampling noise.</summary>
        public static Vector3 LowPass(Vector3 previous, Vector3 now, float dt, float tau) =>
            dt <= 0f ? previous : tau <= 1e-4f ? now : Vector3.Lerp(previous, now, 1f - Mathf.Exp(-dt / tau));

        /// <summary>
        /// One frame of the follow, critically damped in the target's own frame: <paramref name="targetFrom"/> is where the target
        /// was last frame, <paramref name="targetTo"/> where it is now (equal on a retarget, so the tender eases onto the new pose
        /// from its current velocity). The closing speed on the target never exceeds <paramref name="maxRel"/>.
        /// </summary>
        public static void Follow(ref Vector3 pos, ref Vector3 vel, Vector3 targetFrom, Vector3 targetTo, float dt, float smooth, float maxRel)
        {
            if (dt <= 1e-5f) return;
            smooth = Mathf.Max(1e-4f, smooth);
            Vector3 targetVel = (targetTo - targetFrom) / dt;
            Vector3 e = pos - targetFrom;       // error in the target's frame
            Vector3 vr = vel - targetVel;       // velocity relative to the target
            float omega = 2f / smooth, x = omega * dt;
            float damp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            Vector3 change = Vector3.ClampMagnitude(e, maxRel * smooth);
            Vector3 hold = e - change;          // the part of the error the speed limit defers
            Vector3 temp = (vr + omega * change) * dt;
            vr = (vr - omega * temp) * damp;
            Vector3 next = hold + (change + temp) * damp;
            if (Vector3.Dot(e, next) < 0f)      // never overshoot the target
            {
                next = Vector3.zero;
                vr = Vector3.zero;
            }
            pos = targetTo + next;
            vel = targetVel + vr;
        }

        // ---- where the working joints sit

        /// <summary>
        /// Height (receiver frame) of a working joint under a point it must reach: <paramref name="want"/> under the target, but never
        /// within <see cref="BellyClear"/> of the belly nor more than 5 m under it, and never farther under than 80 % of the arm's reach.
        /// </summary>
        public static float WorkHeight(float targetY, float bellyY, float reach, float want) =>
            Mathf.Clamp(targetY - Mathf.Min(want, 0.8f * reach), bellyY - 5f, bellyY - BellyClear);

        /// <summary>Distance from a point to a box (0 inside); the tender-to-airframe clearance.</summary>
        public static float BoxDistance(Vector3 point, Vector3 centre, Vector3 extents)
        {
            Vector3 d = point - centre;
            var outside = new Vector3(Mathf.Max(0f, Mathf.Abs(d.x) - extents.x), Mathf.Max(0f, Mathf.Abs(d.y) - extents.y), Mathf.Max(0f, Mathf.Abs(d.z) - extents.z));
            return outside.magnitude;
        }

        // ---- tether

        /// <summary>Pulls <paramref name="pos"/> back onto the sphere the tether can reach around the winch.</summary>
        public static Vector3 ClampTether(Vector3 winch, Vector3 pos, float max)
        {
            Vector3 d = pos - winch;
            return d.sqrMagnitude > max * max ? winch + d.normalized * max : pos;
        }

        /// <summary>Sag of the line at mid-span, m: a taut tow (tension 1) barely sags, a slack tether on station (0) sags a few percent of its length.</summary>
        public static float Sag(float length, float tension) => length * Mathf.Lerp(0.045f, 0.010f, Mathf.Clamp01(tension));

        /// <summary>Bezier control point that gives the line <paramref name="sag"/> metres of droop at mid-span.</summary>
        public static Vector3 SagControl(Vector3 a, Vector3 b, float sag, Vector3 up) => (a + b) * 0.5f - up * (2f * sag);

        /// <summary>
        /// Control point of the tether from the winch <paramref name="a"/> to the tender's nose <paramref name="b"/>: the sag its tension
        /// gives it, deepened under a receiver (<paramref name="under"/> 0..1) to a quarter of the winch's height over the nose. The line
        /// then comes level into the nose from ahead, so it passes below the receiver's nose, never across the airframe. The control
        /// point always lies between the two ends along the chord, so the line cannot double back.
        /// </summary>
        public static Vector3 TetherControl(Vector3 a, Vector3 b, float tension, float under, Vector3 up) =>
            SagControl(a, b, Mathf.Max(Sag(Vector3.Distance(a, b), tension), under * 0.25f * Vector3.Dot(a - b, up)), up);

        public static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t) =>
            (1f - t) * (1f - t) * a + 2f * (1f - t) * t * control + t * t * b;
    }

    /// <summary>The 6-round tender magazine: a rotary launcher about the tender's long axis, one slot per round, served in order.</summary>
    internal static class SkywellMagazine
    {
        public const int Rounds = 6;

        /// <summary>The slot the next hand-off takes its round from (Rounds when empty).</summary>
        public static int NextSlot(int roundsLeft) => Mathf.Clamp(Rounds - roundsLeft, 0, Rounds);

        /// <summary>The slot still holds a round.</summary>
        public static bool Full(int slot, int roundsLeft) => slot >= Rounds - roundsLeft;
    }

    /// <summary>
    /// One loader hand-off, seconds since it began: lift to the pylon 0.8, latch 0.4 (the round is committed at the end of it),
    /// release and lower 0.8, carousel index 0.5. The host commits at <see cref="CommitAt"/>; the loader staging follows this clock.
    /// </summary>
    internal static class SkywellHandoff
    {
        public const float Lift = 0.8f, Latch = 0.4f, Lower = 0.8f, Index = 0.5f;
        public const float Total = Lift + Latch + Lower + Index;  // 2.5
        public const float CommitAt = Lift + Latch;               // 1.2

        private static float S(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

        /// <summary>Gripper along the straight line from the magazine pickup (0) up to the pylon (1).</summary>
        public static float Reach(float t)
        {
            if (t < Lift) return S(t / Lift);
            if (t < Lift + Latch) return 1f;
            return 1f - S((t - Lift - Latch) / Lower);
        }

        /// <summary>The gripper still holds the round (until the latch ends, when it becomes the real missile on the rail).</summary>
        public static bool Carrying(float t) => t < CommitAt;

        /// <summary>Latch push-in, 0..1..0 over the latch window: the round seats on the rail.</summary>
        public static float Seat(float t) => t < Lift || t > CommitAt ? 0f : Mathf.Sin(Mathf.PI * (t - Lift) / Latch);

        /// <summary>Carousel advance toward the next slot, 0..1 over the last 0.5 s.</summary>
        public static float IndexStep(float t) => S((t - (Total - Index)) / Index);

        /// <summary>Refuel boom plugged-in weight: plug, hold through the flow, unplug.</summary>
        public static float Boom(float t, float flowSeconds)
        {
            float held = PitPlan.FuelPlug + flowSeconds;
            return t < held ? S((t - 0.25f) / 0.65f) : 1f - S((t - held) / 0.40f);
        }
    }

    /// <summary>
    /// Rig conventions the runtime animates the model with (the model builder must match them). Every hinge is driven in the tender's
    /// own body frame (x right, y up, z forward, nose toward the TetherPoint, up toward the dorsal arm bases), as a rotation OFF the
    /// stowed pose in the FBX, so the joints' own local axes do not matter. Positive angles turn right-hand about the axis in Unity's
    /// left-handed frame, so a +90 turn about the body's forward axis takes right to up.
    /// </summary>
    internal static class SkywellRig
    {
        /// <summary>Wing fold: stowed, each wing lies aft along the hull side (a vertical pin at its root trailing edge); spread swings it 90 deg out (right wing -90, left +90 about up).</summary>
        public const float WingFoldDeg = 90f;

        /// <summary>Nacelle tilt: stowed, the fan axes point forward (tilt 90); the tilt axis is the body's right axis, and tilt 0 points them up.</summary>
        public const float NacelleStowedTilt = SkywellTender.FoldedTilt;

        public const float FanDegPerSecond = 1200f;    // fan spin at full power, about the (tilted) fan axis, which is the nacelle's stowed forward axis
        public const float MagazineStepDeg = 60f;      // launcher spacing about the body's forward axis when the model's own Round0/Round1 spacing cannot be measured
        public const float TeleTravel = 1.4f;          // fuel boom telescope travel when it cannot be measured, m
        public const float DefaultUpper = 1.5f, DefaultLower = 1.4f; // loader segments when they cannot be measured, m
    }
}
