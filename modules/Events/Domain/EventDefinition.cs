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
    /// One curated world event. Immutable: the catalog is hand-authored data, the selector
    /// only ever returns an index into it, and the same entry is built identically on every
    /// peer from the broadcast index.
    /// </summary>
    internal sealed class EventDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public string FlavorText { get; }
        public EventCategory Category { get; }
        public string IconKey { get; }

        /// <summary>Support allocation cost factor while active; 1f is flavor only.</summary>
        public float SupportCostMultiplier { get; }

        public int DurationMinSeconds { get; }
        public int DurationMaxSeconds { get; }

        public EventDefinition(string id, string title, string flavorText, EventCategory category,
            string iconKey, float supportCostMultiplier, int durationMinSeconds, int durationMaxSeconds)
        {
            Id = id;
            Title = title;
            FlavorText = flavorText;
            Category = category;
            IconKey = iconKey;
            SupportCostMultiplier = supportCostMultiplier;
            DurationMinSeconds = durationMinSeconds;
            DurationMaxSeconds = durationMaxSeconds;
        }
    }
}
