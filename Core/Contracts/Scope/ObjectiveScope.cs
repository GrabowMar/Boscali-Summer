namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The OBJECTIVE page's feed (HUD v8), served by DynamicOperations over the pilot's selected accepted contract
    /// (<see cref="ISecondaryObjectivesView"/>), the faction's known air defence (<see cref="IThreatPicture"/>) and the local aircraft:
    /// the zone and where the pilot is against it, the contract's phases, the deadline, the payout, the hold gates and the SAM rings.
    /// Live while the selected contract is accepted, marked and inside its vicinity band; the host sends the same snapshot to every peer,
    /// so the page works on clients. Arrays belong to the provider and are refilled in place.
    /// </summary>
    internal struct ObjectiveScope
    {
        public int Id;
        /// <summary>"DAMAGE ASSESSMENT": the contract's title without its number, and its target's name ("FUEL DEPOT 3"; empty when the host names none).</summary>
        public string Title, Target;
        public ObjectiveContact Contact;
        /// <summary>The zone has a place on the map. False when the host has lost the contact: <see cref="X"/> and <see cref="Z"/> are then zero and the page draws no plot, only the plate (CONTACT LOST and the host's blocker).</summary>
        public bool HasMarker;
        /// <summary>The zone: global x, z and radius in metres.</summary>
        public float X, Z, Radius;
        /// <summary>The pilot: global x, z, true heading in degrees and the radar height above ground in metres.</summary>
        public float OwnX, OwnZ, OwnHeading, OwnAgl;
        /// <summary>The deadline: seconds left and the length the contract was given (the ring's whole).</summary>
        public float Left, Total;
        public int Money, Xp;
        /// <summary>The world's contract multiplier (0 = not reported).</summary>
        public float Multiplier;
        /// <summary>The phases the host reports (the pips), how many, and which one is current (-1 = none).</summary>
        public ObjectivePhase[] Phases;
        public int PhaseCount, Current;
        /// <summary>The host's instruction and its blocker for this tick.</summary>
        public string NextAction, Blocker;
        /// <summary>The current phase is one the on-station gates decide (JOIN, COVER, HOLD, SURVEY, OBSERVE), and the gates: inside the zone, 50 m above the ground,
        /// a clear sightline (read from the host's blocker once the other three hold), a fresh contact.</summary>
        public bool HasGates, GateRange, GateAlt, GateLos, GateTrack;
        /// <summary>The known air-defence rings near the zone and the pilot.</summary>
        public int RingCount;
        public AirDefenceRing[] Rings;
    }
}
