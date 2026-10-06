using System;

namespace BoscaliSummer.Modules.Wing.Domain
{

    /// <summary>Compensate native hangar stock debits already covered by the purchase transaction. Measure
    /// before/after counts because rejected or deferred spawn paths may not debit.</summary>
    internal static class SupplyCompensation
    {
        /// <summary>Nonnegative stock decrease to restore; never remove an unrelated increase.</summary>
        public static int Delta(int before, int after) => Math.Max(0, before - after);
    }
}
