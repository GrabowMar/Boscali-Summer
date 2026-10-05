using System;
using BoscaliSummer.Modules.Support.Domain.Sof;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The seam OPERATIONS reads on SOF: every SOF event of a faction, after the desk applied it (a mission success pays +40 toward a FOB).</summary>
    internal sealed partial class SofService
    {
        /// <summary>Raised after every SOF event of a faction.</summary>
        internal event Action<FactionHQ, SofEvent> Observed;
    }
}
