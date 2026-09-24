using System;

namespace BoscaliSummer.Features.Support.Domain.Cyber
{
    /// <summary>Wire-stable kind of a hackable map location. One byte.</summary>
    internal enum LocationKind : byte
    {
        None = 0,

        /// <summary>An enemy-held airfield: leans computing and reach.</summary>
        Airfield = 1,

        /// <summary>A city: leans intel and coverage.</summary>
        City = 2
    }

    /// <summary>Wire-stable node kind. Home nodes are the host's; the rest are hacked locations.</summary>
    internal enum NodeKind : byte
    {
        None = 0,

        /// <summary>Cyber Command: the root, on the faction's central owned airbase.</summary>
        Command = 1,

        /// <summary>Another owned airbase: reach and computing, no stages.</summary>
        Base = 2,

        /// <summary>A hacked enemy airfield.</summary>
        Airfield = 3,

        /// <summary>A hacked city.</summary>
        City = 4
    }

    /// <summary>What money can improve. Never an ability.</summary>
    internal enum CyberUpgrade : byte
    {
        /// <summary>Every hacked location's ability radius grows.</summary>
        Radius = 0,

        /// <summary>Network reach grows, so farther locations can be breached.</summary>
        Reach = 1,

        /// <summary>Every location's computing and intel income grows.</summary>
        Income = 2,

        /// <summary>Breach trace accrues more slowly.</summary>
        Trace = 3
    }

    /// <summary>The phases of one breach. The session runs Probe then Exploit then Extract.</summary>
    internal enum BreachPhase : byte
    {
        None = 0,
        Probe = 1,
        Exploit = 2,
        Extract = 3
    }

    /// <summary>What a CyberBreach order asks for. Wire-stable.</summary>
    internal enum BreachTool : byte
    {
        /// <summary>Open a session and run this phase quiet.</summary>
        Quiet = 0,

        /// <summary>Open a session and run this phase loud.</summary>
        Force = 1,

        /// <summary>Run the next phase quiet.</summary>
        RetuneQuiet = 2,

        /// <summary>Run the next phase loud.</summary>
        RetuneForce = 3,

        /// <summary>Burn computing to knock the trace back.</summary>
        Spoof = 4,

        /// <summary>Leave the session safely; nothing is taken.</summary>
        Disconnect = 5
    }

    /// <summary>Why a breach order is refused, in check order.</summary>
    internal enum BreachDenial : byte
    {
        None = 0,
        NoCommand,
        NoTarget,
        NotHackable,
        AlreadyMine,
        OutOfReach,
        Locked,
        Running,
        NotRunning,
        NoSession,
        LowComputing,
        Recharging,
        AwaitingChoice
    }

    internal readonly struct StageInfo
    {
        public readonly string Name;
        public readonly float Computing;
        public readonly float Intel;
        public readonly float Radius;

        public StageInfo(string name, float computing, float intel, float radius)
        {
            Name = name;
            Computing = computing;
            Intel = intel;
            Radius = radius;
        }
    }

    /// <summary>
    /// The one table that decides what a hacked location gives and what a breach costs. The
    /// model, the host, the panel and the pure tests all read it. A location has four stages;
    /// each successful breach is one stage, and each stage is harder than the last.
    /// </summary>
    internal static class CyberLocations
    {
        public const int StageCount = 4;
        public const int MaximumTargets = 16;
        public const int MaximumBases = 8;
        public const int SlotCount = 1 + MaximumBases + MaximumTargets;
        public const int TargetBase = 1 + MaximumBases;
        public const int UpgradeLevels = 3;

        /// <summary>Home nodes: Cyber Command plus the owned airbases.</summary>
        public const int HomeSlots = 1 + MaximumBases;

        /// <summary>Base network reach before upgrades, metres; the host setting overrides.</summary>
        public const float DefaultReach = 30000f;

        public const float ReachPerLevel = 0.25f;
        public const float RadiusPerLevel = 1500f;
        public const float IncomePerLevel = 0.15f;
        public const float TracePerLevel = 0.10f;

        public const float ComputingBaseCapacity = 100f;
        public const float ComputingCapacityPerNode = 25f;
        public const float IntelBaseCapacity = 60f;
        public const float IntelCapacityPerStage = 15f;

        public const float CommandComputing = 1f;
        public const float BaseComputing = 0.3f;

        public const float ProbeSeconds = 5f;
        public const float ExploitSeconds = 9f;
        public const float ExtractSeconds = 7f;
        public const float ForceDurationScale = 0.55f;
        public const float ForceCostScale = 1.6f;
        public const float ForceTraceScale = 1.6f;

        public const float ProbeCost = 10f;
        public const float ExploitCostPerStage = 20f;
        public const float ExtractCostPerStage = 12f;

