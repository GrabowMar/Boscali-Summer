namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Remote-control face of the local radio for quick controls outside the RAD screen (the
    /// interaction menu). Radio owns playback, the vanilla-music hold and every rule behind
    /// these calls; the caller only reads state and presses buttons. Client-local, never
    /// networked.
    /// </summary>
    internal interface IRadioRemote
    {
        /// <summary>The MUSIC deck has at least one local track to play.</summary>
        bool DeckAvailable { get; }
        bool DeckPlaying { get; }
        string DeckTitle { get; }
        void DeckTogglePlayback();
        void DeckNext();
        void DeckPrevious();

        /// <summary>The RECEIVER has a station to tune.</summary>
        bool ReceiverAvailable { get; }
        bool ReceiverOn { get; }
        string ReceiverStation { get; }
        /// <summary>Switch the receiver on the current station, or off.</summary>
        void ReceiverToggle();
        void ReceiverSeek(int direction);

        /// <summary>Both transports down, soundtrack given back.</summary>
        void StopAll();
    }
}
