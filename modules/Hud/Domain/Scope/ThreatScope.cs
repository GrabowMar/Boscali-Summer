namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What a missile's seeker answers to: radar seekers (SARH, ARH, anti-radiation) fall to a beam and chaff, IR seekers to flares; the rest (laser, optical, inertial) to nothing the board can advise.</summary>
    internal enum ThreatSeeker : byte { Other, Radar, Ir }

    /// <summary>One inbound missile (or, with <see cref="Live"/> false, one that has just lost us), read this frame.</summary>
    internal struct ThreatMissile
    {
        public int Id;
        /// <summary>M1, M2...: the order the missiles appeared this sortie (a missile keeps its number).</summary>
        public int Number;
        /// <summary>The seeker as the game spells it (SARH, ARH, IR...).</summary>
        public string Seeker;
        public ThreatSeeker Class;
        /// <summary>False: it lost us (flare, chaff, a beam, terrain) and is still flying; drawn hollow and fading.</summary>
        public bool Live;
        /// <summary>Its seeker has locked on to us (rather than still searching).</summary>
        public bool Locked;
        /// <summary>Compass degrees off our nose, 0..360 clockwise (heading-up scope).</summary>
        public float BearingDeg;
        public float RangeM, ClosureMps;
        /// <summary>Seconds to impact at the current closure; NaN when it is not closing or is beyond the cap.</summary>
        public float TtiS;
        public float AgeS;
        /// <summary>Seconds since it lost us (defeated missiles only).</summary>
        public float DefeatedS;
        /// <summary>The beam this missile is steering us to: +1 = put it on the right wing line (090), -1 = the left (270), 0 = none yet. Kept per missile.</summary>
        public int BeamSide;
        /// <summary>Where it has been on the scope, oldest first: (bearing, time to impact or NaN, range) triples; provider-owned.</summary>
        public float[] Trail;
        public int TrailCount;
    }

    /// <summary>One radar that is painting us: a lock (it is tracking us) or a search spike.</summary>
    internal struct ThreatEmitter
    {
        public int Id;
        public string Name;
        public bool Lock;
        public float BearingDeg, RangeM;
        /// <summary>Seconds since it began painting us in this state (the spoke fades in over 0.3 s).</summary>
        public float AgeS;
        /// <summary>No fresh warning inside the hold of its current form: the page keeps the radar only for the pilot's hold time, so the ticker does not announce it again.</summary>
        public bool Held;
    }

    /// <summary>
    /// The THREAT page's feed (Hud's own, from <c>ThreatWatch</c>): every missile inbound on us, every radar painting us, the countermeasure
    /// stations and how many known SAM envelopes we are inside. Bearings and ranges are read per frame; the arrays are the provider's, filled in
    /// place. Live while anything is inbound, painting us, or has just lost us. Client-local (the game's own warning events).
    /// </summary>
    internal struct ThreatScope
    {
        public int MissileCount;
        public ThreatMissile[] Missiles;
        public int EmitterCount;
        public ThreatEmitter[] Emitters;
        public CmStation[] Cm;
        public int CmCount;
        /// <summary>Known SAM envelopes covering our position (<see cref="HasSam"/> false: the Intel picture has none for this faction).</summary>
        public bool HasSam;
        public int SamRings;
    }
}
