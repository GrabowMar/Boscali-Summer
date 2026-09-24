namespace BoscaliSummer.Features.Events.Domain
{
    /// <summary>
    /// The curated event list. Hand-authored, like the rail catalog: the selector only
    /// returns indices into this array, so a peer never needs the text over the wire.
    ///
    /// <para>Every flavor text stays honest about the real seams this module drives: support
    /// requisition prices, faction funds, morale, per-player allocation and the vanilla convoy
    /// queue. An entry with both support multipliers at 1.0 is flavor only; a neutral
    /// price may still pair with a real request-tempo change.</para>
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
                "A rally in the capital sends volunteers and donated stores toward the front. Support requisitions ease while the drive lasts.",
                EventCategory.Political, EventTier.Minor, EventTarget.All,
                "homefront_rally", 0.9f, 90, 150),
            new EventDefinition(
                "monsoon_season", "Monsoon Season",
                "Monsoon rain is grounding courier flights and slowing support dispatches. Requisition prices hold, but crews need longer between calls.",
                EventCategory.Hazard, EventTier.Minor, EventTarget.All,
                "monsoon_season", 1f, 120, 180, supportCooldownMultiplier: 1.2f),
            new EventDefinition(
                "war_bond_drive", "War Bond Drive",
                "A public subscription drive is oversubscribed and the treasury is releasing the proceeds to the front. Support costs ease, slightly, while the money lasts.",
                EventCategory.Political, EventTier.Minor, EventTarget.All,
                "war_bond_drive", 0.85f, 150, 210),
            new EventDefinition(
                "press_censorship", "Press Censorship",
                "Every support request now clears a censor's desk before dispatch. The extra handling raises prices and slows repeat calls.",
                EventCategory.Political, EventTier.Minor, EventTarget.All,
                "press_censorship", 1.1f, 90, 150, supportCooldownMultiplier: 1.15f),
            new EventDefinition(
                "dust_storm", "Dust Storm",
                "A dust storm has rolled over the forward strips. Ground crews need extra time and stores between support sorties.",
                EventCategory.Hazard, EventTier.Minor, EventTarget.All,
                "dust_storm", 1.15f, 120, 180, supportCooldownMultiplier: 1.2f),
            new EventDefinition(
                "holiday_stand_down", "Holiday Stand-Down",
                "A national holiday brings an unofficial lull. Idle depots release their surplus at a small discount.",
                EventCategory.Political, EventTier.Minor, EventTarget.All,
                "holiday_stand_down", 0.9f, 90, 150),
            new EventDefinition(
                "scrap_drive", "Scrap Metal Drive",
                "A civilian scrap drive fills the rail yards with reusable metal. Support requisitions draw on cheaper reclaimed stores.",
                EventCategory.Economic, EventTier.Minor, EventTarget.All,
                "scrap_drive", 0.9f, 90, 150),

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
                "A fuel depot is burning and the nearest reserves are rationed. Support requests cost more and crews need longer to reset between dispatches.",
                EventCategory.Hazard, EventTier.Medium, EventTarget.All,
                "fuel_depot_fire", 1.25f, 180, 270, supportCooldownMultiplier: 1.15f),
            new EventDefinition(
                "rail_embargo", "Rail Embargo",
                "The rail net is refusing war freight after a week of strikes and sabotage. Everything the front asks for now arrives by road, one truck at a time.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "rail_embargo", 1.3f, 210, 300),
            new EventDefinition(
                "volunteer_logistics_corps", "Volunteer Logistics Corps",
                "Civilian drivers and depots have volunteered to move our stores. Support requisitions are cheaper and dispatches turn around faster while they stay.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "volunteer_logistics_corps", 0.75f, 210, 300, supportCooldownMultiplier: 0.85f),
            new EventDefinition(
                "veteran_contractor_influx", "Veteran Contractor Influx",
                "Retired crews have signed on as contractors and are taking routine work off the regular staff. Repeat support calls turn around faster and cheaper while they stay.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "veteran_contractor_influx", 0.7f, 180, 270, supportCooldownMultiplier: 0.85f),
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
            new EventDefinition(
                "dockworker_strike", "Dockworker Strike",
                "Port labor has walked off over hazard pay and nothing is moving off the quays. Requisitions queue behind the stalled cargo until someone breaks the deadlock.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "dockworker_strike", 1.3f, 210, 300),
            new EventDefinition(
                "currency_devaluation", "Currency Devaluation",
                "The treasury has quietly let the currency slide to keep the mints running. Every requisition now costs more of a currency worth less by the week.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "currency_devaluation", 1.35f, 210, 300),
            new EventDefinition(
                "harvest_shortfall", "Harvest Shortfall",
                "A poor harvest has the quartermasters competing with the civilian market for road transport. Support requisitions ride the same thin, expensive trucks.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "harvest_shortfall", 1.2f, 180, 270),
            new EventDefinition(
                "captured_depot", "Captured Depot",
                "A forward push has overrun an enemy supply depot intact. The haul is being sorted and issued at cost while the paperwork catches up.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "captured_depot", 0.7f, 180, 240),

            // ---- Superevents: scripted, targeted, rare ------------------------------------
            new EventDefinition(
                "allied_intervention", "Allied Intervention",
                "Coalition trucks are crossing the frontier for the hard-pressed side. Emergency credit opens the route now; a relief convoy and pilot allocations follow.",
                EventCategory.Political, EventTier.Super, EventTarget.Losing,
                "allied_intervention", 0.65f, 240, 300,
                new[]
                {
                    new EventStep(0, EventEffect.Funds, 900f, "COALITION CREDIT RELEASED"),
                    new EventStep(1, EventEffect.Morale, 8f, "COALITION MORALE +8"),
                    new EventStep(8, EventEffect.Convoy, 1200f, "RELIEF CONVOY REQUESTED"),
                    new EventStep(35, EventEffect.Allocation, 150f, "PILOT ALLOCATIONS ISSUED"),
                }),
            new EventDefinition(
                "emergency_appropriation", "Emergency Appropriation",
                "The war cabinet has opened the reserve for the side losing ground. Treasury funds arrive immediately, followed by a depot convoy and pilot allocations.",
                EventCategory.Economic, EventTier.Super, EventTarget.Losing,
                "emergency_appropriation", 0.8f, 210, 270,
                new[]
                {
                    new EventStep(0, EventEffect.Funds, 1100f, "WAR RESERVE RELEASED"),
                    new EventStep(1, EventEffect.Morale, 6f, "WAR RESERVE MORALE +6"),
                    new EventStep(8, EventEffect.Convoy, 1200f, "DEPOT CONVOY REQUESTED"),
                    new EventStep(35, EventEffect.Allocation, 250f, "PRIORITY ALLOCATION ISSUED"),
                }),
            new EventDefinition(
                "frontline_overstretch", "Frontline Overstretch",
                "The leading side has outrun its supply line. Rear command spends funds on repairs and morale slips while support gets dearer; a recovery convoy is ordered later.",
                EventCategory.Economic, EventTier.Super, EventTarget.Leading,
                "frontline_overstretch", 1.35f, 240, 300,
                new[]
                {
                    new EventStep(0, EventEffect.Funds, -800f, "SUPPLY LINE REPAIRS FUNDED"),
                    new EventStep(1, EventEffect.Morale, -8f, "FRONTLINE MORALE -8"),
                    new EventStep(45, EventEffect.Funds, -400f, "REAR DEPOTS DRAINED"),
                    new EventStep(90, EventEffect.Convoy, 800f, "RECOVERY CONVOY REQUESTED"),
                }),
            new EventDefinition(
                "munitions_crisis", "Munitions Crisis",
                "A theater-wide shortage empties the depots and shakes morale. Emergency purchases and slower dispatches begin now; pilot allocations and last-reserve convoys follow.",
                EventCategory.Hazard, EventTier.Super, EventTarget.All,
                "munitions_crisis", 1.5f, 240, 300,
                new[]
                {
                    new EventStep(0, EventEffect.Funds, -450f, "EMERGENCY PURCHASES CHARGED"),
                    new EventStep(1, EventEffect.Morale, -5f, "MUNITIONS MORALE -5"),
                    new EventStep(30, EventEffect.Allocation, 150f, "PILOT ALLOCATIONS ISSUED"),
                    new EventStep(75, EventEffect.Convoy, 1000f, "LAST RESERVE CONVOY REQUESTED"),
                }, 1.2f),
            new EventDefinition(
                "ceasefire_ultimatum", "Ceasefire Ultimatum",
                "Diplomats have set a deadline. Both sides rush stocked materiel toward the line, briefly cutting support costs and turnaround time as convoys depart.",
                EventCategory.Political, EventTier.Super, EventTarget.All,
                "ceasefire_ultimatum", 0.7f, 210, 270,
                new[]
                {
                    new EventStep(0, EventEffect.Funds, 350f, "EMERGENCY FREIGHT RELEASED"),
                    new EventStep(8, EventEffect.Convoy, 1000f, "FRONTLINE CONVOY REQUESTED"),
                    new EventStep(45, EventEffect.Allocation, 100f, "PILOT ALLOCATIONS ISSUED"),
                }, 0.85f),

            // Append new rows: catalog indices are carried over the network.
            new EventDefinition(
                "quartermaster_audit", "Quartermaster Audit",
                "An urgent inventory check slows procurement paperwork. Support requisitions cost a little more until the books are reconciled.",
                EventCategory.Economic, EventTier.Minor, EventTarget.All,
                "quartermaster_audit", 1.1f, 120, 180),
            new EventDefinition(
                "forward_workshop", "Forward Workshop",
                "Mechanics have recovered enough usable parts to handle small support requests locally. Requisition costs ease while the workshop stock lasts.",
                EventCategory.Economic, EventTier.Minor, EventTarget.All,
                "forward_workshop", 0.9f, 120, 180),
            new EventDefinition(
                "local_donations", "Local Donations",
                "Civilian collections are covering a narrow slice of the supply bill. Support requests are modestly cheaper for a short window.",
                EventCategory.Political, EventTier.Minor, EventTarget.All,
                "local_donations", 0.9f, 120, 180),
            new EventDefinition(
                "stocktaking_hold", "Stocktaking Hold",
                "Depots have paused release while crews count the remaining stores. Replacement support is slightly more expensive until the tally clears.",
                EventCategory.Economic, EventTier.Minor, EventTarget.All,
                "stocktaking_hold", 1.1f, 120, 180),
            new EventDefinition(
                "radio_relay_lease", "Radio Relay Lease",
                "A civilian relay network is carrying requisition traffic. Support costs fall slightly and dispatch acknowledgements arrive sooner while the lease is active.",
                EventCategory.Political, EventTier.Minor, EventTarget.All,
                "radio_relay_lease", 0.9f, 120, 180, supportCooldownMultiplier: 0.85f),
            new EventDefinition(
                "fuel_rationing", "Fuel Rationing",
                "Quartermasters are holding back fuel reserves after a poor delivery. The added handling cost raises support requisition prices for a short spell.",
                EventCategory.Hazard, EventTier.Minor, EventTarget.All,
                "fuel_rationing", 1.15f, 120, 180),

            new EventDefinition(
                "air_corridor_closure", "Air Corridor Closure",
                "A contested transport corridor has been closed to suppliers. Rerouted support shipments carry a premium and slow repeat dispatches.",
                EventCategory.Political, EventTier.Medium, EventTarget.All,
                "air_corridor_closure", 1.3f, 180, 270, supportCooldownMultiplier: 1.2f),
            new EventDefinition(
                "depot_refit", "Depot Refit",
                "An aging storage complex needs emergency repairs before stores can be issued. Requisition costs rise while the depot is reworked.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "depot_refit", 1.25f, 180, 270),
            new EventDefinition(
                "merchant_fleet_charter", "Merchant Fleet Charter",
                "A bulk freight charter has locked in cheap capacity for the theater. Support requisitions draw on its favorable contract rate.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "merchant_fleet_charter", 0.7f, 180, 270),
            new EventDefinition(
                "parts_standardization", "Parts Standardization",
                "Workshops have agreed on a common set of replacement parts. Less custom handling brings support requisition costs down.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "parts_standardization", 0.8f, 180, 270),
            new EventDefinition(
                "emergency_procurement", "Emergency Procurement",
                "The treasury has prepaid a block of essential stores. Support requisitions are cheaper until that contract window closes.",
                EventCategory.Political, EventTier.Medium, EventTarget.All,
                "emergency_procurement", 0.7f, 180, 270),
            new EventDefinition(
                "harbor_insurance_spike", "Harbor Insurance Spike",
                "A string of losses has sent freight insurance rates sharply upward. Support requests now carry the new premium.",
                EventCategory.Economic, EventTier.Medium, EventTarget.All,
                "harbor_insurance_spike", 1.35f, 180, 270),

            new EventDefinition(
                "partisan_supply_raid", "Partisan Supply Raid",
                "The leading side's rear freight line has been hit. Emergency repairs drain its treasury and shake morale; a replacement convoy is requested before fresh pilot allocations arrive.",
                EventCategory.Hazard, EventTier.Super, EventTarget.Leading,
                "partisan_supply_raid", 1.4f, 240, 300,
                new[]
                {
                    new EventStep(0, EventEffect.Funds, -650f, "REAR LINE REPAIRS CHARGED"),
                    new EventStep(1, EventEffect.Morale, -8f, "SUPPLY RAID MORALE -8"),
                    new EventStep(50, EventEffect.Convoy, 1100f, "REPLACEMENT CONVOY REQUESTED"),
                    new EventStep(80, EventEffect.Allocation, 100f, "PILOT ALLOCATIONS ISSUED"),
                }),
            new EventDefinition(
                "industrial_surge", "Industrial Surge",
                "Factories on both sides have cleared their backlogs. War funds and morale rise immediately; each side requests a stocked convoy and later issues pilot allocations.",
                EventCategory.Economic, EventTier.Super, EventTarget.All,
                "industrial_surge", 0.65f, 240, 300,
                new[]
                {
                    new EventStep(0, EventEffect.Funds, 450f, "FACTORY ADVANCE RELEASED"),
                    new EventStep(1, EventEffect.Morale, 5f, "PRODUCTION MORALE +5"),
                    new EventStep(25, EventEffect.Convoy, 1000f, "FACTORY CONVOY REQUESTED"),
                    new EventStep(70, EventEffect.Allocation, 100f, "PILOT ALLOCATIONS ISSUED"),
                }),
            new EventDefinition(
                "emergency_withdrawal", "Emergency Withdrawal",
                "The hard-pressed side is pulling stores back under pressure. Emergency funds and a recovery convoy help it regroup, but morale falls and support costs rise until the move is complete.",
                EventCategory.Hazard, EventTier.Super, EventTarget.Losing,
                "emergency_withdrawal", 1.2f, 210, 270,
                new[]
                {
                    new EventStep(0, EventEffect.Funds, 600f, "WITHDRAWAL FUND RELEASED"),
                    new EventStep(1, EventEffect.Morale, -6f, "WITHDRAWAL MORALE -6"),
                    new EventStep(8, EventEffect.Convoy, 1300f, "RECOVERY CONVOY REQUESTED"),
                    new EventStep(30, EventEffect.Allocation, 150f, "PILOT ALLOCATIONS ISSUED"),
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
