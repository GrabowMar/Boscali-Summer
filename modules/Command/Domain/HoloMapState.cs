namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Where the map key has taken the pilot: the game's small corner map, a key press whose Maximize is held back until it is a tap or a hold, the holo map, or the
    /// game's maximised map.
    /// </summary>
    internal enum HoloStage : byte { Mini, Pending, Holo, Mfd }

    /// <summary>What the runtime does after a <see cref="HoloMapState.Tick"/>.</summary>
    internal enum HoloStep : byte
    {
        /// <summary>Nothing.</summary>
        None,
        /// <summary>The key was held past the tap time: project the holo.</summary>
        ShowHolo,
        /// <summary>The key was let go while the holo was up: take it down (and the MFD map does not open).</summary>
        HideHolo,
        /// <summary>The key was tapped: call Maximize now, outside the key path, the way the game would have.</summary>
        OpenMfd,
    }

    /// <summary>
    /// The map key as a pure machine: HOLD to project the holo, TAP for the maximised map. The game's key handler reaches <c>DynamicMap.Maximize</c> the moment the key goes
    /// down, which is too early to know which of the two this is, so that call is swallowed (<see cref="KeyDown"/>, MINI -> PENDING) and the runtime asks <see cref="Tick"/> every frame
    /// with the key's held state: let go within <see cref="TapSeconds"/> it is a tap (<see cref="HoloStep.OpenMfd"/>, back to MINI until the Maximize postfix says <see cref="Maximized"/>);
    /// still held after that the holo is shown (<see cref="HoloStep.ShowHolo"/>) and stays exactly as long as the key does (<see cref="HoloStep.HideHolo"/> on release, no MFD).
    /// The Minimize key press, the holo off or unavailable, and every Maximize that is not the key's never wait: they are not <see cref="KeyDown"/>'s to hold back.
    /// Anything that closes the holo (<see cref="Close"/>: Esc, pause, death or eject, a lost aircraft, the setting off), a map maximised or minimised by any caller
    /// and a scene change also drop a pending tap. The machine never touches the game; the runtime reads it.
    /// </summary>
    internal sealed class HoloMapState
    {
        /// <summary>A key let go sooner than this after it went down is a tap.</summary>
        internal const float TapSeconds = .2f;

        private float downAt;
        // The holo is up because the key is held (it goes when the key does); false when the sim hook opened it.
        private bool byKey;

        /// <summary>Where the pilot is now.</summary>
        internal HoloStage Stage { get; private set; }

        /// <summary>The holo is up.</summary>
        internal bool Holo => Stage == HoloStage.Holo;

        /// <summary>The key's state decides what happens next: the runtime has to read it this frame.</summary>
        internal bool KeyDriven => Stage == HoloStage.Pending || (Stage == HoloStage.Holo && byKey);

        /// <summary>
        /// The map key's Maximize is about to run, at <paramref name="now"/> (seconds). True when it is swallowed and remembered (the caller skips the original): only from MINI, and only
        /// when <paramref name="holoAvailable"/> (the setting is on, the material and texture exist, an aircraft is flown, the corner map is showing and the game would open the map).
        /// False in every other case, and the original runs at once: the holo off or unavailable, and the sim hook's holo (that press is the way on to the maximised map).
        /// </summary>
        internal bool KeyDown(bool holoAvailable, float now)
        {
            if (Stage == HoloStage.Pending) return true;
            if (Stage == HoloStage.Mini && holoAvailable)
            {
                Stage = HoloStage.Pending;
                downAt = now;
                return true;
            }
            if (Stage == HoloStage.Holo) Stage = HoloStage.Mfd;
            return false;
        }

        /// <summary>
        /// Every frame, with the time (<paramref name="now"/>, the same clock as <see cref="KeyDown"/>) and whether the key is held. A held key past <see cref="TapSeconds"/> shows the holo;
        /// a key let go sooner opens the MFD map, and one let go later (a frame that took longer than the tap time) is a hold that was never seen and opens nothing.
        /// </summary>
        internal HoloStep Tick(float now, bool keyHeld)
        {
            if (Stage == HoloStage.Pending)
            {
                if (keyHeld)
                {
                    if (now - downAt < TapSeconds) return HoloStep.None;
                    Stage = HoloStage.Holo;
                    byKey = true;
                    return HoloStep.ShowHolo;
                }
                bool tap = now - downAt < TapSeconds;
                Stage = HoloStage.Mini;
                return tap ? HoloStep.OpenMfd : HoloStep.None;
            }
            if (Stage == HoloStage.Holo && byKey && !keyHeld)
            {
                Stage = HoloStage.Mini;
                return HoloStep.HideHolo;
            }
            return HoloStep.None;
        }

        /// <summary>The game's maximised map opened by any caller (the Maximize postfix): MFD, and the holo is gone and no tap is waiting.</summary>
        internal void Maximized() => Stage = HoloStage.Mfd;

        /// <summary>The game minimised its map (the Minimize postfix, which also runs when it was not maximised): MINI, no holo and no tap waiting.</summary>
        internal void Minimized() => Stage = HoloStage.Mini;

        /// <summary>Esc, pause, death, a lost aircraft, the setting off: the holo goes back to MINI and a pending tap is dropped. MFD and MINI are not the holo's to close.</summary>
        internal void Close()
        {
            if (Stage == HoloStage.Holo || Stage == HoloStage.Pending) Stage = HoloStage.Mini;
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

        /// <summary>Opens the holo from outside the key (the sim hook): from MINI only. It stays until <see cref="Close"/>, whatever the key does.</summary>
        internal bool Open(bool holoAvailable)
        {
            if (Stage != HoloStage.Mini || !holoAvailable) return Holo;
            Stage = HoloStage.Holo;
            byKey = false;
            return true;
        }

        /// <summary>A scene change: back to MINI.</summary>
        internal void Reset() => Stage = HoloStage.Mini;
    }
}
