namespace BoscaliSummer.Core.Contracts
{
    /// <summary>What a strike track is: the kinetic rod, the PRSM missile, the cruise salvo, the EMP shock, the decoy barrage.</summary>
    internal enum StrikeKind : byte { Rod, Prsm, Cruise, Emp, Decoy }

    /// <summary>The OPS front a perk belongs to (its glyph on the perk strip).</summary>
    internal enum PerkFront : byte { Space, Cyber, Sof }

    internal enum PerkPhase : byte { Cooling, Ready, Armed }

    /// <summary>A node of the pilot's CYBER intrusion chain; the same order as the wire's node kinds.</summary>
    internal enum ChainNodeKind : byte { Radar, SamC2, Relay, Uplink, DataCenter }

    /// <summary>One of the local pilot's own strikes in the air (or just landed), as the STRIKE page draws its track.</summary>
    internal struct StrikeTrack
    {
        /// <summary>The support request's id: one track, one id.</summary>
        public int Id;
        public StrikeKind Kind;
        /// <summary>Seconds to impact; zero or negative once it has landed.</summary>
        public float Eta;
        /// <summary>Seconds from the call to the impact: the whole flight.</summary>
        public float Lead;
        /// <summary>Seconds the effect lasts after the impact (EMP, decoys), 0 for a one-shot.</summary>
        public float Linger;
        /// <summary>From the player's aircraft to the aim point: metres and true compass degrees.</summary>
        public float RangeM, BearingDeg;
        /// <summary>CRUISE: waypoint legs confirmed on the way (the route's diamonds).</summary>
        public int Legs;
        /// <summary>Payloads in the flight (PRSM and the rod are one) and the seconds between their impacts.</summary>
        public int Rounds;
        public float RoundGap;
        /// <summary>The aim point on the theatre grid ("12.4 / -3.1 KM"), cached by the provider; null when unknown.</summary>
        public string Target;
    }

    /// <summary>One OPS perk of the strip: its name, front, state and cooldown.</summary>
    internal struct PerkTile
    {
        public string Name;
        public PerkFront Front;
        public PerkPhase State;
        /// <summary>Seconds left on the cooldown (0 unless cooling) and the whole cooldown (the ring's denominator).</summary>
        public float Cooldown, Total;
    }

    /// <summary>
    /// The STRIKE / OPS page's feed (HUD v8), served by Support: the local pilot's own strikes (soonest first, at most three), the perk strip,
    /// the SAT window of the pilot's own RECON PASS or SAT CAMERA, the pilot's own CYBER intrusion and the figures of the gauge row. Every
    /// peer holds its own pilot's strikes (the host's accepted reply reaches the requester on the host and on a client alike), and the
    /// CYBER, SOF and OPS mirrors reach every member of the faction, so the page works on clients; what is not known stays false and hides.
    /// Arrays belong to the provider and are refilled in place.
    /// </summary>
    internal struct StrikeScope
    {
        public int TrackCount;
        public StrikeTrack[] Tracks;
        public int PerkCount;
        public PerkTile[] Perks;

        /// <summary>The 20 s window the pilot's own RECON PASS (radar) or SAT CAMERA (optical) opened over its footprint.</summary>
        public bool SatOpen, SatCamera;
        public float SatLeft, SatTotal;
        public int SatContacts;

        /// <summary>The pilot's own intrusion is hopping or holding: its chain (held nodes first, then the hop target), how many are held, the running hop and the trace.</summary>
        public bool CyberLive, CyberHopping;
        public int ChainCount, ChainHeld;
        public ChainNodeKind[] Chain;
        public float HopProgress, HopLeft;
        /// <summary>Trace burn in percent and its rate in percent per second.</summary>
        public float Trace, TraceRate;

        /// <summary>Allocation balance and the price of the kinetic rod and of the three strikes (the bar's ticks).</summary>
        public bool HasAlloc;
        public float Alloc;
        public int RodCost;
        public float[] AllocCosts;
        public int AllocCostCount;

        /// <summary>The kinetic satellite's fuel, 0..1.</summary>
        public bool HasBird;
        public float BirdFuel;

        /// <summary>The own SOF team on a mission: its callsign word, the mission word and the seconds left of its phase out of the whole phase.</summary>
        public bool HasSof;
        public float SofLeft, SofTotal;
        public string SofName, SofMission;
    }
}
