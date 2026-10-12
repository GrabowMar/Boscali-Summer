using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>A global position in metres (X east, Y up above sea level, Z north): what a trail stores, because local positions go stale at every floating-origin shift.</summary>
    internal struct GPos
    {
        public float X, Y, Z;

        public GPos(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    /// <summary>A missile flies on its seeker; a bomb falls (and may steer: glide and laser bombs have a target).</summary>
    internal enum ShotKind : byte { Missile, Bomb }

    /// <summary>The prototype's phase chain: BOOST (motor, still accelerating) - MID (coasting, data-link) - ACTIVE (the seeker homes) - TERM (the last 2.5 km); a bomb just FALLs.</summary>
    internal enum ShotPhase : byte { Boost, Mid, Active, Term, Fall }

    /// <summary>One of our own weapons in the air (or, <see cref="Ghost"/>, one that has just lost its target), read this frame.</summary>
    internal struct ShotMissile
    {
        public int Id;
        /// <summary>The weapon's short name and its full name.</summary>
        public string Short, Full;
        public ShotKind Kind;
        public ShotPhase Phase;
        /// <summary>The seeker is a data-linked active radar: its line is dashed until it goes active ("pitbull"), then solid with an A tag.</summary>
        public bool Arh;
        public bool Pitbull;
        /// <summary>Seconds since the seeker went active (a flash for the first 0.8 s); large when it has not.</summary>
        public float PitbullS;
        /// <summary>The weapon lost its target (flare, chaff, a notch, terrain): hollow and fading, still flying.</summary>
        public bool Ghost;
        public float GhostS;
        public GPos Pos;
        /// <summary>Heading of its velocity in compass degrees.</summary>
        public float HeadingDeg;
        /// <summary>Index into <see cref="ShotScope.Targets"/>; -1 none (fired blind, or the lock was lost).</summary>
        public int Target;
        /// <summary>Where it is going: the target for a guided weapon, the predicted splash point of a dumb bomb (<see cref="HasAim"/> false: nowhere known).</summary>
        public bool HasAim;
        public GPos Aim;
        /// <summary>Metres to the aim point, closing speed on it and seconds to arrive (NaN when it is not closing).</summary>
        public float RangeM, ClosureMps, TtiS;
        /// <summary>The fraction of the journey flown (launch to aim), 0..1: the timeline cell's progress rail.</summary>
        public float Progress;
        /// <summary>Where it has been, oldest first: global (x, y, z) triples; provider-owned.</summary>
        public float[] Trail;
        public int TrailCount;
    }

    /// <summary>Something the shots are flying at, or that we have selected.</summary>
    internal struct ShotTarget
    {
        public int Id;
        public string Name;
        public bool Air;
        /// <summary>A live shot is flying at it.</summary>
        public bool Hot;
        public GPos Pos;
        /// <summary>Horizontal velocity (east, north) in m/s: the aspect tick.</summary>
        public float VelX, VelZ;
    }

    /// <summary>The outcome of a shot, at the place it ended.</summary>
    internal struct ShotResult
    {
        public ShotState State;
        public float AgeS;
        public GPos Pos;
    }

    /// <summary>One cell of the SHOT TIMELINE (the last eight shots, oldest first).</summary>
    internal struct ShotCell
    {
        /// <summary>The weapon's id (<see cref="ShotMissile.Id"/>): the lead shot's cell is the one bracketed.</summary>
        public int Id;
        public string Name;
        public ShotState State;
        /// <summary>Journey flown, 0..1, while it is in flight.</summary>
        public float Frac;
        /// <summary>Seconds since it resolved (a fresh result lights its cell).</summary>
        public float AgeS;
    }

    /// <summary>
    /// The SHOT page's feed (Hud's own, from <c>ShotWatch</c> and the <see cref="ShotLedger"/>): our own deliveries in the air with their
    /// trails and aim points, their targets, the results of the last seconds and the timeline of the last eight shots (the selected weapon and its
    /// rounds are the stores header's). Positions are read per frame; arrays are the provider's, filled in place. Live while a shot is in the air or just resolved.
    /// Client-local (the game's own missile events); a remote player's shots are not ours and never show.
    /// </summary>
    internal struct ShotScope
    {
        public PlanFrame Frame;
        public float OwnAltM;
        public int MissileCount;
        public ShotMissile[] Missiles;
        public int TargetCount;
        public ShotTarget[] Targets;
        public int ResultCount;
        public ShotResult[] Results;
        public int CellCount;
        public ShotCell[] Cells;
        /// <summary>The farthest thing the plan has to hold, in km (the page's range scale follows it).</summary>
        public float NeedKm;
        /// <summary>The target that matters most (the lead shot's, else the first selected): its name, bearing off the nose and range.</summary>
        public bool HasLead;
        public string LeadName;
        public float LeadBearingDeg, LeadRangeM;
    }
}
