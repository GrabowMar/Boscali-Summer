using System.Collections.Generic;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>Where an offensive is in its own life: gather, plan, aim, launch, fight, report.</summary>
    internal enum TheaterOperationPhase : byte
    {
        Mustering = 0,
        Planning = 1,
        AwaitingTarget = 2,
        Launching = 3,
        Assault = 4,

        /// <summary>Every funded wave is on the road; the push holds for reinforcement or a result.</summary>
        Holding = 5,

        Concluded = 6,
    }

    /// <summary>How an offensive ended. <see cref="None"/> while it is still running.</summary>
    internal enum TheaterOperationOutcome : byte
    {
        None = 0,

        /// <summary>The committed waves were delivered and the assault ran its course.</summary>
        CommitmentSpent = 1,

        /// <summary>The assault window elapsed without the objective closing.</summary>
        Stalled = 2,

        /// <summary>The objective is ours.</summary>
        ObjectiveSecured = 3,

        /// <summary>The objective left the board without being secured.</summary>
        ObjectiveLost = 4,

        /// <summary>The commander called it off before the waves were spent.</summary>
        Cancelled = 5,
    }

    /// <summary>What the theater director is doing with the faction's war right now.</summary>
    internal enum TheaterDirectorPosture : byte
    {
        Idle = 0,
        Attacking = 1,
        Defending = 2,
        Holding = 3,
    }

    /// <summary>
    /// The director's posture as the board may state it: what it is doing, where the effort
    /// points and why, and how many offensives are on the road.
    /// </summary>
    internal sealed class TheaterDirectionView
    {
        public TheaterDirectorPosture Posture { get; }
        public bool EffortIsDefense { get; }
        public string DefenseLabel { get; }
        public int ActivePlans { get; }

        public TheaterDirectionView(
            TheaterDirectorPosture posture, bool effortIsDefense, string defenseLabel, int activePlans)
        {
            Posture = posture;
            EffortIsDefense = effortIsDefense;
            DefenseLabel = defenseLabel;
            ActivePlans = activePlans;
        }
    }

    /// <summary>One axis lean: the objective key and its favor/avoid weight, -1..1.</summary>
    internal sealed class TheaterAxisView
    {
        public string Key { get; }
        public float Weight { get; }

        public TheaterAxisView(string key, float weight)
        {
            Key = key;
            Weight = weight;
        }
    }

    /// <summary>
    /// The faction's standing orders to the director: stance, hold, chest, axes, and who
    /// set them last. A snapshot; mutating it changes nothing — intents do.
    /// </summary>
    internal sealed class TheaterInfluenceView
    {
        public float Stance { get; }
        public bool HoldOffense { get; }
        public float MaxEscrowPerPlan { get; }
        public float ReserveFloor { get; }
        public string Setter { get; }
        public IReadOnlyList<TheaterAxisView> Axes { get; }

        public TheaterInfluenceView(
            float stance, bool holdOffense, float maxEscrowPerPlan, float reserveFloor,
            string setter, IReadOnlyList<TheaterAxisView> axes)
        {
            Stance = stance;
            HoldOffense = holdOffense;
            MaxEscrowPerPlan = maxEscrowPerPlan;
            ReserveFloor = reserveFloor;
            Setter = setter;
            Axes = axes;
        }
    }

    /// <summary>
    /// The theater director's war, published by the TheaterOps module and consumed by the STR
    /// console's OPERATIONS page.
    ///
    /// <para>An operation is a staff plan, not a unit order. The director opens it,
    /// names its objective, sizes and funds its waves from the shared faction pool, and
    /// launches it; the offensive then runs itself through the vanilla supply path and the
    /// faction's main effort, which the director also owns. When the last funded wave is
    /// on the road the plan holds, and the director funds more or lets it conclude.
    /// Nothing is selected, spawned or micro-managed here, and the unspent escrow always
    /// returns to the pool.</para>
    ///
    /// <para>Players conduct; they never command. Stance, hold, chest and axes are the
    /// faction's standing orders — any faction member may set them, last writer wins, and
    /// the host derives the faction and the setter from the sender. A remote client
    /// receives the host's board, posture, influence and staff log read-only over the
    /// TheaterOps transport.</para>
    /// </summary>
    internal interface ITheaterOperationsView
    {
        bool Available { get; }
        bool CanCommand { get; }
        string Status { get; }

        /// <summary>Pool cost of the staff work behind one offensive, in millions.</summary>
        float OverheadCost { get; }

        /// <summary>Pool cost of one wave slot, in millions. Every slot escrows this much.</summary>
        float WaveBudget { get; }

        /// <summary>The wave slots one offensive may carry, the ceiling the board draws.</summary>
        int MaximumWaves { get; }

        /// <summary>The local faction's offensives, newest first, bounded by the module's ceiling.</summary>
        IReadOnlyList<TheaterOperationView> Operations { get; }

        /// <summary>What the director is doing with the faction's war right now.</summary>
        TheaterDirectionView Direction { get; }

        /// <summary>The faction's standing orders to the director and who set them last.</summary>
        TheaterInfluenceView Influence { get; }

        /// <summary>The staff log, newest first, bounded by the module's ceiling.</summary>
        IReadOnlyList<string> StaffLog { get; }

        /// <summary>Marks the view as observed so it stays fresh. Called from the panel refresh.</summary>
        void Refresh();

        /// <summary>Intent: lean the staff toward attack (1) or defense (0). Any faction member.</summary>
        bool RequestStance(float stance);

        /// <summary>Intent: hold new offensives, or release them. Defense still runs. Any faction member.</summary>
        bool RequestHold(bool hold);

        /// <summary>Intent: cap the escrow per plan and the pool reserve, in millions. Any faction member.</summary>
        bool RequestChest(float maxEscrowPerPlan, float reserveFloor);

        /// <summary>Intent: favor (1) or avoid (-1) one objective; near-zero clears it. Any faction member.</summary>
        bool RequestAxis(string objectiveKey, float weight);

    }

    /// <summary>One offensive as the board may state it. Figures are the host's own.</summary>
    internal sealed class TheaterOperationView
    {
        public string Name { get; }
        public TheaterOperationPhase Phase { get; }
        public TheaterOperationOutcome Outcome { get; }

        /// <summary>The named objective, or null while the plan still awaits one.</summary>
        public string TargetLabel { get; }

        /// <summary>Progress through the current phase, 0..1. Full once the plan is ready.</summary>
        public float Progress { get; }

        /// <summary>Unspent budget still escrowed for the waves, in millions.</summary>
        public float Budget { get; }

        /// <summary>Everything the operation has taken from the pool, in millions.</summary>
        public float Committed { get; }

        /// <summary>What the delivered waves actually drew, in millions.</summary>
        public float Spent { get; }

        /// <summary>Seconds since H-hour, through the assault and the hold; frozen once concluded.</summary>
        public float ElapsedSeconds { get; }

        /// <summary>Seconds until the hold ends on its own, or -1 when no hold is running.</summary>
        public float HoldRemaining { get; }

        public int WavesPlanned { get; }
        public int WavesLaunched { get; }

        /// <summary>Seconds to H-hour while launching, or -1 when no countdown is running.</summary>
        public float Countdown { get; }

        /// <summary>
        /// Who holds the faction's main effort when this operation is due at H-hour but another
        /// push has the order; null when the operation is free to launch.
        /// </summary>
        public string Holder { get; }

        public bool IsHeld => !string.IsNullOrEmpty(Holder);

        /// <summary>Escrow that went back to the pool: everything committed that no wave drew.</summary>
        public float Returned => Committed > Spent ? Committed - Spent : 0f;

        public TheaterOperationView(
            string name, TheaterOperationPhase phase, TheaterOperationOutcome outcome,
            string targetLabel, float progress, float budget, float committed, float spent,
            float elapsedSeconds, float holdRemaining,
            int wavesPlanned, int wavesLaunched, float countdown, string holder)
        {
            Name = name;
            Phase = phase;
            Outcome = outcome;
            TargetLabel = targetLabel;
            Progress = progress;
            Budget = budget;
            Committed = committed;
            Spent = spent;
            ElapsedSeconds = elapsedSeconds;
            HoldRemaining = holdRemaining;
            WavesPlanned = wavesPlanned;
            WavesLaunched = wavesLaunched;
            Countdown = countdown;
            Holder = holder;
        }
    }
}
