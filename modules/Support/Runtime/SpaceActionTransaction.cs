using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>Exact faction/scene receipt; a queued coroutine cannot masquerade as a launch.</summary>
    internal sealed class SpaceActionTransaction
    {
        private readonly SpaceService service;
        private readonly FactionHQ owner;
        private readonly SpaceState state;
        private readonly SpaceTaskReservation receipt;
        private readonly int scene;
        private readonly float seconds;
        private bool finished;

        public SpaceActionTransaction(SpaceService service, FactionHQ owner, SpaceState state,
            in SpaceTaskReservation receipt, float seconds)
        {
            this.service = service;
            this.owner = owner;
            this.state = state;
            this.receipt = receipt;
            this.seconds = seconds;
            scene = service.Generation;
        }

        public bool PhysicalLaunch { get; private set; }
        public bool CanLaunch => !finished && service.Generation == scene &&
            service.TryGetState(owner, out SpaceState current) && ReferenceEquals(current, state) &&
            state.CanCommit(receipt, SupportManager.MissionNow(), seconds);

        /// <summary>Call immediately after a real native spawn; false requires deleting that spawn.</summary>
        public bool ReportPhysicalLaunch()
        {
            if (!Commit()) return false;
            PhysicalLaunch = true;
            return true;
        }

        public bool Commit()
        {
            if (!CanLaunch || !state.Commit(receipt, SupportManager.MissionNow(), seconds)) return false;
            finished = true;
            return true;
        }

        public void Cancel()
        {
            if (finished) return;
            state.Cancel(receipt);
            finished = true;
        }
    }
}
