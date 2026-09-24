namespace BoscaliSummer.Features.Events.Domain
{
    /// <summary>What kind of story the event tells; drives the card icon and tint.</summary>
    internal enum EventCategory : byte
    {
        Economic = 0,
        Political = 1,
        Hazard = 2,
    }

    /// <summary>
    /// How loud an event is. Minor is weather; medium moves a price; a superevent is a
    /// scripted, faction-targeted intervention the director only fires when the theater is
    /// leaning, and it fires its effects in timed beats rather than once.
    /// </summary>
    internal enum EventTier : byte
    {
        Minor = 0,
        Medium = 1,
        Super = 2,
    }

    /// <summary>Which side of the theater an event is aimed at, resolved at roll time.</summary>
    internal enum EventTarget : byte
    {
        All = 0,

        /// <summary>The faction holding the fewest ground airbases.</summary>
        Losing = 1,

        /// <summary>The faction holding the most ground airbases.</summary>
        Leading = 2,
    }

    /// <summary>What one scripted beat does when it lands. Funds and allocation are scaled
    /// by <c>Events.EffectStrength</c>; a convoy is the vanilla group the side already owns.</summary>
    internal enum EventEffect : byte
    {
        None = 0,

        /// <summary>Credit the target faction's shared pool.</summary>
        Funds = 1,

        /// <summary>Credit every player on the target faction's side.</summary>
        Allocation = 2,

        /// <summary>Fund and queue the target's cheapest ready convoy group.</summary>
        Convoy = 3,
        /// <summary>Raise or lower target faction morale on the host.</summary>
        Morale = 4,
    }

    /// <summary>One timed beat of a superevent script.</summary>
    internal sealed class EventStep
    {
        public int AtSeconds { get; }
        public EventEffect Effect { get; }
        public float Amount { get; }
        public string Label { get; }

        public EventStep(int atSeconds, EventEffect effect, float amount, string label)
        {
            AtSeconds = atSeconds < 0 ? 0 : atSeconds;
            Effect = effect;
            Amount = amount;
            Label = label ?? "";
        }
    }

    /// <summary>
    /// One curated world event. Immutable: the catalog is hand-authored data, the selector
    /// only ever returns an index into it, and the same entry is built identically on every
    /// peer from the broadcast index.
    /// </summary>
    internal sealed class EventDefinition
    {
        private static readonly EventStep[] NoScript = new EventStep[0];

        public string Id { get; }
        public string Title { get; }
        public string FlavorText { get; }
        public EventCategory Category { get; }
        public EventTier Tier { get; }
        public EventTarget Target { get; }
        public string IconKey { get; }

        /// <summary>Support allocation cost factor while active; 1f leaves price unchanged.</summary>
        public float SupportCostMultiplier { get; }

        /// <summary>Support request cooldown factor while active; 1f leaves tempo unchanged.</summary>
        public float SupportCooldownMultiplier { get; }

        public int DurationMinSeconds { get; }
        public int DurationMaxSeconds { get; }

        /// <summary>Timed beats fired while the event runs; empty for minor and medium.</summary>
        public EventStep[] Script { get; }

        public EventDefinition(string id, string title, string flavorText, EventCategory category,
            EventTier tier, EventTarget target, string iconKey, float supportCostMultiplier,
            int durationMinSeconds, int durationMaxSeconds, EventStep[] script = null,
            float supportCooldownMultiplier = 1f)
        {
            Id = id;
            Title = title;
            FlavorText = flavorText;
            Category = category;
            Tier = tier;
            Target = target;
            IconKey = iconKey;
            SupportCostMultiplier = supportCostMultiplier;
            SupportCooldownMultiplier = supportCooldownMultiplier;
            DurationMinSeconds = durationMinSeconds;
            DurationMaxSeconds = durationMaxSeconds;
            Script = script ?? NoScript;
        }

        public bool IsSuper => Tier == EventTier.Super;
    }
}
