using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;

namespace BoscaliSummer.Modules.Chatter.Runtime
{
    /// <summary>
    /// The command net's publisher: watches what the theater modules already show (the rolling event, the active operation's phase, a friendly insertion touching down
    /// nearby) and raises the matching <c>command.*</c> event when one changes. The first look at each is a baseline: joining a mission announces nothing that was already so.
    /// </summary>
    internal sealed class CommandNet
    {
        private const float InsertionRange = 20000f;

        private string worldEvent;
        private float worldEventStarted;
        private int operationId;
        private TheaterPhase operationPhase;
        private bool eventBaseline, operationBaseline, insertion;
        private IAirAssaultObservation assault;
        private Aircraft player;

        public void Reset()
        {
            if (assault != null) assault.Landed -= InsertionLanded;
            assault = null;
            player = null;
            worldEvent = null;
            worldEventStarted = 0f;
            operationId = 0;
            operationPhase = TheaterPhase.Awaiting;
            eventBaseline = operationBaseline = insertion = false;
        }

        public void Tick(IChatter chatter, Aircraft me)
        {
            player = me;
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
                chatter.Raise("command.insertion", chatter.Station(ChatterNet.Command, "GROUND CONTROL"));
            }
            if (ModuleServices.TryGet<IActiveEventsView>(out var events) && events.Available)
            {
                ActiveEventView current = events.Current;
                if (eventBaseline && current != null && (current.Id != worldEvent || current.StartedAtMissionTime != worldEventStarted))
                    chatter.Raise("command.event", chatter.Station(ChatterNet.Command, "THEATER CONTROL"), "event:" + current.Id, ("title", current.Title));
                worldEvent = current?.Id;
                worldEventStarted = current?.StartedAtMissionTime ?? 0f;
                eventBaseline = true;
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

        private void InsertionLanded(int faction, float x, float z, int shell)
        {
            if (player == null || player.NetworkHQ == null || player.NetworkHQ.GetInstanceID() != faction || !float.IsFinite(x) || !float.IsFinite(z)) return;
            var at = player.GlobalPosition();
            if ((at.x - x) * (at.x - x) + (at.z - z) * (at.z - z) <= InsertionRange * InsertionRange) insertion = true;
        }
    }
}
