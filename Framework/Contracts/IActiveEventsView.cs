using System.Collections.Generic;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Read-only view of the mission's rotating world events, published by the Events
    /// module. The host owns rotation; a client reads the state the host broadcast. A
    /// consumer multiplies <see cref="SupportCostMultiplier"/> into whatever it prices,
    /// and reads exactly 1 while no event is active.
    /// </summary>
    internal interface IActiveEventsView
    {
        /// <summary>True once a mission is running on this peer, host or client.</summary>
        bool Available { get; }

        /// <summary>The event rolling now, or null while the theater is calm.</summary>
        ActiveEventView Current { get; }

        /// <summary>Bounded history, oldest first; never backfilled to a late joiner.</summary>
        IReadOnlyList<ActiveEventView> History { get; }

        /// <summary>Live modifier on support allocation costs; exactly 1 when calm.</summary>
        float SupportCostMultiplier { get; }

        /// <summary>
        /// The same factor for one player, including any response they bought. The host
        /// answers this per requester; a client answers it for itself.
        /// </summary>
        float SupportCostMultiplierFor(ulong playerId);
    }

    /// <summary>
    /// One catalog event as it is shown. Title and flavor text are looked up from the
    /// catalog by index on every peer, so only the index and its mission timestamps cross
    /// the wire.
    /// </summary>
    internal sealed class ActiveEventView
    {
        public string Id { get; }
        public string Title { get; }
        public string FlavorText { get; }
        public string Category { get; }
        public string IconKey { get; }

        /// <summary>Precomputed display line, e.g. "+50% SUPPORT COST" or "NO EFFECT".</summary>
        public string EffectSummary { get; }

        public float StartedAtMissionTime { get; }
        public float EndsAtMissionTime { get; }

        public ActiveEventView(string id, string title, string flavorText, string category,
            string iconKey, string effectSummary, float startedAtMissionTime, float endsAtMissionTime)
        {
            Id = id;
            Title = title;
            FlavorText = flavorText;
            Category = category;
            IconKey = iconKey;
            EffectSummary = effectSummary;
            StartedAtMissionTime = startedAtMissionTime;
            EndsAtMissionTime = endsAtMissionTime;
        }
    }
}
