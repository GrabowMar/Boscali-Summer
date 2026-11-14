using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    // Engine-free types of the NATION layer (factories, policies, politics). Linked by tests/Nation.Tests.

    internal enum EconomyPolicy : byte { Civilian, Balanced, War }

    internal enum PriorityPolicy : byte { Air, Balanced, Ground }

    /// <summary>The path a faction commits to on the WAR CABINET card; the other two close for the match.</summary>
    internal enum Doctrine : byte { None, TotalWar, Fortress, Shadow }

    internal enum LineKind : byte { Idle, Aircraft, Vehicle, Construction }

    internal enum NationOutcome : byte
    {
        Ok, Off, Locked, NotOpen, NoFunds, NoSlot, NoSite, Busy, Unknown, NotMember, NotProduced, NoSupport,
    }

    /// <summary>What a client may ask of the host. A and B are small integers whose meaning the kind fixes.</summary>
    internal enum NationVerb : byte
    {
        /// <summary>A = <see cref="EconomyPolicy"/>.</summary>
        Economy,
        /// <summary>A = <see cref="PriorityPolicy"/>.</summary>
        Priority,
        /// <summary>A = plant id, B = stock row index (or -1 for CONSTRUCTION): pins the plant to that line.</summary>
        Line,
        /// <summary>A = plant id: hands the plant back to the staff.</summary>
        Unpin,
        Build,
        /// <summary>A = the cabinet card's serial, B = the option.</summary>
        Answer,
        /// <summary>A = <see cref="DecisionId"/>: spends war support on it (the first press wins).</summary>
        Decide,
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
        /// <summary>Stopped by an enemy SABOTAGE: makes nothing until the raid ends.</summary>
        public bool Raided;
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

    /// <summary>One reason war support moved: how much, why (a place, a kill, a decision) and how long ago.</summary>
    internal struct SupportMove
    {
        public float Delta;
        public string Text;
        public float Age;
    }

    /// <summary>One faction's nation as a screen draws it. Times are seconds left (or ago), never clock values.</summary>
    internal sealed class NationView
    {
        public int Slots;
        public float Output = 1f, Stability = 50f, Funds, FactoryCost;
        public EconomyPolicy Economy = EconomyPolicy.Balanced;
        public PriorityPolicy Priority = PriorityPolicy.Balanced;
        public float EconomyLock, PriorityLock;
        public bool Building;
        public float BuildLeft, BuildTotal;
        public int Escalation;
        public readonly List<PlantView> Plants = new List<PlantView>(12);
        public readonly List<StockView> Stock = new List<StockView>(24);

        /// <summary>0..100, starts at 40; earned by fighting, spent on decisions.</summary>
        public float WarSupport = 40f;
        /// <summary>Why it moved, newest first (at most four).</summary>
        public readonly List<SupportMove> Moves = new List<SupportMove>(4);
        public Doctrine Doctrine;
        /// <summary>1..3 once a doctrine is chosen (0 before); seconds since it was chosen (-1 before) and who chose.</summary>
        public int Rank;
        public float DoctrineAge = -1f;
        public string DoctrineBy = "";
        /// <summary>The posture band politics allows the staff and pilots (TheaterWarPosture as a byte: 0 cautious .. 2 bold).</summary>
        public byte PostureFloor, PostureCeiling = 2;
        /// <summary>AI jets over (under) the mission's own limit right now.</summary>
        public int JetsDelta;
        /// <summary>Per <see cref="DecisionId"/>: seconds until it can be taken again, and seconds it stays in force.</summary>
        public readonly float[] Cooldown = new float[Decisions.Count], InForce = new float[Decisions.Count];
        /// <summary>The newest decision of this side (-1 none), who took it, how long ago; the serial moves with every decision.</summary>
        public int Decided = -1, DecidedSerial;
        public string DecidedBy = "";
        public float DecidedAge;
        /// <summary>The other side: name, doctrine, rank, war support, its newest decision (-1 none) and its main effort while DEFECTOR runs.</summary>
        public string EnemyName = "", EnemyIntel = "";
        public Doctrine EnemyDoctrine;
        public int EnemyRank, EnemyDecided = -1, EnemyDecidedSerial;
        public float EnemyWarSupport = 40f, EnemyDecidedAge, EnemyDecidedLeft;
        /// <summary>The open cabinet card (serial 0 = none): which, the place it names, seconds to the staff's answer, its marked option, and once answered the option and who chose.</summary>
        public int CabinetSerial, CabinetId = -1, CabinetStaff, CabinetChosen = -1;
        public string CabinetPlace = "", CabinetChosenBy = "";
        public float CabinetLeft;
    }
}
