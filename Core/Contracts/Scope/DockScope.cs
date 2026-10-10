namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Where a SKYWELL receiver is in the hook-up: CUE (flying astern of the tanker), CAPTURE (inside the capture radius), ENGAGE (ready or docking, hold the brake), TRANSFER, SERVED.</summary>
    internal enum DockPhase : byte { Cue, Capture, Engage, Transfer, Served }

    /// <summary>
    /// The DOCK page's feed (HUD v8), served by the Vanguard module: a receiver pilot's probe against the SKYWELL tanker's contact point in the tanker's own axes,
    /// the closure, the hook-up phase and the real fuel and munitions of the receiver. Live only while the local aircraft is a receiver with a deployed tanker within
    /// cue range or docked to one. The thresholds are Vanguard's own (<c>SkywellContact</c>, <c>SkywellService</c>), carried along so the page never copies them.
    /// SKYWELL's phase is on every peer (<c>SkywellBoard.Views</c>), the geometry is read from the synced aircraft, so it works on clients too.
    /// </summary>
    internal struct DockScope
    {
        public DockPhase Phase;
        /// <summary>Service-zone mechanics: fly formation while the tender comes to the receiver.</summary>
        public bool ServiceZone, ZoneReady;
        public string JobName;
        public int StockRounds;
        /// <summary>The server has docked this receiver (the airframe is being flown in); false while it is still being flown by hand.</summary>
        public bool Docked;
        /// <summary>The tanker's name (upper case).</summary>
        public string Tanker;
        /// <summary>Probe minus contact point in tanker axes, metres: x to the tanker's right, y up, z forward (negative astern).</summary>
        public float RelX, RelY, RelZ;
        /// <summary>Distance from the probe to the contact point, and the closing speed along the tanker's axis (+ = closing).</summary>
        public float RangeM, ClosureMps;
        /// <summary>The thresholds: the cue range, the capture and engage radii, the closing speed the hook-up tolerates.</summary>
        public float CueM, CaptureM, EngageM, MaxClosingMps;
        /// <summary>The receiver's brake is applied.</summary>
        public bool Brake;
        /// <summary>The receiver's fuel now and what it will be filled to (0..1), the kilograms that is, and its rounds aboard against a full load of the rearmable stations.</summary>
        public float Fuel, FuelTarget, FuelNeedKg;
        public int Ammo, AmmoFull;
        /// <summary>Transfer phase only: 0..1 of the transfer time that has passed (the game moves the fuel and rounds at its end).</summary>
        public float TransferT;
        /// <summary>Served: what the transfer moved (kilograms of fuel, rounds); <see cref="HasMoved"/> false when the feed did not see the transfer begin (the board was not polled, or the hook-up began before it), so there is no total to claim.</summary>
        public bool HasMoved;
        public float MovedFuelKg;
        public int MovedRounds;
        /// <summary>The tanker kit's remaining fuel and munitions in kilograms (<see cref="HasStock"/> false: not known).</summary>
        public bool HasStock;
        public float StockFuelKg, StockFuelMaxKg, StockMunKg;
    }
}
