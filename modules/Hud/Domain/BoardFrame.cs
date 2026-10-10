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

        /// <summary>The nearest threat's lamps, dial bearing and ages.</summary>
        public ThreatPicture Threat = ThreatPicture.Clear;

        /// <summary>Seconds since the HUD's aircraft appeared (negative: no lamp test).</summary>
        public float BootAge = -1f;

        /// <summary>Back to "nothing to say" without allocating.</summary>
        public void Clear()
        {
            Systems = default;
            Threat = ThreatPicture.Clear;
            BootAge = -1f;
        }
    }
}