        public const float ProbeTrace = 0.08f;
        public const float ExploitTrace = 0.35f;
        public const float ExtractTrace = 0.25f;

        public const float StageTraceScale = 0.10f;
        public const float DistanceTraceScale = 0.25f;
        public const float SpoofTrace = 0.25f;
        public const float SpoofCost = 15f;
        public const float SpoofRecharge = 30f;
        public const float LockoutSeconds = 300f;
        public const float PendingChoiceSeconds = 120f;

        public const float PatchSeconds = 8f;
        public const float HoneypotSeconds = 60f;
        public const float SelfRepairSeconds = 120f;
        public const float FootholdSeconds = 240f;
        public const float ContainSeconds = 40f;
        public const float TraceSeconds = 20f;

        public static readonly string[] StageNames = { "—", "FOOTHOLD", "CONTROL", "DOMINION", "MASTERY" };

        public static readonly float[] UpgradeCosts =
        {
            800f, 1300f, 1900f,   // radius
            700f, 1200f, 1800f,   // reach
            900f, 1400f, 2000f,   // income
            1000f, 1500f, 2200f   // trace
        };

        public static string UpgradeName(CyberUpgrade upgrade)
        {
            switch (upgrade)
            {
                case CyberUpgrade.Radius: return "ABILITY RADIUS";
                case CyberUpgrade.Reach: return "NETWORK REACH";
                case CyberUpgrade.Income: return "RESOURCE YIELD";
                default: return "TRACE RESISTANCE";
            }
        }

        public static string UpgradeEffect(CyberUpgrade upgrade)
        {
            switch (upgrade)
            {
                case CyberUpgrade.Radius: return "+" + (int)RadiusPerLevel + " m on every location";
                case CyberUpgrade.Reach: return "+" + (int)(ReachPerLevel * 100f) + " % breach reach";
                case CyberUpgrade.Income: return "+" + (int)(IncomePerLevel * 100f) + " % resource income";
                default: return "−" + (int)(TracePerLevel * 100f) + " % trace per phase";
            }
        }

        public static float UpgradeCost(CyberUpgrade upgrade, int level) =>
            level < 0 || level >= UpgradeLevels ? 0f : UpgradeCosts[(int)upgrade * UpgradeLevels + level];

        /// <summary>Income, radius and tier of a location at a stage. Stage 0 is nothing.</summary>
        public static StageInfo Stage(LocationKind kind, int stage)
        {
            int s = Math.Max(0, Math.Min(StageCount, stage));
            if (s == 0) return new StageInfo("—", 0f, 0f, 0f);
            bool city = kind == LocationKind.City;
            float computing = (city ? 0.25f : 0.4f) * s + (city ? 0f : 0.2f) * (s - 1);
            float intel = s >= 2 ? (city ? 0.15f : 0.1f) * (s - 1) : 0f;
            float radius = (city ? 5000f : 4000f) + (city ? 3000f : 2000f) * (s - 1);
            return new StageInfo(StageNames[s], computing, intel, radius);
        }

        /// <summary>0 none, 1 basic, 2 mid, 3 capstone: the highest ability tier a stage opens.</summary>
        public static int Tier(int stage) => Math.Max(0, Math.Min(3, stage - 1));

        public static string KindName(LocationKind kind) =>
            kind == LocationKind.City ? "CITY" : kind == LocationKind.Airfield ? "AIRFIELD" : "—";

        /// <summary>
        /// True for the map's city building sets. The same <c>MapBuildingSet</c> component also
        /// dresses power lines, pylons, wind and solar farms and lighthouses, which are not
        /// hackable locations; only a set the map names for a city counts.
        /// </summary>
        public static bool CitySetName(string name) =>
            !string.IsNullOrEmpty(name) && name.IndexOf("city", StringComparison.OrdinalIgnoreCase) >= 0;

        public static string NodeName(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Command: return "CYBER COMMAND";
                case NodeKind.Base: return "AIRBASE NODE";
                case NodeKind.Airfield: return "HACKED AIRFIELD";
                case NodeKind.City: return "HACKED CITY";
                default: return "—";
            }
        }

        public static bool Known(byte kind) => kind <= (byte)NodeKind.City;

        public static bool Hackable(LocationKind kind) =>
            kind == LocationKind.Airfield || kind == LocationKind.City;

        /// <summary>True when a node kind is the host's own infrastructure.</summary>
        public static bool IsHome(NodeKind kind) => kind == NodeKind.Command || kind == NodeKind.Base;

        /// <summary>True when a node kind is a hacked location.</summary>
        public static bool IsHacked(NodeKind kind) => kind == NodeKind.Airfield || kind == NodeKind.City;

        public static LocationKind LocationOf(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Airfield: return LocationKind.Airfield;
                case NodeKind.City: return LocationKind.City;
                default: return LocationKind.None;
            }
        }
    }
}
