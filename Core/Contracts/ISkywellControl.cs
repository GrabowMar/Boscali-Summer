namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// SKYWELL tanker kit on the local aircraft, and the pit stop a receiver asks for. Vanguard owns the kit, the host
    /// message and validation; the radial menu only reads state and asks for a deploy / stow or an order.
    /// </summary>
    internal interface ISkywellControl
    {
        /// <summary>The local aircraft carries a SKYWELL kit.</summary>
        bool Carried { get; }

        /// <summary>The kit is deployed (ramp open, deck out).</summary>
        bool Deployed { get; }

        /// <summary>Remaining stock for the radial status, e.g. "FUEL 5.9T  AMMO 1.24T".</summary>
        string Stock { get; }

        void Toggle();

        /// <summary>A deployed friendly kit is within 800 m of the local aircraft's probe, or the aircraft is docked to one.</summary>
        bool PitAvailable { get; }

        /// <summary>The local aircraft's standing pit-stop order: 0 BOTH, 1 FUEL ONLY, 2 MUNITIONS ONLY.</summary>
        byte PitMode { get; }

        void SetPitMode(byte mode);

        /// <summary>Stations a handoff can serve right now (missing rounds), at most 8. The list shrinks as stations fill, so
        /// callers identify a station by <see cref="PitStationAt"/> once, then keep using that index.</summary>
        int PitStationCount { get; }

        /// <summary>Weapon-station index of the n-th servable station; -1 when n is out of range.</summary>
        int PitStationAt(int slot);

        /// <summary>Upper-case short name of this weapon station, at most 14 characters.</summary>
        string PitStationLabel(int station);

        /// <summary>"x1/2", plus " FIRST" when that station is the priority; empty once it needs nothing.</summary>
        string PitStationStatus(int station);

        /// <summary>Serve this weapon station first; -1 returns to automatic order.</summary>
        void PrioritiseStation(int station);
    }
}
