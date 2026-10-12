using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>First cause wins; the order is the order a pilot can act on them.</summary>
    internal static class GroundReasonRule
    {
        /// <summary>eligibleVehicles and traces are -1 until the first sweep and the first trace copy have finished.</summary>
        internal static GroundReason Decide(bool host, bool fieldsFound, bool tacticsOn, bool rtsInstalled,
            int eligibleVehicles, int traces)
        {
            if (!host) return GroundReason.NotHost;
            if (!fieldsFound) return GroundReason.Unsupported;
            if (!tacticsOn) return GroundReason.TacticsOff;
            if (rtsInstalled) return GroundReason.RtsOwnsOrders;
            if (eligibleVehicles < 0 || traces < 0) return GroundReason.Pending;
            if (eligibleVehicles == 0) return GroundReason.NoVehicles;
            if (traces == 0) return GroundReason.NoFront;
            return GroundReason.Ready;
        }

        /// <summary>Fixed upper-case token for logs and proof results (copy for the pilot lives in TheaterWords).</summary>
        internal static string Token(GroundReason reason)
        {
            switch (reason)
            {
                case GroundReason.Ready: return "READY";
                case GroundReason.TacticsOff: return "TACTICS_OFF";
                case GroundReason.RtsOwnsOrders: return "RTS_OWNS_ORDERS";
                case GroundReason.Unsupported: return "UNSUPPORTED";
                case GroundReason.NotHost: return "NOT_HOST";
                case GroundReason.NoVehicles: return "NO_VEHICLES";
                case GroundReason.NoFront: return "NO_FRONT";
                case GroundReason.Pending: return "PENDING";
                default: return "UNKNOWN";
            }
        }
    }

    /// <summary>State of the contact halt of one group.</summary>
    internal struct ContactClock
    {
        /// <summary>Mission second the current halt began; 0 when none.</summary>
        internal float Since;
        /// <summary>Sightings are ignored until this mission second (after a ceiling release).</summary>
        internal float GraceUntil;
        /// <summary>The trailing hold lasts until this mission second.</summary>
        internal float TailUntil;
    }

    /// <summary>When a sighting halts a group, and for how long, tail included. Bounded: vanilla's tracking database never ages.</summary>
    internal static class ContactRule
    {
        /// <summary>A sighting halts a group this close to its lead, metres.</summary>
        internal const float RadiusMeters = 700f;
        /// <summary>A sighting counts while it was spotted this recently, seconds.</summary>
        internal const float FreshSeconds = ContactWindow.FreshSeconds;
        /// <summary>The whole halt, trailing hold included, never exceeds this, seconds.</summary>
        internal const float MaxHoldSeconds = 90f;
        /// <summary>After a ceiling release the same sighting is ignored this long, seconds.</summary>
        internal const float GraceSeconds = 60f;
        /// <summary>The halt lingers this long after the last sighting, seconds.</summary>
        internal const float TailSeconds = 25f;

        /// <summary>A sighting counts while its last spotting is recent; a negative or NaN age is not a sighting.</summary>
        internal static bool Fresh(float lastSpottedAgeSeconds) =>
            lastSpottedAgeSeconds >= 0f && lastSpottedAgeSeconds <= FreshSeconds;

        /// <summary>True while the group stays in Contact this pass; now is mission seconds, always above zero once a group exists.</summary>
        internal static bool Step(ref ContactClock clock, bool sighting, float now)
        {
            // An unknown clock is no contact; a NaN written into Since or TailUntil would hold the group for ever.
            if (!float.IsFinite(now)) return false;
            if (now < clock.GraceUntil) sighting = false;
            if (sighting)
            {
                if (clock.Since <= 0f) clock.Since = now;
                if (now - clock.Since > MaxHoldSeconds)
                {
                    clock.Since = 0f; clock.TailUntil = 0f; clock.GraceUntil = now + GraceSeconds;
                    return false;
                }
                clock.TailUntil = Math.Min(now + TailSeconds, clock.Since + MaxHoldSeconds);
                return true;
            }
            if (clock.Since > 0f && now < clock.TailUntil) return true;
            // A tail that ran into the ceiling is a ceiling release too: without the grace a flickering sighting restarts the halt one pass later.
            if (clock.Since > 0f && clock.TailUntil >= clock.Since + MaxHoldSeconds) clock.GraceUntil = now + GraceSeconds;
            clock.Since = 0f; clock.TailUntil = 0f;
            return false;
        }
    }

    /// <summary>Progress of one group toward its current stage target.</summary>
    internal struct Approach
    {
        /// <summary>False until Reset has armed it.</summary>
        internal bool Active;
        /// <summary>Smallest distance seen, metres.</summary>
        internal float Best;
        /// <summary>Mission second the best distance last improved by MinGainMeters.</summary>
        internal float Since;
        /// <summary>Distance and mission second when the approach began (for the ETA).</summary>
        internal float StartDistance, StartAt;
    }

    /// <summary>Stall, closing and ETA rules over one Approach.</summary>
    internal static class ApproachProgress
    {
        /// <summary>No gain for this long means stalled, seconds.</summary>
        internal const float WindowSeconds = 180f;
        /// <summary>The gain that counts as progress, metres.</summary>
        internal const float MinGainMeters = 150f;
        /// <summary>Within this of the target a group has arrived and is never stalled, metres.</summary>
        internal const float ArrivedMeters = 400f;
        /// <summary>Within this of the target, or improving within ClosingWindowSeconds, a group is moving, metres.</summary>
        internal const float NearMeters = 600f;
        /// <summary>A gain this recent counts as closing, seconds.</summary>
        internal const float ClosingWindowSeconds = 60f;

        /// <summary>Arms the approach at the current distance.</summary>
        internal static void Reset(ref Approach approach, float distance, float now)
        {
            approach.Active = true;
            approach.Best = approach.StartDistance = distance;
            approach.Since = approach.StartAt = now;
        }

        /// <summary>True when the group has not closed MinGainMeters for a whole window and is not within ArrivedMeters.</summary>
        internal static bool Stalled(ref Approach approach, float distance, float now)
        {
            // An unknown distance changes nothing: arming at a made-up 0 m would make every later real distance look like no gain.
            if (!float.IsFinite(distance)) return false;
            if (!approach.Active) { Reset(ref approach, distance, now); return false; }
            if (distance <= ArrivedMeters || distance <= approach.Best - MinGainMeters)
            { approach.Best = distance; approach.Since = now; return false; }
            return now - approach.Since >= WindowSeconds;
        }

        /// <summary>True when the group is near its target or has gained ground recently; arming the approach is not a gain.</summary>
        internal static bool Closing(in Approach approach, float distance, float now) =>
            approach.Active && float.IsFinite(distance) &&
            (distance <= NearMeters || approach.Best < approach.StartDistance && now - approach.Since < ClosingWindowSeconds);

        /// <summary>Seconds to the target at the rate measured since the approach began; -1 when not known.</summary>
        internal static int EtaSeconds(in Approach approach, float distance, float now)
        {
            float elapsed = now - approach.StartAt, gained = approach.StartDistance - distance;
            // Written as "not enough" so a NaN clock or distance reads unknown instead of casting NaN to an int.
            if (!approach.Active || !(elapsed >= 30f) || !(gained >= 50f) || !float.IsFinite(distance)) return -1;
            return (int)Math.Max(0f, Math.Min(3600f, distance / (gained / elapsed)));
        }
    }

    /// <summary>Where to try next when the staged point is water or too steep; never forward of the line.</summary>
    internal static class SlotSearch
    {
        /// <summary>Number of candidate offsets, index 0 being the staged point itself.</summary>
        internal const int Candidates = 9;
        private static readonly float[] Along = { 0f, 0f, 120f, -120f, 0f, 240f, -240f, 0f, 0f };
        private static readonly float[] Back = { 0f, 120f, 0f, 0f, 240f, 0f, 0f, 360f, 480f };

        /// <summary>Offset index along the front tangent and back toward the friendly side; false past the last candidate.</summary>
        internal static bool Offset(int index, out float along, out float back)
        {
            along = back = 0f;
            if (index < 0 || index >= Candidates) return false;
            along = Along[index];
            back = Back[index];
            return true;
        }
    }

    /// <summary>Which side of a front line is friendly when the control field gives no clear answer: the last good side, then the side the group stands on, else none (hold in place).</summary>
    internal static class FrontSide
    {
        /// <summary>The remembered friendly direction counts while it still points this much along (or against) the new normal.</summary>
        internal const float HistoryMinDot = 0.2f;
        /// <summary>The group's centroid says which side it is on once it stands this far from the line, metres.</summary>
        internal const float CentroidClearMeters = 300f;

        /// <summary>+1 or -1 along the normal (normalX, normalZ) toward the friendly side, 0 when unknown. offset is the centroid minus the point on the line; a Withdraw passes allowCentroid false, since a group already across the line must not retreat further across it.</summary>
        internal static float Decide(float probeSign, bool hasHistory, float lastFriendlyX, float lastFriendlyZ,
            float normalX, float normalZ, bool allowCentroid, float offsetX, float offsetZ)
        {
            if (probeSign > 0f) return 1f;
            if (probeSign < 0f) return -1f;
            if (hasHistory)
            {
                float dot = lastFriendlyX * normalX + lastFriendlyZ * normalZ;
                if (Math.Abs(dot) >= HistoryMinDot) return dot > 0f ? 1f : -1f;
            }
            if (allowCentroid)
            {
                float along = offsetX * normalX + offsetZ * normalZ;
                if (Math.Abs(along) >= CentroidClearMeters) return along > 0f ? 1f : -1f;
            }
            return 0f;
        }
    }

    /// <summary>Casualty accounting for the withdrawal rule.</summary>
    internal static class GroupLedger
    {
        /// <summary>A destroyed vehicle stays a casualty; one a player or another mod took over is gone from the formation.</summary>
        internal static int FormedAfterLoss(int formed, bool destroyed) =>
            Math.Max(0, destroyed ? formed : formed - 1);
    }

    /// <summary>What the engine reads of a faction's ground effort; -1 is unknown and goes on the wire as 255 or 65535, never as 0.</summary>
    internal struct GroundOpReport
    {
        /// <summary>The value of every unknown count.</summary>
        internal const int Unknown = -1;
        /// <summary>Why steering runs or does not.</summary>
        internal GroundReason Reason;
        /// <summary>True when steering is running.</summary>
        internal bool Steering => Reason == GroundReason.Ready;
        /// <summary>Groups on the effort (OnEffort), their live vehicles and original strength.</summary>
        internal int Groups, Vehicles, Formed;
        /// <summary>Effort groups by stage; the five sum to Groups.</summary>
        internal int Rally, Line, Assault, Contact, Withdraw;
        /// <summary>Closing groups, vehicles answered by the hook in the last 8 s, vehicles within 300 m of their slot, groups stalled on this key, smallest known ETA seconds.</summary>
        internal int Moving, Steered, OnSlot, Stalled, EtaSeconds;
        /// <summary>Distance from the operation fix to the nearest live effort vehicle, metres; -1 unknown.</summary>
        internal float NearestToFixMeters;

        /// <summary>The report of a faction whose steering is not running (any reason but Ready): every number unknown. default(GroundOpReport) reads Ready with zeros, so never hand that one out.</summary>
        internal static GroundOpReport Unavailable(GroundReason reason) => new GroundOpReport
        {
            Reason = reason,
            Groups = Unknown, Vehicles = Unknown, Formed = Unknown,
            Rally = Unknown, Line = Unknown, Assault = Unknown, Contact = Unknown, Withdraw = Unknown,
            Moving = Unknown, Steered = Unknown, OnSlot = Unknown, Stalled = Unknown, EtaSeconds = Unknown,
            NearestToFixMeters = Unknown,
        };
    }

    /// <summary>One group's position and stage for the map overlay.</summary>
    internal readonly struct GroundMark
    {
        /// <summary>Centroid, metres.</summary>
        internal readonly float X, Z;
        /// <summary>Stage of the group.</summary>
        internal readonly GroundStage Stage;
        /// <summary>Live members, 0 to 6.</summary>
        internal readonly byte Alive;

        /// <summary>Builds one mark.</summary>
        internal GroundMark(float x, float z, GroundStage stage, byte alive)
        { X = x; Z = z; Stage = stage; Alive = alive; }
    }
}
