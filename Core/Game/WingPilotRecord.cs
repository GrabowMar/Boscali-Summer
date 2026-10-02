namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// Flat custom-pilot record mirrored from the Wing feature's squad contract.
    /// Field order matches the studio record the Wing side adapts.
    /// </summary>
    internal struct WingPilotRecord
    {
        public string Name;
        public string Callsign;
        public string DialogueTag;
        public string Background;
        public int Persona;
        public int Xp;
        public int Kills;
        public int Sorties;
        public bool HasPortrait;
        public int Body;
        public int Face;
        public int Hair;
        public int Uniform;
        public int Accessory;
        public int Backdrop;
    }
}
