namespace BoscaliSummer.Features.Intel.Domain
{
    internal enum ForceRole : byte
    {
        Other = 0,
        Mbt,
        Ifv,
        Apc,
        AntiTank,
        AirDefence,
        Artillery,
        Truck,
        Emplacement
    }

    /// <summary>
    /// Ground power per unit role for area intel, keyed by the unit definition's short code
    /// (MBT, IFV, SAM IR, …) with the role identity as the fallback. Moves to Core in S5, when
    /// TheaterOps' staff becomes its second consumer.
    /// </summary>
    internal static class ForceRoles
    {
        public static ForceRole Classify(string code, bool building, float antiSurface, float antiAir)
        {
            switch (code)
            {
                case "MBT": return ForceRole.Mbt;
                case "IFV": return ForceRole.Ifv;
                case "APC":
                case "MRAP": return ForceRole.Apc;
                case "AT":
                case "ATGM": return ForceRole.AntiTank;
                case "SAM IR":
                case "SAM R":
                case "SPAAG":
                case "AA":
                case "AAA":
                case "MANPADS":
                case "LADS": return ForceRole.AirDefence;
                case "ARTY":
                case "MLRS":
                case "TBM": return ForceRole.Artillery;
                case "TRK":
                case "AMMO":
                case "FUEL":
                case "CMD":
                case "RDR":
                case "CEV": return ForceRole.Truck;
            }
            if (building) return ForceRole.Emplacement;
            if (antiSurface >= 0.5f) return ForceRole.AntiTank;
            if (antiAir >= 0.5f) return ForceRole.AirDefence;
            return ForceRole.Other;
        }

        /// <summary>MBT 1.2, IFV 1.0, APC 0.6, AT 1.0, AD 0.8 (ground weight only), artillery 0.5, truck 0.1, emplacement 0.5; other 0.3.</summary>
        public static float GroundWeight(ForceRole role)
        {
            switch (role)
            {
                case ForceRole.Mbt: return 1.2f;
                case ForceRole.Ifv: return 1.0f;
                case ForceRole.Apc: return 0.6f;
                case ForceRole.AntiTank: return 1.0f;
                case ForceRole.AirDefence: return 0.8f;
                case ForceRole.Artillery: return 0.5f;
                case ForceRole.Truck: return 0.1f;
                case ForceRole.Emplacement: return 0.5f;
                default: return 0.3f;
            }
        }
    }
}
