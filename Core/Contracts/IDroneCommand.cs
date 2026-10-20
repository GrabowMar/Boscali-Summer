namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// VANGUARD drones of the local aircraft, for the radial menu. Designation (SEND DRONES TO STRIKE / SCREEN): every launched
    /// loadout that follows the pilot's target reads it (REMORA volley/ram, HAWC-X nudge, GLAIVE hover/orbit). Drone link
    /// (WEAPONS > DRONE LINK): one linked drone at a time, which the pilot can fly by hand (it goes the way the pilot's nose
    /// points), cycle through, watch on the HUD feed or open in the UAV terminal.
    /// Vanguard owns the host messages and validation; the radial menu only reads state and picks an order.
    /// </summary>
    internal interface IDroneCommand
    {
        /// <summary>True after DESIGNATE until RELEASE, the designated target dies or the local aircraft changes (local view). DESIGNATE stays valid while it is true: a fresh cue re-aims the drones.</summary>
        bool Striking { get; }

        /// <summary>The local aircraft has a target selected to designate.</summary>
        bool CanStrike { get; }

        /// <summary>REMORA drones the local aircraft has flying right now.</summary>
        int Ready { get; }

        void Strike();
        void Screen();

        /// <summary>Linkable drones the local aircraft has flying (every Vanguard flight but AEGIS darts, ORCA and the ALE-X kite), launch order.</summary>
        int Linkable { get; }

        /// <summary>Roster index of the linked drone, -1 with none.</summary>
        int LinkedIndex { get; }

        /// <summary>Callsign of roster entry <paramref name="index"/> (REMORA 2); "" out of range.</summary>
        string Name(int index);

        /// <summary>Order and range of roster entry <paramref name="index"/> (HAND · 4.2 KM); "" out of range.</summary>
        string State(int index);

        /// <summary>The linked drone is hand-flown.</summary>
        bool HandFlying { get; }

        /// <summary>The drone feed card is on the HUD.</summary>
        bool FeedShown { get; }

        void Link(int index);

        /// <summary>Links the drone <paramref name="step"/> places along the roster; a hand-flown link hands the stick over to it.</summary>
        void Cycle(int step);

        /// <summary>Takes hand control of the linked drone (linking the first one when none is), or gives it back to its job.</summary>
        void ToggleHand();

        void ToggleFeed();

        void OpenTerminal();
    }
}
