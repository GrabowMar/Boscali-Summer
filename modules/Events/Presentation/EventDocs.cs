namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>Local-only field archive copy. These notes never enter event state or the wire.</summary>
    internal sealed class EventDocEntry
    {
        internal string Code { get; }
        internal string Title { get; }
        internal string Body { get; }

        internal EventDocEntry(string code, string title, string body)
        {
            Code = code;
            Title = title;
            Body = body;
        }
    }

    internal static class EventDocs
    {
        internal static readonly EventDocEntry[] World =
        {
            new EventDocEntry("DIRECTORATE / 01", "THE FIELD DESK",
                "A cockpit receives the last sentence of a much longer report. Before a dispatch reaches the wire, a convoy clerk has counted the missing trucks, an airbase has revised its stores, and somebody in the rear has decided whose signature can move the reserve. The directorate keeps those fragments together. Its brief tells crews what changed; its archive keeps the reasons close enough to examine."),
            new EventDocEntry("DIRECTORATE / 02", "THE PRICE OF DISTANCE",
                "A support request is a route through fuel, roads, spare parts, crews and permission. The price on the cockpit display compresses all of them into one number. Bad weather can slow the next request without changing that price. A freight shock can raise the price while aircraft remain ready. Neither signal tells the whole story alone."),
            new EventDocEntry("DIRECTORATE / 03", "BASES ARE VOTES",
                "Airbases are the visible ledger of the ground war. A strip holds more than a runway: it anchors repair crews, stores and the chance to keep aircraft in the fight. The event director watches custody of ground bases when judging whether the theater has tipped far enough for an extraordinary intervention. A carrier is not a ground base."),
            new EventDocEntry("DIRECTORATE / 04", "THE PUBLIC SIGNAL",
                "A rumor can travel faster than a tanker. In the rear, speeches, censorship, bond drives and sanctions alter what the public believes the war can afford. At the front, that pressure arrives as a changing requisition price, a treasury order or a timed field dispatch. The archive records the message crews heard, never a claim that every rumor came true."),
            new EventDocEntry("DIRECTORATE / 05", "THE CONVOY CLOCK",
                "A supply column does not materialize because a dispatch says one was requested. It needs an available mission group and a funded route. When a theater order clears those conditions, the host queues the existing convoy system. If the route cannot be paid for or the mission has no ready group, the order remains a line in the log, not a ghost vehicle on the road."),
        };

        internal static readonly EventDocEntry[] Guide =
        {
            new EventDocEntry("FIELD MANUAL / 01", "READING A DISPATCH",
                "Only one world event is live at a time. Its target, price factor and support reset factor are separate readouts. The event clock counts mission time; a super-event may carry additional timed orders. The response desk offers routes only when the current event changes your side's support price."),
            new EventDocEntry("FIELD MANUAL / 02", "CHOOSING A RESPONSE",
                "Each pilot can initiate one response per event. Allocation containment or leverage is personal. A treasury directive spends faction funds and reaches the whole side. Contract intelligence is a faction-wide credit earned by completing a secondary contract and can be spent once per mission. A qualifying Recon perk opens a personal pilot channel. The host checks every cost and prerequisite."),
            new EventDocEntry("FIELD MANUAL / 03", "SHARED ORDERS",
                "A teammate can issue a faction directive after another pilot has made a personal choice. Both effects can benefit that pilot, subject to the support-price floor. A faction order is mirrored to late joiners; personal replies go to the pilot who sent them. The archive itself never sends an order."),
            new EventDocEntry("FIELD MANUAL / 04", "THE EVENT LOG",
                "The log is a bounded local record of completed events in this mission. It is newest first and is not backfilled for a pilot who joins late. A fresh scene clears it. The dossier library is different: it describes authored theater scenarios and can be read even while the director is quiet."),
            new EventDocEntry("FIELD MANUAL / 05", "AIRFRAME REGISTRY",
                "The aircraft shelf reads the game's loaded encyclopedia. Names, descriptions and performance figures come from native definitions; the rotating object is a local render-mesh copy of the selected prefab. It has no flight logic, network identity or collision. The viewer renders only when you change the selection or viewing angle."),
        };
    }
}
