namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// VANGUARD drones of the local aircraft, for the C menu (WEAPONS > DRONES). Designation (ATTACK MY TARGET / CALL OFF): every launched
    /// loadout that follows the pilot's target reads it (REMORA air targets, MANTA ground targets, HAWC-X nudge, GLAIVE hover/orbit). Drone link
    /// (the rest of WEAPONS > DRONES): one linked drone at a time takes the pilot's orders (go where the pilot looks, go to the target,
    /// fly the pilot's heading, back on task); every drone keeps its last order when the link moves on. Watch it on the HUD feed or
    /// open the UAV terminal. Vanguard owns the host messages and validation; the radial menu only reads state and picks an order.
    /// </summary>
    internal interface IDroneCommand
    {
        /// <summary>True after DESIGNATE until RELEASE, the designated target dies or the local aircraft changes (local view). DESIGNATE stays valid while it is true: a fresh cue re-aims the drones.</summary>
        bool Striking { get; }

        /// <summary>The local aircraft has a target selected to designate (also SEND TO TARGET).</summary>
        bool CanStrike { get; }

        /// <summary>REMORA drones the local aircraft has flying right now.</summary>
        int Ready { get; }

        void Strike();
        void Screen();

        /// <summary>RAM MODE: the local aircraft's REMORAs ram their targets instead of firing (local view of the last switch).</summary>
        bool RamMode { get; }

        void ToggleRamMode();

        /// <summary>Linkable drones the local aircraft has flying (every Vanguard flight but AEGIS darts, ORCA and the ALE-X kite), launch order.</summary>
        int Linkable { get; }

        /// <summary>Roster index of the linked drone, -1 with none.</summary>
        int LinkedIndex { get; }

        /// <summary>Callsign of roster entry <paramref name="index"/> (REMORA 2); "" out of range.</summary>
        string Name(int index);

        /// <summary>Order and range of roster entry <paramref name="index"/> (HAND · 4.2 KM); "" out of range.</summary>
        string State(int index);

        /// <summary>The linked drone flies a pilot's order (HAND or WPT), not its own job.</summary>
        bool Ordered { get; }

        /// <summary>The drone feed card is on the HUD.</summary>
        bool FeedShown { get; }

        void Link(int index);

        /// <summary>Links the drone <paramref name="step"/> places along the roster. Orders stay with their drones.</summary>
        void Cycle(int step);

        /// <summary>SEND WHERE I LOOK: the linked drone (the first one when none is linked) flies to the ground the pilot's view centre is on and circles it.</summary>
        void SendToLook();

        /// <summary>SEND TO TARGET: the linked drone flies to the pilot's selected target and circles it.</summary>
        void SendToTarget();

        /// <summary>FLY MY HEADING: the linked drone takes the pilot's heading and height and holds them (HAND).</summary>
        void MatchNose();

        /// <summary>BACK ON TASK: the linked drone drops its order and goes back to its own job.</summary>
        void Release();

        void ToggleFeed();

        void OpenTerminal();
    }
}
