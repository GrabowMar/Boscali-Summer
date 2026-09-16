using System.Collections.Generic;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Faction reinforcement funding and the theater's readiness picture, published by the
    /// TheaterOps module and consumed by the STR console's CMD page.
    ///
    /// <para>Reinforcement is the vanilla convoy path: the host spends the shared faction
    /// pool, the mission's own convoy group enters the supply queue, and the game deploys it
    /// from the depot nearest the main effort. No unit is placed, selected or ordered here.
    /// Readiness is a local read of the vanilla rearm network and reads as unobserved — not
    /// as zero — when that network cannot be inspected.</para>
    /// </summary>
    internal interface ITheaterLogisticsView
    {
        bool Available { get; }
        bool CanCommand { get; }
        string Status { get; }

        /// <summary>Shared faction pool in millions, or NaN when it could not be read.</summary>
        float FactionFunds { get; }

        IReadOnlyList<ReinforcementOption> Reinforcements { get; }
        ReadinessSummary Readiness { get; }

        /// <summary>Marks the view as observed so the board stays fresh. Called from the panel refresh.</summary>
        void Refresh();

        /// <summary>Intent: fund one convoy group from the faction pool. Host only.</summary>
        bool RequestReinforcement(string key);
    }

    /// <summary>One mission-defined convoy group and whether the host can afford to call it now.</summary>
    internal sealed class ReinforcementOption
    {
        public string Key { get; }
        public string Label { get; }
        public string Detail { get; }
        public float Cost { get; }
        public bool Ready { get; }
        public bool Affordable { get; }
        public float CooldownSeconds { get; }

        public ReinforcementOption(
            string key, string label, string detail, float cost,
            bool ready, bool affordable, float cooldownSeconds)
        {
            Key = key;
            Label = label;
            Detail = detail;
            Cost = cost;
            Ready = ready;
            Affordable = affordable;
            CooldownSeconds = cooldownSeconds;
        }
    }

    /// <summary>
    /// Local observation of the vanilla rearm network. <see cref="Observed"/> false means the
    /// network was not inspectable and every count must read as a dash.
    /// </summary>
    internal readonly struct ReadinessSummary
    {
        public bool Observed { get; }
        public int UnitsAwaitingRearm { get; }
        public int RearmAssets { get; }
        public int RearmAvailable { get; }
        public int RearmDepleted { get; }

        public ReadinessSummary(
            bool observed, int unitsAwaitingRearm, int rearmAssets,
            int rearmAvailable, int rearmDepleted)
        {
            Observed = observed;
            UnitsAwaitingRearm = unitsAwaitingRearm;
            RearmAssets = rearmAssets;
            RearmAvailable = rearmAvailable;
            RearmDepleted = rearmDepleted;
        }
    }
}
