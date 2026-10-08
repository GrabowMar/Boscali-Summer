namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Narrow seam for the missile tracker and missile view (HUD module owns both): how many
    /// own missiles are in flight, what the C-menu should show, and the follow-camera actions.
    /// Client-local, read-only except for the camera switch itself.
    /// </summary>
    internal interface IMissileView
    {
        /// <summary>Tracker is live (own aircraft known, registry readable).</summary>
        bool Available { get; }

        /// <summary>Camera is currently following one of our missiles.</summary>
        bool Active { get; }

        /// <summary>Own missiles in flight right now.</summary>
        int Count { get; }

        /// <summary>Short status for the C-menu ("2 MSL · CLOSEST 1.2KM"). Empty when none.</summary>
        string Status { get; }

        /// <summary>Current follow target ("MSL 1/2 AIM-120"). Empty when not following.</summary>
        string CurrentLabel { get; }

        bool CanEnter { get; }

        void Enter();

        void Next();

        void Prev();

        void Exit();
    }
}
