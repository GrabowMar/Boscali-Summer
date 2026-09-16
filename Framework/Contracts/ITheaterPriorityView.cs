using System.Collections.Generic;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// The faction's main effort, published by the TheaterOps module and consumed by the
    /// STR console's CMD page.
    ///
    /// <para>The priority names one of the faction's active objectives. Friendly AI
    /// reinforcements are delivered nearer it and units with no better order head for it;
    /// it never selects, moves or retasks a unit itself. The view is scoped to the local
    /// faction. Only the host may change it — a remote client reads the host-set state once
    /// replication lands and otherwise says so instead of showing a confident "none".</para>
    /// </summary>
    internal interface ITheaterPriorityView
    {
        /// <summary>Whether the priority system is installed and running.</summary>
        bool Available { get; }

        /// <summary>True on the host, where a priority can actually be set.</summary>
        bool CanCommand { get; }

        /// <summary>The reason the view cannot be used, in one sentence. Never null when unavailable.</summary>
        string Status { get; }

        bool HasPriority { get; }
        string PriorityLabel { get; }

        /// <summary>Active objectives the local faction may name as its main effort.</summary>
        IReadOnlyList<TheaterPriorityOption> Options { get; }

        /// <summary>Marks the view as observed so the option list stays fresh. Called from the panel refresh.</summary>
        void Refresh();

        /// <summary>Intent: set the main effort to an objective from the last <see cref="Options"/>. Host only.</summary>
        bool RequestPriority(string key);

        /// <summary>Intent: clear the main effort. Host only.</summary>
        bool RequestClear();
    }

    /// <summary>One selectable objective: the identity to send back, its display copy and whether it is the main effort.</summary>
    internal sealed class TheaterPriorityOption
    {
        public string Key { get; }
        public string Label { get; }
        public string Detail { get; }
        public bool Selected { get; }

        public TheaterPriorityOption(string key, string label, string detail, bool selected)
        {
            Key = key;
            Label = label;
            Detail = detail;
            Selected = selected;
        }
    }
}
