using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    /// <summary>Player-facing weight class of a CALL (core §2a: LIGHT / HEAVY / STRATEGIC).</summary>
    internal enum CallTier : byte { Light = 0, Heavy = 1, Strategic = 2 }

    /// <summary>The OPS domain a CALL belongs to (core §4).</summary>
    internal enum CallFamily : byte { Space = 0, Cyber = 1, Sof = 2 }

    internal readonly struct CallRow
    {
        public readonly SupportActionId Id;
        public readonly CallTier Tier;
        public readonly CallFamily Family;
        public readonly string Label;

        public CallRow(SupportActionId id, CallTier tier, CallFamily family, string label)
        {
            Id = id;
            Tier = tier;
            Family = family;
            Label = label;
        }
    }

    /// <summary>
    /// The STANDARD calls of M0: every fire action that survives the old OPS. FLARE BARRAGE and FORTIFY are interim
    /// rows until the CYBER (M3) and SOF (M5) specs replace them.
    /// </summary>
    internal static class CallSheet
    {
        public const int LightPrice = 25, HeavyPrice = 70, StrategicPrice = 400;

        private static readonly CallRow[] rows =
        {
            new CallRow(SupportActionId.Recon, CallTier.Light, CallFamily.Space, "RADAR SCAN"),
            new CallRow(SupportActionId.Prsm, CallTier.Light, CallFamily.Space, "PRSM"),
            new CallRow(SupportActionId.JtacMark, CallTier.Light, CallFamily.Sof, "JTAC LASE"),
            new CallRow(SupportActionId.MtiSweep, CallTier.Heavy, CallFamily.Space, "MTI SWEEP"),
            new CallRow(SupportActionId.ElintSweep, CallTier.Heavy, CallFamily.Space, "ELINT SWEEP"),
            new CallRow(SupportActionId.Cruise, CallTier.Heavy, CallFamily.Space, "CRUISE SALVO"),
            new CallRow(SupportActionId.FlareMissile, CallTier.Heavy, CallFamily.Cyber, "FLARE BARRAGE"),
            new CallRow(SupportActionId.Fortify, CallTier.Heavy, CallFamily.Sof, "FORTIFY"),
            new CallRow(SupportActionId.Artillery, CallTier.Strategic, CallFamily.Space, "ORBITAL ROD"),
            new CallRow(SupportActionId.Emp, CallTier.Strategic, CallFamily.Cyber, "EMP"),
        };

        public static IReadOnlyList<CallRow> Rows => rows;

        public static int BasePrice(CallTier tier) =>
            tier == CallTier.Strategic ? StrategicPrice : tier == CallTier.Heavy ? HeavyPrice : LightPrice;

        public static bool TryGet(SupportActionId id, out CallRow row)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Id != id) continue;
                row = rows[i];
                return true;
            }
            row = default;
            return false;
        }
    }
}
