namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The local faction's nation (factories, policies, focus tree) for the STR and FAC screens. Published by the Nation module;
    /// absent when the module is off. The host owns every number: a press is a request, and the answer shows in the next view.
    /// </summary>
    internal interface INationView
    {
        /// <summary>The layer runs in this mission and a view of the local faction has arrived.</summary>
        bool Active { get; }

        /// <summary>The local faction's nation, or null while <see cref="Active"/> is false. Timers are seconds left, kept current.</summary>
        NationView Local { get; }

        /// <summary>
        /// Asks the host (or applies at once when this peer is the host). The return value is the host's answer on the host and
        /// <see cref="NationOutcome.Ok"/> for "sent" on a client, whose real answer arrives through <see cref="LastOutcome"/>.
        /// </summary>
        NationOutcome Request(NationVerb verb, int a = 0, int b = 0);

        /// <summary>The newest answer to a request of the local player and the unscaled time it arrived (0 before the first).</summary>
        NationOutcome LastOutcome { get; }
        float LastOutcomeAt { get; }
    }

    /// <summary>What a finished focus does to a number another module owns. Absent service or unknown faction reads 1.</summary>
    internal interface INationModifiers
    {
        float Scale(FactionHQ hq, NationStat stat);
    }

    /// <summary>The one-line read for the owners of those numbers: 1 when the Nation module is absent or off.</summary>
    internal static class NationScale
    {
        public static float Of(FactionHQ hq, NationStat stat) =>
            Services.ModuleServices.TryGet(out INationModifiers nation) ? nation.Scale(hq, stat) : 1f;
    }
}
