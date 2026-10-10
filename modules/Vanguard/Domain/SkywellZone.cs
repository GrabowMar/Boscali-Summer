using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// SKYWELL v5 service zone. A friendly receiver that flies loose formation inside it (no docking, nothing ever moves the
    /// receiver) is served: the tender leaves its tow position and comes to the receiver's belly. Everything is in the deck frame
    /// (origin at the kit mount; x right, y up, z forward, so astern is negative z). Pure maths: the domain tests run outside Unity,
    /// so there are no native Quaternion calls in this file.
    /// </summary>
    internal static class SkywellZone
    {
        /// <summary>Tow position: where the tender trails on its tether with nobody to serve (40 m astern and 15 m below the deck).</summary>
        public static readonly Vector3 Tow = new Vector3(0f, -15f, -40f);

        /// <summary>Formation slot a steered AI receiver (and the sim's seated jet) flies to: astern of the tow, well inside the zone.</summary>
        public static readonly Vector3 HoldSlot = new Vector3(0f, -15f, -75f);

        public const float Radius = 120f;          // service zone: within this of the tow position ...
        public const float MaxRelSpeed = 15f;      // ... relative speed under this ...
        public const float MaxRollDeg = 25f;       // ... and roll relative to the tanker under this
        public const float MaxTether = 150f;       // tether length: the tender never works farther than this from the winch
        public const float TailClearance = 25f;    // never closer than this astern of the deck (tail fin and open ramp)
        public const float Above = 4f;             // never higher than this over the deck
        public const float HoldSeconds = 1f;       // inside this long before the tender is sent
        public const float HoldGraceSeconds = 0.3f; // a gap this short (replica noise, one gust) does not restart the hold
        public const float ExitSeconds = 2f;       // outside this long breaks the service
        public const float SlotClearance = 35f;    // an AI is not called to the hold slot while anyone is this close to it
        public const float BreakDistance = 150f, BreakVertical = 15f, BreakSeconds = 8f; // a served AI peels off to its own lane, clear of the zone

        // Tanker limits while the kit is deployed.
        public const float MaxTankerSpeed = 140f;  // tether limit: the HUD warns above this
        public const float AutoReelSpeed = 160f;   // above this the winch reels the tender in by itself
        public const float DeployAltitude = 300f;  // radar altitude to deploy
        public const float StowAltitude = 150f;    // below this the kit stows by itself (hysteresis: a landing never starts with the deck out)

        /// <param name="rel">Receiver position minus the kit mount, in deck axes.</param>
        /// <param name="relSpeed">Receiver velocity relative to the tanker, m/s.</param>
        /// <param name="rollDeg">Receiver roll relative to the tanker, degrees (either sign).</param>
        public static bool Contains(Vector3 rel, float relSpeed, float rollDeg) =>
            relSpeed < MaxRelSpeed && Mathf.Abs(rollDeg) < MaxRollDeg &&
            (rel - Tow).magnitude <= Radius && rel.z <= -TailClearance && rel.y <= Above && rel.magnitude <= MaxTether - SkywellTender.Under;

        /// <summary>Where a served AI receiver peels off to (deck axes): lanes alternate side and height, so queued receivers never share one.</summary>
        public static Vector3 BreakSlot(int lane) =>
            Tow + new Vector3((lane & 1) == 0 ? BreakDistance : -BreakDistance, (lane & 2) == 0 ? BreakVertical : -BreakVertical, 0f);

        /// <summary>Roll of the receiver about its own nose relative to the tanker's horizon, from both aircraft's world axes.</summary>
        public static float Roll(Vector3 tankerRight, Vector3 tankerUp, Vector3 tankerFwd, Vector3 rxFwd, Vector3 rxUp)
        {
            var f = new Vector3(Vector3.Dot(rxFwd, tankerRight), Vector3.Dot(rxFwd, tankerUp), Vector3.Dot(rxFwd, tankerFwd));
            var u = new Vector3(Vector3.Dot(rxUp, tankerRight), Vector3.Dot(rxUp, tankerUp), Vector3.Dot(rxUp, tankerFwd));
            Vector3 level = Vector3.up - f * Vector3.Dot(Vector3.up, f);
            if (level.sqrMagnitude < 1e-4f) return 0f; // nose straight up or down: roll is meaningless
            return Vector3.SignedAngle(level, u, f);
        }

        /// <summary>
        /// Director cue for a receiver outside the zone, worst axis first, like a tanker's belly lights: DROP BACK / DOWN / UP / LEFT /
        /// RIGHT / CLOSE with metres to go, then SLOW or FAST, then LEVEL WINGS; HOLD STEADY once nothing is left to fix.
        /// </summary>
        /// <param name="relForward">Receiver speed along the tanker's heading minus the tanker's: positive = faster than the tanker.</param>
        public static string Director(Vector3 rel, float relSpeed, float relForward, float rollDeg)
        {
            if (rel.z > -TailClearance) return "DROP BACK  ·  AFT " + Mathf.CeilToInt(rel.z + TailClearance) + " m";
            var cue = new List<string>(4);
            if (rel.y > Above) cue.Add("DOWN " + Mathf.CeilToInt(rel.y - Above) + " m");
            Vector3 d = rel - Tow;
            float excess = d.magnitude - Radius;
            if (excess > 0f)
            {
                float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
                string metres = " " + Mathf.CeilToInt(excess) + " m";
                if (az >= ax && az >= ay) cue.Add(d.z > 0f ? "DROP BACK" + metres : "CLOSE" + metres);
                else if (ay >= ax) { if (rel.y <= Above) cue.Add(d.y < 0f ? "UP" + metres : "DOWN" + metres); }
                else cue.Add(d.x > 0f ? "LEFT" + metres : "RIGHT" + metres);
            }
            float tetherExcess = rel.magnitude - (MaxTether - SkywellTender.Under);
            if (tetherExcess > 0f && excess <= 0f) cue.Add("CLOSE " + Mathf.CeilToInt(tetherExcess) + " m");
            if (relSpeed >= MaxRelSpeed) cue.Add(relForward > 0f ? "SLOW" : "FAST");
            if (Mathf.Abs(rollDeg) >= MaxRollDeg) cue.Add("LEVEL WINGS");
            return cue.Count == 0 ? "HOLD STEADY" : string.Join("  ·  ", cue);
        }
    }

    /// <summary>
    /// One kit's "held the zone" timer for one receiver: inside for <see cref="SkywellZone.HoldSeconds"/>, tolerating gaps shorter than
    /// <see cref="SkywellZone.HoldGraceSeconds"/>. Per kit, never shared: two idle kits must not reset each other's receivers.
    /// </summary>
    internal struct ZoneHold
    {
        public float Since, LastIn;
        public bool Active;

        /// <returns>True once the receiver has held the zone long enough.</returns>
        public bool Update(bool inside, float now)
        {
            if (inside)
            {
                if (!Active)
                {
                    Active = true;
                    Since = now;
                }
                LastIn = now;
            }
            else if (Active && now - LastIn > SkywellZone.HoldGraceSeconds) Active = false;
            return Active && now - Since >= SkywellZone.HoldSeconds;
        }
    }

    /// <summary>The served receiver's "left the zone" timer: outside for longer than <see cref="SkywellZone.ExitSeconds"/> breaks the service.</summary>
    internal struct ZoneExit
    {
        public float OutSince; // 0 = inside

        public void Reset() => OutSince = 0f;

        /// <returns>True once the receiver has been outside for more than the grace.</returns>
        public bool Update(bool inside, float now)
        {
            if (inside)
            {
                OutSince = 0f;
                return false;
            }
            if (OutSince == 0f) OutSince = Mathf.Max(now, 1e-3f);
            return now - OutSince > SkywellZone.ExitSeconds;
        }
    }
}
