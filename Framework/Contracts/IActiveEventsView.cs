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
        /// The same factor for one player, including any response they bought and any
        /// faction the active event is aimed at. The host answers this per requester; a
        /// client answers it for itself.
        /// </summary>
        float SupportCostMultiplierFor(ulong playerId);
    }

    /// <summary>One timed beat of a superevent script, as shown.</summary>
    internal sealed class ActiveEventStep
    {
        public string Label { get; }

        /// <summary>Seconds after the event started this beat lands.</summary>
        public int AtSeconds { get; }

        public ActiveEventStep(string label, int atSeconds)
        {
            Label = label ?? "";
            AtSeconds = atSeconds;
        }
    }

    /// <summary>
    /// One catalog event as it is shown. Title and flavor text are looked up from the
    /// catalog by index on every peer, so only the index, its target faction and its
    /// mission timestamps cross the wire.
    /// </summary>
    internal sealed class ActiveEventView
    {
        private static readonly IReadOnlyList<ActiveEventStep> NoSteps = new ActiveEventStep[0];

        public string Id { get; }
        public string Title { get; }
        public string FlavorText { get; }
        public string Category { get; }
        public string IconKey { get; }

        /// <summary>Display tier: MINOR, MEDIUM or SUPEREVENT.</summary>
        public string Tier { get; }

        /// <summary>Display target: ALL THEATER, HARD-PRESSED SIDE or LEADING SIDE.</summary>
        public string Target { get; }

        public bool IsSuper { get; }

        /// <summary>Precomputed display line, e.g. "+50% SUPPORT COST" or "NO EFFECT".</summary>
        public string EffectSummary { get; }

        /// <summary>Timed beats, empty for minor and medium events.</summary>
        public IReadOnlyList<ActiveEventStep> Steps { get; }

        public float StartedAtMissionTime { get; }
        public float EndsAtMissionTime { get; }

        public ActiveEventView(string id, string title, string flavorText, string category,
            string tier, string target, bool isSuper, string iconKey, string effectSummary,
            IReadOnlyList<ActiveEventStep> steps, float startedAtMissionTime, float endsAtMissionTime)
        {
            Id = id;
            Title = title;
            FlavorText = flavorText;
            Category = category;
            Tier = tier;
            Target = target;
            IsSuper = isSuper;
            IconKey = iconKey;
            EffectSummary = effectSummary;
            Steps = steps ?? NoSteps;
            StartedAtMissionTime = startedAtMissionTime;
            EndsAtMissionTime = endsAtMissionTime;
        }
    }
}
