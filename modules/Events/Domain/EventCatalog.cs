namespace BoscaliSummer.Features.Events.Domain
{
    /// <summary>
    /// The curated event list. Hand-authored, like the rail catalog: the selector only
    /// returns indices into this array, so a peer never needs the text over the wire.
    ///
    /// <para>Every flavor text stays honest about the one real seam this module drives:
    /// support requisition costs. An entry with a 1.0 multiplier says so rather than
    /// implying a vanilla price change that does not exist.</para>
    /// </summary>
    internal static class EventCatalog
    {
        public static readonly EventDefinition[] All =
        {
            new EventDefinition(
                "global_supply_chain_crisis", "Global Supply Chain Crisis",
                "Freight lanes are backed up for a thousand kilometres and every replacement part now travels the long way round. Requisitioning support through official channels costs more until the backlog clears.",
                EventCategory.Economic, "global_supply_chain_crisis", 1.5f, 600, 900),
            new EventDefinition(
                "diplomatic_sanctions", "Diplomatic Sanctions",
                "A bloc vote has closed friendly ports to our suppliers and neither side looks ready to blink. Support requisitions carry the markup of a shrinking market.",
                EventCategory.Political, "diplomatic_sanctions", 1.35f, 480, 720),
            new EventDefinition(
                "insurance_premium_hike", "Insurance Premium Hike",
                "Underwriters have repriced the war overnight and the extra premium lands on every shipment we order. Support costs climb while the market panics.",
                EventCategory.Economic, "insurance_premium_hike", 1.4f, 480, 600),
            new EventDefinition(
                "fuel_depot_fire", "Fuel Depot Fire",
                "A fuel depot is burning and the nearest reserves are rationed to keep the wings flying. Standing support requests compete for a thinner logistics pool.",
                EventCategory.Hazard, "fuel_depot_fire", 1.25f, 360, 600),
            new EventDefinition(
                "volunteer_logistics_corps", "Volunteer Logistics Corps",
                "Civilian drivers and depots have volunteered to move our stores and the queues are clearing by the hour. Support requisitions are cheaper while the volunteers stay.",
                EventCategory.Economic, "volunteer_logistics_corps", 0.75f, 480, 720),
            new EventDefinition(
                "war_bond_drive", "War Bond Drive",
                "A public subscription drive is oversubscribed and the treasury is releasing the proceeds to the front. Support costs ease while the money lasts.",
                EventCategory.Political, "war_bond_drive", 0.85f, 600, 720),
            new EventDefinition(
                "veteran_contractor_influx", "Veteran Contractor Influx",
                "Retired crews have signed on as contractors and are taking the routine work off the regular staff. Requisitions move faster and cheaper for as long as they stay.",
                EventCategory.Economic, "veteran_contractor_influx", 0.7f, 360, 480),
            new EventDefinition(
                "black_market_surplus", "Black Market Surplus",
                "Someone has flooded the rear areas with surplus stores of doubtful paperwork and undeniable utility. Support is cheap while the supply lasts.",
                EventCategory.Economic, "black_market_surplus", 0.6f, 300, 480),
            new EventDefinition(
                "strategic_reserve_release", "Strategic Reserve Release",
                "Theater reserve depots have been ordered open to cover the current operation. For a short window, support requisitions draw on stock that is already paid for.",
                EventCategory.Economic, "strategic_reserve_release", 0.65f, 360, 480),
            new EventDefinition(
                "ceasefire_rumors", "Ceasefire Rumors",
                "Unconfirmed reports of a ceasefire are moving through the command nets and nobody is sure what to believe. The rumor shifts attention, but requisition prices are unchanged.",
                EventCategory.Political, "ceasefire_rumors", 1f, 300, 480),
            new EventDefinition(
                "homefront_rally", "Homefront Rally",
                "A rally in the capital is dominating the broadcasts and the public mood is buoyant. It is good for morale and nothing else; support costs are unchanged.",
                EventCategory.Political, "homefront_rally", 1f, 300, 480),
            new EventDefinition(
                "monsoon_season", "Monsoon Season",
                "A monsoon front has settled over the theater and flying is miserable for everyone. It changes the tempo of the war without touching the cost of support.",
                EventCategory.Hazard, "monsoon_season", 1f, 360, 600),
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
    }
}
