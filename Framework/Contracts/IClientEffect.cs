using System.Collections.Generic;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Client effect lifecycle and diagnostics. The effect stays in its module and keeps
    /// its own settings and ticking. Shared render targets and voices have global caps.
    /// </summary>
    internal interface IClientEffect
    {
        /// <summary>Stable lowercase id, e.g. "canopy" or "rain-field".</summary>
        string EffectId { get; }

        /// <summary>Declared cost for diagnostics; individual limits are owner-enforced.</summary>
        FxBudget Budget { get; }

        /// <summary>Release every allocation (RTs, clips, materials, volumes). Idempotent.</summary>
        void ReleaseFx();

        /// <summary>Current live cost for readouts: ms, bytes, voices, quality tier.</summary>
        void DescribeFx(IDictionary<string, object> state);
    }
}
