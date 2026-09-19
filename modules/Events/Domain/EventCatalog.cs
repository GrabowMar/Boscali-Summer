namespace BoscaliSummer.Features.Events.Domain
{
    /// <summary>
    /// The curated event list. Hand-authored, like the rail catalog: the selector only
    /// returns indices into this array, so a peer never needs the text over the wire.
    ///
    /// <para>Every flavor text stays honest about the real seams this module drives: support
    /// requisition prices, faction funds, per-player allocation and the vanilla convoy
    /// queue. A minor entry with a 1.0 multiplier says it is flavor only rather than
    /// implying a change that does not exist.</para>
    ///
    /// <para>A superevent is scripted: its beats fire from the authored table while it runs,
    /// and its target ("LOSING"/"LEADING") is resolved from live airbase ownership when the
    /// host rolls it. Windows are short on purpose — the director's job is to keep the
    /// theater moving, so an event is a few minutes, not a season.</para>
    /// </summary>
    internal static class EventCatalog
    {
        public static readonly EventDefinition[] All =
        {
            // ---- Minor: short, single-price, mostly weather --------------------------------
            new EventDefinition(
                "ceasefire_rumors", "Ceasefire Rumors",
                "Unconfirmed reports of a ceasefire are moving through the command nets and nobody is sure what to believe. The rumor shifts attention, but requisition prices are unchanged.",
                EventCategory.Political, EventTier.Minor, EventTarget.All,
                "ceasefire_rumors", 1f, 90, 150),
            new EventDefinition(
                "homefront_rally", "Homefront Rally",
                "A rally in the capital is dominating the broadcasts and the public mood is buoyant. It is good for morale and nothing else; support costs are unchanged.",
                EventCategory.Political, EventTier.Minor, EventTarget.All,
                "homefront_rally", 1f, 90, 150),
            new EventDefinition(
                "monsoon_season", "Monsoon Season",
                "A monsoon front has settled over the theater and flying is miserable for everyone. It changes the tempo of the war without touching the cost of support.",
                EventCategory.Hazard, EventTier.Minor, EventTarget.All,
                "monsoon_season", 1f, 120, 180),
            new EventDefinition(
                "war_bond_drive", "War Bond Drive",
                "A public subscription drive is oversubscribed and the treasury is releasing the proceeds to the front. Support costs ease, slightly, while the money lasts.",
                EventCategory.Political, EventTier.Minor, EventTarget.All,
                "war_bond_drive", 0.85f, 150, 210),

            // ---- Medium: one price, one theater ---------------------------------------------
            new EventDefinition(
                "global_supply_chain_crisis", "Global Supply Chain Crisis",
                "Freight lanes are backed up for a thousand kilometres and every replacement part now travels the long way round. Requisitioning support through official channels costs more until the backlog clears.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "global_supply_chain_crisis", 1.5f, 240, 300),
            new EventDefinition(
                "diplomatic_sanctions", "Diplomatic Sanctions",
                "A bloc vote has closed friendly ports to our suppliers and neither side looks ready to blink. Support requisitions carry the markup of a shrinking market.",
                EventCategory.Political, EventTier.Medium, EventTarget.All,
                "diplomatic_sanctions", 1.35f, 210, 300),
            new EventDefinition(
                "insurance_premium_hike", "Insurance Premium Hike",
                "Underwriters have repriced the war overnight and the extra premium lands on every shipment we order. Support costs climb while the market panics.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "insurance_premium_hike", 1.4f, 210, 300),
            new EventDefinition(
                "fuel_depot_fire", "Fuel Depot Fire",
                "A fuel depot is burning and the nearest reserves are rationed to keep the wings flying. Standing support requests compete for a thinner logistics pool.",
                EventCategory.Hazard, EventTier.Medium, EventTarget.All,
                "fuel_depot_fire", 1.25f, 180, 270),
            new EventDefinition(
                "rail_embargo", "Rail Embargo",
                "The rail net is refusing war freight after a week of strikes and sabotage. Everything the front asks for now arrives by road, one truck at a time.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "rail_embargo", 1.3f, 210, 300),
            new EventDefinition(
                "volunteer_logistics_corps", "Volunteer Logistics Corps",
                "Civilian drivers and depots have volunteered to move our stores and the queues are clearing by the hour. Support requisitions are cheaper while the volunteers stay.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "volunteer_logistics_corps", 0.75f, 210, 300),
            new EventDefinition(
                "veteran_contractor_influx", "Veteran Contractor Influx",
                "Retired crews have signed on as contractors and are taking the routine work off the regular staff. Requisitions move faster and cheaper for as long as they stay.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "veteran_contractor_influx", 0.7f, 180, 270),
            new EventDefinition(
                "black_market_surplus", "Black Market Surplus",
                "Someone has flooded the rear areas with surplus stores of doubtful paperwork and undeniable utility. Support is cheap while the supply lasts.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "black_market_surplus", 0.6f, 150, 240),
            new EventDefinition(
                "strategic_reserve_release", "Strategic Reserve Release",
                "Theater reserve depots have been ordered open to cover the current operation. For a short window, support requisitions draw on stock that is already paid for.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "strategic_reserve_release", 0.65f, 180, 270),
            new EventDefinition(
                "salvage_boom", "Salvage Boom",
                "Recovery crews are pulling flyable airframes and intact stores out of the wreck fields faster than the paperwork can lose them. Spare parts are suddenly cheap.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "salvage_boom", 0.8f, 180, 240),

            // ---- Superevents: scripted, targeted, rare ------------------------------------
            new EventDefinition(
                "allied_intervention", "Allied Intervention",
                "The bloc has seen which way the wind is blowing. A coalition supply bridge opens tonight: credit, spares, and one escorted convoy, all of it earmarked for the hard-pressed side.",
                EventCategory.Political, EventTier.Super, EventTarget.Losing,
                "allied_intervention", 0.65f, 240, 300,
                new[]
                {
                    new EventStep(15, EventEffect.Funds, 900f, "COALITION CREDIT RELEASED"),
                    new EventStep(45, EventEffect.Convoy, 0f, "ESCORTED CONVOY QUEUED"),
                    new EventStep(75, EventEffect.Allocation, 150f, "EMERGENCY REQUISITION CREDITS"),
                }),
            new EventDefinition(
                "emergency_appropriation", "Emergency Appropriation",
                "The war cabinet has torn open the strategic reserve as the front buckles. Requisition desks are told to stop counting and start shipping.",
                EventCategory.Economic, EventTier.Super, EventTarget.Losing,
                "emergency_appropriation", 0.8f, 210, 270,
                new[]
                {
                    new EventStep(15, EventEffect.Funds, 1400f, "RESERVE OPENED TO THE FRONT"),
                    new EventStep(60, EventEffect.Allocation, 250f, "PRIORITY ALLOCATION ISSUED"),
                }),
            new EventDefinition(
                "frontline_overstretch", "Frontline Overstretch",
                "The winning side has outrun its own supply lines. Every kilometre gained now costs twice to hold, and the depots behind the advance are running thin.",
                EventCategory.Economic, EventTier.Super, EventTarget.Leading,
                "frontline_overstretch", 1.35f, 240, 300,
                new[]
                {
                    new EventStep(90, EventEffect.Allocation, 200f, "DEPOT STOCKS CLAWED FORWARD"),
                }),
            new EventDefinition(
                "munitions_crisis", "Munitions Crisis",
                "A global shortage has hit every arsenal at once and the market has nothing left to sell. Support costs spike across the theater until the panic breaks.",
                EventCategory.Hazard, EventTier.Super, EventTarget.All,
                "munitions_crisis", 1.5f, 240, 300,
                new[]
                {
                    new EventStep(90, EventEffect.Allocation, 200f, "EMERGENCY REQUISITION CREDITS"),
                }),
            new EventDefinition(
                "ceasefire_ultimatum", "Ceasefire Ultimatum",
                "A bloc ultimatum has frozen the arms trade for the length of the truce window. With nobody able to buy a shell, requisition prices collapse for every side.",
                EventCategory.Political, EventTier.Super, EventTarget.All,
                "ceasefire_ultimatum", 0.7f, 210, 270,
                new[]
                {
                    new EventStep(60, EventEffect.Funds, 600f, "EMBARGO DIVIDEND PAID OUT"),
                }),
        };

        public static int Count => All.Length;

        public static EventDefinition At(int index) =>
            index >= 0 && index < All.Length ? All[index] : null;

        public static string CategoryLabel(EventCategory category)
        {
            switch (category)
            {
                case EventCategory.Political: return "POLITICAL";
                case EventCategory.Hazard: return "HAZARD";
                default: return "ECONOMIC";
            }
        }

        public static string TierLabel(EventTier tier)
        {
            switch (tier)
            {
                case EventTier.Super: return "SUPEREVENT";
                case EventTier.Medium: return "MEDIUM";
                default: return "MINOR";
            }
        }

        public static string TargetLabel(EventTarget target)
        {
            switch (target)
            {
                case EventTarget.Losing: return "HARD-PRESSED SIDE";
                case EventTarget.Leading: return "LEADING SIDE";
                default: return "ALL THEATER";
            }
        }
    }
}
