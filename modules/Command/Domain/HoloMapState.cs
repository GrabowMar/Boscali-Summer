namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>Where the map key has taken the pilot: the game's small corner map, the holo map between, or the game's maximised map.</summary>
    internal enum HoloStage : byte { Mini, Holo, Mfd }

    /// <summary>
    /// The map key's three stops, as a pure machine: MINI -> key -> HOLO -> key -> MFD -> key -> MINI. Only the key path is diverted: the game's key handler
    /// reaches <c>DynamicMap.Maximize</c> and the machine is asked through <see cref="KeyMaximize"/>; every other caller of Maximize (the mod's menus, panels,
    /// automation) is told to <see cref="Maximized"/> and gets the maximised map straight away, closing the holo if it was up. In HOLO the same key lets
    /// the original Maximize run, which is the way on to MFD. The holo also closes on <see cref="Close"/> (Esc, pause, death or eject, loss of the aircraft,
    /// the minimised map going away, the setting turned off, a scene change). The machine never touches the game; the runtime reads it.
    /// </summary>
    internal sealed class HoloMapState
    {
        /// <summary>Where the pilot is now.</summary>
        internal HoloStage Stage { get; private set; }

        /// <summary>The holo is up.</summary>
        internal bool Holo => Stage == HoloStage.Holo;

        /// <summary>
        /// The map key's Maximize is about to run. True when the holo takes the press instead (the caller then skips the original): only from MINI, and only
        /// when <paramref name="holoAvailable"/> (the setting is on, the material and texture exist, an aircraft is flown, the corner map is showing). From HOLO the
        /// press is the way on to MFD: the holo closes and false is returned, so the original opens the maximised map.
        /// </summary>
        internal bool KeyMaximize(bool holoAvailable)
        {
            if (Stage == HoloStage.Mini && holoAvailable)
            {
                Stage = HoloStage.Holo;
                return true;
            }
            if (Stage == HoloStage.Holo) Stage = HoloStage.Mfd;
            return false;
        }

        /// <summary>The game's maximised map opened by any caller (the Maximize postfix): MFD, and the holo is gone.</summary>
        internal void Maximized() => Stage = HoloStage.Mfd;

        /// <summary>The game minimised its map (the Minimize postfix, which also runs when it was not maximised): MINI, and the holo is gone.</summary>
        internal void Minimized() => Stage = HoloStage.Mini;

        /// <summary>Esc, pause, death, a lost aircraft, a scene change, the setting off: the holo goes back to MINI. MFD and MINI are not the holo's to close.</summary>
        internal void Close()
        {
            if (Stage == HoloStage.Holo) Stage = HoloStage.Mini;
        }

        /// <summary>
        /// Brings the machine back to the game's own state (checked every frame): a maximised map is MFD whatever was recorded, and an MFD that is not maximised (a
        /// Maximize the game refused, as while an aircraft is being chosen) is MINI again.
        /// </summary>
        internal void Sync(bool mapMaximized)
        {
            if (mapMaximized) Stage = HoloStage.Mfd;
            else if (Stage == HoloStage.Mfd) Stage = HoloStage.Mini;
        }

        /// <summary>Opens the holo from outside the key (the sim hook): from MINI only.</summary>
        internal bool Open(bool holoAvailable)
        {
            if (Stage != HoloStage.Mini || !holoAvailable) return Holo;
            Stage = HoloStage.Holo;
            return true;
        }

        /// <summary>A scene change: back to MINI.</summary>
        internal void Reset() => Stage = HoloStage.Mini;
    }
}
