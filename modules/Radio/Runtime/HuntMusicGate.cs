namespace BoscaliSummer.Features.Radio.Runtime
{
    // A manual transport action wins until the current hunt has ended. Polling an
    // active hunt must never restart music that the listener deliberately stopped.
    internal sealed class HuntMusicGate
    {
        private int handledHunt;

        internal bool Begin(bool huntActive, int huntId)
        {
            if (!huntActive || huntId <= 0 || huntId == handledHunt) return false;
            handledHunt = huntId;
            return true;
        }

        internal void Suppress(int huntId) { if (huntId > 0) handledHunt = huntId; }
        internal void Reset() { handledHunt = 0; }
    }
}
