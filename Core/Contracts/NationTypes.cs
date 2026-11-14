using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    // Engine-free types of the NATION layer (factories, policies, focus tree). Linked by tests/Nation.Tests.

    internal enum EconomyPolicy : byte { Civilian, Balanced, War }

    internal enum PriorityPolicy : byte { Air, Balanced, Ground }

    /// <summary>Branch-major order: INDUSTRY 0-3, MILITARY 4-7, STATE 8-11; the tier is the index within the branch.</summary>
    internal enum FocusId : byte
    {
        NewWorks, WarIndustry, StateContracts, TotalMobilisation,
        FighterReserve, SupplyCorps, ArmouredReserve, AirArmada,
        NationalUnity, WarBonds, SignalsBureau, BlackBudget,
    }

    /// <summary>A number another module owns that a focus scales; read through <see cref="INationModifiers"/>.</summary>
    internal enum NationStat : byte { ConvoyCost, WingCallCost, FrontIncome, PerkPrice }

    internal enum LineKind : byte { Idle, Aircraft, Vehicle, Construction }

    internal enum NationOutcome : byte
    {
        Ok, Off, Locked, NotOpen, NoFunds, NoSlot, NoSite, Busy, Unknown, NotMember,
    }

    /// <summary>What a client may ask of the host. A and B are small integers whose meaning the kind fixes.</summary>
    internal enum NationVerb : byte
    {
        /// <summary>A = <see cref="EconomyPolicy"/>.</summary>
        Economy,
        /// <summary>A = <see cref="PriorityPolicy"/>.</summary>
        Priority,
        /// <summary>A = <see cref="FocusId"/>: starts it when nothing runs, else queues it.</summary>
        Focus,
        /// <summary>Clears the queued focus.</summary>
        Unqueue,
        /// <summary>A = plant id, B = stock row index (or -1 for CONSTRUCTION): pins the plant to that line.</summary>
        Line,
        /// <summary>A = plant id: hands the plant back to the staff.</summary>
        Unpin,
        Build,
    }

    internal struct PlantView
    {
        public int Id;
        public LineKind Kind;
        /// <summary>Index into <see cref="NationView.Stock"/>, -1 when idle or building.</summary>
        public int Row;
        public bool Pinned;
        /// <summary>Seconds to the next unit, 0 when resting.</summary>
        public float NextIn;
        public float Interval;
        public float Health01;
        /// <summary>GLOBAL metres.</summary>
        public float X, Z;
    }

    internal struct StockView
    {
        public string Name;
        public string Code;
        public bool Aircraft;
        public int Count, Target;
    }

    /// <summary>One faction's nation as a screen draws it. Times are seconds left, never clock values.</summary>
    internal sealed class NationView
    {
        public int Slots;
        public float Output = 1f, Stability = 50f, Funds, FactoryCost;
        public EconomyPolicy Economy = EconomyPolicy.Balanced;
        public PriorityPolicy Priority = PriorityPolicy.Balanced;
        public float EconomyLock, PriorityLock;
        public ushort Done;
        /// <summary>-1 when none.</summary>
        public int Running = -1, Queued = -1;
        public float RunningLeft, RunningTotal;
        public string PickHolder = "";
        public float PickLock;
        public bool Building;
        public float BuildLeft, BuildTotal;
        public int Escalation;
        public readonly List<PlantView> Plants = new List<PlantView>(12);
        public readonly List<StockView> Stock = new List<StockView>(24);
    }
}
