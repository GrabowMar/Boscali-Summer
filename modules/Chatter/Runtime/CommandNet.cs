using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;

namespace BoscaliSummer.Modules.Chatter.Runtime
{
    /// <summary>
    /// The command net's publisher: watches what the theater modules already show (the rolling event, the active operation's phase, a friendly insertion touching down
    /// nearby) and raises the matching <c>command.*</c> event when one changes. Operations baseline quietly; event starts arrive directly from the replicated event service.
    /// </summary>
    internal sealed class CommandNet
    {
        private const float InsertionRange = 20000f;

        private int operationId;
        private TheaterPhase operationPhase;
        private bool operationBaseline, insertion;
        private IAirAssaultObservation assault;
        private IActiveEventsView eventSource;
        private IChatter radio;
        private string lastEvent;
        private Aircraft player;

        public void Reset()
        {
            if (assault != null) assault.Landed -= InsertionLanded;
            if (eventSource != null) eventSource.EventStarted -= EventStarted;
            eventSource = null;
            radio = null;
            lastEvent = null;
            assault = null;
            player = null;
            operationId = 0;
            operationPhase = TheaterPhase.Awaiting;
            operationBaseline = insertion = false;
        }

        public void Tick(IChatter chatter, Aircraft me)
        {
            radio = chatter;
            player = me;
            ModuleServices.TryGet<IActiveEventsView>(out var events);
            if (!ReferenceEquals(eventSource, events))
            {
                if (eventSource != null) eventSource.EventStarted -= EventStarted;
                eventSource = events;
                if (eventSource != null)
                {
                    eventSource.EventStarted += EventStarted;
                    ActiveEventView current = eventSource.Current;
                    if (current != null && (current.IsSuper || chatter.Now - current.StartedAtMissionTime < 3f)) EventStarted(current);
                }
            }
            ModuleServices.TryGet<IAirAssaultObservation>(out var observation);
            if (!ReferenceEquals(assault, observation))
            {
                if (assault != null) assault.Landed -= InsertionLanded;
                assault = observation;
                if (assault != null) assault.Landed += InsertionLanded;
            }
            if (insertion)
            {
                insertion = false;
                ChatterSpeaker drop = chatter.Station(ChatterNet.Command, "DROP LEAD");
                drop.Character = "paratrooper";
                chatter.Raise("command.insertion", drop);
            }
            if (ModuleServices.TryGet<ITheaterWarView>(out var theater) && theater.Available && theater.IsFresh)
            {
                var operation = theater.ActiveOperation;
                if (operationBaseline && operation != null && (operation.Id != operationId || operation.Phase != operationPhase))
                    chatter.Raise("command.operation", chatter.Station(ChatterNet.Command, "COMMAND"), "operation:" + operation.Id + ":" + operation.Phase,
                        ("name", operation.Name), ("phase", TheaterWords.Phase(operation.Phase, operation.PhaseNote)));
                operationId = operation?.Id ?? 0;
                operationPhase = operation?.Phase ?? TheaterPhase.Awaiting;
                operationBaseline = true;
            }
        }

        private void EventStarted(ActiveEventView current)
        {
            if (radio == null || current == null) return;
            string key = "event:" + current.Id + ":" + current.StartedAtMissionTime.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (key == lastEvent) return;
            lastEvent = key;
            string evt = current.IsSuper ? "command.superevent" : "command.event";
            string specific = evt + "." + current.Id;
            if (current.IsSuper && !radio.Director.Library.Events.ContainsKey(specific)) specific = "command.event." + current.Id;
            if (radio.Director.Library.Events.ContainsKey(specific)) evt = specific;
            radio.Raise(evt, radio.Station(ChatterNet.Command, "COMMAND"), key, ("title", current.Title));
        }

        private void InsertionLanded(int faction, float x, float z, int shell)
        {
            if (player == null || player.NetworkHQ == null || player.NetworkHQ.GetInstanceID() != faction || !float.IsFinite(x) || !float.IsFinite(z)) return;
            var at = player.GlobalPosition();
            if ((at.x - x) * (at.x - x) + (at.z - z) * (at.z - z) <= InsertionRange * InsertionRange) insertion = true;
        }
    }
}
