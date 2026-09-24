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
    /// faction. The theater director owns it outright — players lean on axes through the
    /// operations view, never by setting the effort — and a remote client reads the
    /// host-set state once replication lands, otherwise saying so instead of showing a
    /// confident "none".</para>
    /// </summary>
    internal interface ITheaterPriorityView
    {
        /// <summary>Whether the priority system is installed and running.</summary>
        bool Available { get; }

        /// <summary>The reason the view cannot be used, in one sentence. Never null when unavailable.</summary>
        string Status { get; }

        bool HasPriority { get; }
        string PriorityLabel { get; }

        /// <summary>Active objectives the director scores and the axes lean on.</summary>
        IReadOnlyList<TheaterPriorityOption> Options { get; }

        /// <summary>Marks the view as observed so the option list stays fresh. Called from the panel refresh.</summary>
        void Refresh();
    }

    /// <summary>One objective on the axes list: the identity to send back and its display copy.</summary>
    internal sealed class TheaterPriorityOption
    {
        public string Key { get; }
        public string Label { get; }
        public string Detail { get; }
        /// <summary>Global, non-hidden objective map point; NaN when a source did not provide it.</summary>
        public float X { get; }
        public float Z { get; }

        public TheaterPriorityOption(string key, string label, string detail,
            float x = float.NaN, float z = float.NaN)
        {
            Key = key;
            Label = label;
            Detail = detail;
            X = x;
            Z = z;
        }
    }
}
