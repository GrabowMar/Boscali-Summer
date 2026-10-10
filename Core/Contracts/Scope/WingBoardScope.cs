namespace BoscaliSummer.Core.Contracts
{
    /// <summary>What a wingman is doing, as the WING page words it (the prototype's FORM / JOIN / ENGAGE / DEFEND / WINCH / RTB / DAMAGED, plus the ground and landing cases).</summary>
    internal enum WingState : byte { Form, Join, Engage, Defend, Winch, Rtb, Refit, Damaged, Ground, Land }

    /// <summary>One wingman on the WING page, positions in metres from the player in the PLAYER'S frame (x right of the nose, y ahead, heading up).</summary>
    internal struct WingBoardMember
    {
        /// <summary>Its place in the wing (#n = seat + 2) and the name the page shows: <c>#2</c>, or <c>#2 REAPER</c> with a pilot callsign.</summary>
        public int Number;
        public string Name;
        public WingState State;
        public float X, Y;
        /// <summary>Its heading relative to the player's nose (compass degrees, clockwise).</summary>
        public float HeadingDeg;
        /// <summary>Where its slot is (<see cref="HasSlot"/> false: unknown, a client has no shape) and how far it is from it.</summary>
        public bool HasSlot;
        public float SlotX, SlotY, SlotErrM;
        /// <summary>Fuel and munitions as 0..1 fractions; <see cref="Bingo"/> / <see cref="Joker"/> as the wing's own fuel rule says.</summary>
        public float Fuel, Ammo;
        public bool Bingo, Joker;
        /// <summary>The wing's damage flag: a part is lost or the hull is under 90 % (latched until it is mended, so it says "was hit", not "is being hit"). The state word is not DAMAGED; the page words a fresh hit.</summary>
        public bool Damaged;
        /// <summary>Hull as a 0..1 fraction (1 = whole); <see cref="HasHull"/> false: unknown.</summary>
        public bool HasHull;
        public float Hull;
        /// <summary>Going home: the field it flies to (upper case) and how far it is from it (<see cref="HasField"/> false: no field known). Host only.</summary>
        public bool HasField;
        public float FieldM;
        public string Field;
        /// <summary>Engaging: the target it was given, its bearing from the player (compass degrees relative to the nose), its range from the wingman and its name. Host only.</summary>
        public bool HasTarget;
        public float TargetBearingDeg, TargetRangeM;
        public string TargetName;
        /// <summary>A missile guiding on it: where it is, seconds to impact (99 = unknown), what it homes with (<c>SARH</c>) and what the wingman answers with (<c>CHAFF</c>). Host only.</summary>
        public bool HasThreat;
        public float ThreatX, ThreatY, ThreatTtiS;
        public string ThreatSeeker, ThreatReply;
    }

    /// <summary>One order result for the page's ack log: <c>#3</c>, <c>REJOIN - WILCO</c>, how long ago, accepted or refused.</summary>
    internal struct WingBoardAck
    {
        public string Who, Text;
        /// <summary>Identifies the ack (the clock it was pushed at): a new id is a new ack, for the ticker's notice.</summary>
        public float Id;
        public float AgeS;
        public bool Accepted;
    }

    /// <summary>
    /// The WING page's feed (HUD v8), served by the Wing module: every wingman against the player (real position, slot, state word, fuel, munitions,
    /// target, threat), the formation and the order, the player's own order acknowledgements and the autopilot annunciator. The arrays are the provider's own,
    /// filled in place. Host and solo read the live wing; a client with a wing snapshot mirror gets the rows and the positions but no slots, targets or threats
    /// (the snapshot carries neither the shape nor the target; <see cref="Client"/> true), and a client with no mirror has no feed at all.
    /// </summary>
    internal struct WingBoardScope
    {
        public int Count;
        public WingBoardMember[] Members;
        /// <summary>The formation's name (upper case, <c>FINGER FOUR</c>) or null when unknown, and whether any member is fighting (the order reads ENGAGE rather than FORM).</summary>
        public string Shape;
        public bool Engaged;
        public bool Client;
        public int AckCount;
        public WingBoardAck[] Acks;
        /// <summary>The player's own autopilot as two short words (<c>NAV 2/5</c>, <c>ALT 450</c>); <see cref="HasAutopilot"/> false: not engaged.</summary>
        public bool HasAutopilot;
        public string ApText, ApSub;
        /// <summary>How far along its route a NAV autopilot is (the leg it flies over the legs there are), 0..1; 0 for the other holds.</summary>
        public float ApProgress;
    }
}
