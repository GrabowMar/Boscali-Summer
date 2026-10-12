using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// Everything the status board is told each tick, in one caller-owned object that is reused every tick (the board's
    /// readers fill it, <c>StatusPanel.Present</c> only reads it). Each reader owns its own fields; a block that has no
    /// data this tick is left at its empty default and its row takes no room.
    /// </summary>
    internal sealed class BoardFrame
    {
        /// <summary>The aircraft's own switches (the annunciator lamps).</summary>
        public SystemStates Systems;

        /// <summary>The top threat's lamps, dial bearing and ages.</summary>
        public ThreatPicture Threat = ThreatPicture.Clear;

        /// <summary>The board's clock (unscaled seconds): flash phase, new-contact pulses and the flash-on-warning timer run on it, so a render can pin it.</summary>
        public float Now;

        /// <summary>Seconds since the HUD's aircraft appeared (negative: no lamp test).</summary>
        public float BootAge = -1f;

        /// <summary>The player's unit system, for every distance the board writes.</summary>
        public AvUnits Units = AvUnits.Metric;

        /// <summary>Ranked threat detail lines, best first (<see cref="ContactCount"/> filled).</summary>
        public readonly ThreatContact[] Contacts = new ThreatContact[ThreatRank.MaxRows];
        public int ContactCount;

        /// <summary>Seeker types (<see cref="HudCm.SeekerMask"/>) of everything inbound; the CM tile that counters one is highlighted.</summary>
        public byte InboundMask;

        /// <summary>Countermeasure stations (<see cref="CmCount"/> filled).</summary>
        public readonly CmStation[] Cm = new CmStation[HudCm.MaxStations];
        public int CmCount;

        /// <summary>Fuel, endurance and BINGO / JOKER.</summary>
        public FuelState Fuel;

        /// <summary>Lasting damage: engines, leaks, fire, structure.</summary>
        public DamageState Damage;

        /// <summary>The camera is the cockpit's (not an external view of the aircraft, not a missile's): the only view the NAV DATA block is drawn in. Set by <c>NavReader</c>.</summary>
        public bool CockpitView;

        /// <summary>The NAV DATA block's data: nearest field, selected target, endurance and flags, mission clock (empty outside the cockpit view).</summary>
        public NavInput Nav;

        /// <summary>The notice the ticker shows now (<see cref="HasTicker"/> false: no notice).</summary>
        public bool HasTicker;
        public TickerLine Ticker;
    }
}
