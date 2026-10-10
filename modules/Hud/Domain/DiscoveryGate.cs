namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// When the native flight-number hider must walk the HUD hierarchy again: for a NEW aircraft or a NEW HUD app manager
    /// (HUDExtras spawn late), never because the camera flipped between cockpit and orbit (that flip hands the hider no
    /// aircraft at all, which is not a new one). Ids are Unity instance ids; 0 means "none" and never counts as new.
    /// </summary>
    internal struct DiscoveryGate
    {
        private int aircraft, manager;
        private bool done;

        /// <summary>True when the hierarchy must be (re)discovered now. Call only while hiding with an aircraft present.</summary>
        public bool Needs(int aircraftId, int managerId)
        {
            if (aircraftId != 0 && aircraftId != aircraft) { aircraft = aircraftId; done = false; }
            if (managerId != 0 && managerId != manager) { manager = managerId; done = false; }
            if (done) return false;
            done = true;
            return true;
        }

        public void Reset()
        {
            aircraft = manager = 0;
            done = false;
        }
    }
}
