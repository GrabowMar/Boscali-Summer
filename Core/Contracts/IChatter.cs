using BoscaliSummer.Core.Chatter;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The radio every module speaks through. Local presentation of facts the client already sees: no world change, nothing sent. A publisher raises an event for a
    /// speaker and the chatter packs supply the words (docs/CHATTER_PACKS.md); <see cref="Say"/> is for words made elsewhere. Both return false when the radio will not say it.
    /// </summary>
    internal interface IChatter
    {
        bool Raise(string evt, ChatterSpeaker speaker, string key = null, params (string Name, string Value)[] facts);

        bool Say(ChatterSpeaker speaker, string key, string text, ChatterPriority priority = ChatterPriority.Status);

        /// <summary>The one speaker of a ground or airborne station on a net ("COMMAND", "MAGIC"), the same object every call.</summary>
        ChatterSpeaker Station(ChatterNet net, string callsign);

        /// <summary>The channel's state, the log and the exchanges, for a publisher that paces its own talk.</summary>
        ChatterDirector Director { get; }

        /// <summary>The clock the director runs on (mission seconds, stopped while the game is paused).</summary>
        float Now { get; }
    }
}
