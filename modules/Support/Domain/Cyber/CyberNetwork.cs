using System;

namespace BoscaliSummer.Features.Support.Domain.Cyber
{
    /// <summary>What an operator can do to a node or an incident from the console. Wire-stable.</summary>
    internal enum CyberVerb : byte
    {
        /// <summary>Toggle: cut a node's links. Stops an intrusion there; the node stops working.</summary>
        Isolate = 0,

        /// <summary>Channel a clean image onto a compromised node.</summary>
        Patch = 1,

        /// <summary>Dress a node as bait: an intrusion that reaches it stalls and traces faster.</summary>
        Honeypot = 2,

        /// <summary>Follow an intrusion or detected operation home. Needs a stage-2 location covering it.</summary>
        Trace = 3,

        /// <summary>Overpower a jamming raid with a hacked location inside or beside it.</summary>
        BurnThrough = 4
    }

    /// <summary>Why a verb or a node order is refused, in check order.</summary>
    internal enum CyberDenial : byte
    {
        None = 0,
        NoCommand,
        NoTarget,
        Offline,
        CommandProtected,
        NotCompromised,
        AlreadyPatching,
        AlreadyBaited,
        NeedsEar,
        NeedsCoverage,
        NotTraceable,
        AlreadyTracing,
        LowComputing,
        Recharging
    }

    /// <summary>One slot of the network: Cyber Command, an owned airbase, or a map location.</summary>
    internal struct CyberNode
    {
        public NodeKind Kind;

        /// <summary>1..4 for a hacked location; 0 for home nodes and unhacked targets.</summary>
        public byte Stage;

        public float X, Z;

        /// <summary>Cyber Command or an owned airbase: the host raises and removes it with the base.</summary>
        public bool Static;

        /// <summary>A home node whose anchor building on the airbase is destroyed.</summary>
        public bool Down;

        /// <summary>This faction has breached the location.</summary>
        public bool Hacked;

        public bool Compromised;
        public bool Isolated;

        /// <summary>The stage-4 capstone the location took.</summary>
        public Capstone Capstone;

        public double PatchDone;
        public double HoneypotUntil;
        public double CompromisedAt;

        /// <summary>Set after a backtrace: the location cannot be breached again until it lapses.</summary>
        public double LockoutUntil;

        /// <summary>Host identity of the airbase (home) or location anchor (target). Never on the wire.</summary>
        public int Anchor;
    }

    /// <summary>Network totals the panel and the console show.</summary>
    internal readonly struct CyberStats
    {
        public readonly int Nodes;
        public readonly int Online;
        public readonly int Hacked;
        public readonly int StageTotal;

        public CyberStats(int nodes, int online, int hacked, int stageTotal)
        {
            Nodes = nodes;
            Online = online;
            Hacked = hacked;
            StageTotal = stageTotal;
        }
    }

    /// <summary>
    /// One faction's cyber network: the home nodes the host raises on owned airbases (Cyber
    /// Command plus a relay node on every other base) and the map locations the faction has
    /// breached, each with a stage. Computing fuels breaches and console verbs, intel fuels the
    /// map abilities, and money buys reach, radius, yield and trace resistance — never an
    /// ability. The adversary campaign runs in <c>CyberNetwork.Campaign.cs</c>. Pure: the host
    /// owns every mutation; a client rebuilds from <see cref="CyberSnapshot"/>s.
    /// </summary>
    internal sealed partial class CyberNetwork
    {
        public const int SlotCount = CyberLocations.SlotCount;
        public const int TargetBase = CyberLocations.TargetBase;
        public const int TargetSlots = CyberLocations.MaximumTargets;
        public const int VerbCount = 5;

        private readonly CyberNode[] nodes = new CyberNode[SlotCount];
        private readonly double[] verbReady = new double[VerbCount];
        private readonly double[] capstoneReady = new double[3];
        private readonly int[] upgradeLevels = new int[4];
        private readonly bool[] mirrorSeen = new bool[SlotCount];

        public float Computing { get; private set; }
        public float Intel { get; private set; }

        /// <summary>The host setting for base network reach; upgrades scale it.</summary>
        public float BaseReach { get; set; } = CyberLocations.DefaultReach;

        /// <summary>Host-set: the enemy factions an incident may name, in snapshot order.</summary>
        public int OriginCount { get; set; }

        // ---- Nodes --------------------------------------------------------------------------

        public CyberNode Node(int slot) => slot >= 0 && slot < SlotCount ? nodes[slot] : default;

        public bool Exists(int slot) => slot >= 0 && slot < SlotCount && nodes[slot].Kind != NodeKind.None;

        public bool IsHome(int slot) => Exists(slot) && nodes[slot].Static;

        /// <summary>A target slot this faction has taken.</summary>
        public bool IsHacked(int slot) => Exists(slot) && nodes[slot].Hacked;

        public bool Static(int slot) => IsHome(slot);

        /// <summary>Exists, alive and, for home nodes, with the anchor building standing.</summary>
        public bool Online(int slot) =>
            Exists(slot) && !nodes[slot].Down && (nodes[slot].Static || nodes[slot].Hacked);

        /// <summary>The node is doing its job: online, not isolated, not compromised.</summary>
        public bool Working(int slot) =>
            Online(slot) && !nodes[slot].Isolated && !nodes[slot].Compromised;

        public int Stage(int slot) => IsHacked(slot) ? nodes[slot].Stage : (byte)0;

        /// <summary>0 none, 1 basic, 2 mid, 3 capstone.</summary>
        public int Tier(int slot) => IsHacked(slot) ? CyberLocations.Tier(nodes[slot].Stage) : 0;

        public int CommandSlot
        {
            get
            {
                for (int i = 0; i < SlotCount; i++)
                    if (nodes[i].Kind == NodeKind.Command) return i;
                return -1;
            }
        }

        public bool HasCommand => CommandSlot >= 0;

        public bool CommandOnline
        {
            get
            {
                int slot = CommandSlot;
                return slot >= 0 && Online(slot);
            }
        }

        public bool CommandCompromised
        {
            get
            {
                int slot = CommandSlot;
                return slot >= 0 && nodes[slot].Compromised;
            }
        }

        public int Count(NodeKind kind)
        {
            int count = 0;
            for (int i = 0; i < SlotCount; i++)
                if (nodes[i].Kind == kind) count++;
            return count;
        }

        public int NodeCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < SlotCount; i++)
                    if (nodes[i].Kind != NodeKind.None && (nodes[i].Static || nodes[i].Hacked)) count++;
                return count;
            }
        }

        public int HackedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < SlotCount; i++)
                    if (nodes[i].Hacked) count++;
                return count;
            }
        }

        public int CapstoneCount(Capstone capstone)
        {
            if (capstone == Capstone.None) return 0;
            int count = 0;
            for (int i = 0; i < SlotCount; i++)
                if (nodes[i].Hacked && nodes[i].Capstone == capstone) count++;
            return count;
        }

        public bool AnyCapstone(Capstone capstone) => CapstoneCount(capstone) > 0;

        public CyberStats Stats()
        {
            int count = 0, online = 0, hacked = 0, stages = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (nodes[i].Kind == NodeKind.None) continue;
                if (!nodes[i].Static && !nodes[i].Hacked) continue;
                count++;
                if (Online(i)) online++;
                if (nodes[i].Hacked)
                {
                    hacked++;
                    stages += nodes[i].Stage;
                }
            }
            return new CyberStats(count, online, hacked, stages);
        }

        // ---- Resources ----------------------------------------------------------------------

        public float IncomeScale => 1f + CyberLocations.IncomePerLevel * upgradeLevels[(int)CyberUpgrade.Income];

        public float ComputingIncome()
        {
            float income = 0f;
            int command = CommandSlot;
            if (command >= 0 && Online(command)) income += CyberLocations.CommandComputing;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Online(i) || i == command) continue;
                if (nodes[i].Static) income += CyberLocations.BaseComputing;
                else if (nodes[i].Hacked) income += CyberLocations.Stage(CyberLocations.LocationOf(nodes[i].Kind), nodes[i].Stage).Computing;
            }
            return income * IncomeScale;
        }

        public float IntelIncome()
        {
            float income = 0f;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Online(i) || !nodes[i].Hacked) continue;
                income += CyberLocations.Stage(CyberLocations.LocationOf(nodes[i].Kind), nodes[i].Stage).Intel;
            }
            return income * IncomeScale;
        }

        public float ComputingCapacity() =>
            CyberLocations.ComputingBaseCapacity + CyberLocations.ComputingCapacityPerNode * NodeCount;

        public float IntelCapacity()
        {
            int stages = 0;
            for (int i = 0; i < SlotCount; i++)
                if (nodes[i].Hacked) stages += nodes[i].Stage;
            return CyberLocations.IntelBaseCapacity + CyberLocations.IntelCapacityPerStage * stages;
        }

        public bool SpendComputing(float amount)
        {
            if (amount > 0f && Computing + 0.001f < amount) return false;
            Computing = Math.Max(0f, Computing - Math.Max(0f, amount));
            return true;
        }

        public bool SpendIntel(float amount)
        {
            if (amount > 0f && Intel + 0.001f < amount) return false;
            Intel = Math.Max(0f, Intel - Math.Max(0f, amount));
            return true;
        }

        /// <summary>Host: an ability's loot and the trace reward land here.</summary>
        public void GrantIntel(float amount) =>
            Intel = Math.Min(IntelCapacity(), Intel + Math.Max(0f, amount));

        // ---- Upgrades (money; never an ability) ---------------------------------------------

        public int UpgradeLevel(CyberUpgrade upgrade) => upgradeLevels[(int)upgrade];

        public float UpgradeCost(CyberUpgrade upgrade)
        {
            int level = UpgradeLevel(upgrade);
            return level >= CyberLocations.UpgradeLevels ? 0f : CyberLocations.UpgradeCost(upgrade, level);
        }

        public bool CanUpgrade(CyberUpgrade upgrade) => UpgradeLevel(upgrade) < CyberLocations.UpgradeLevels;

        /// <summary>Authority-free level change; the host checks the price and charges separately.</summary>
        public bool TryUpgrade(CyberUpgrade upgrade)
        {
            if (!CanUpgrade(upgrade)) return false;
            upgradeLevels[(int)upgrade]++;
            return true;
        }

        public float Reach =>
            Math.Max(1000f, BaseReach) * (1f + CyberLocations.ReachPerLevel * upgradeLevels[(int)CyberUpgrade.Reach]);

        /// <summary>Ability radius of a hacked location, upgraded, halved inside a jamming raid.</summary>
        public float RadiusOf(int slot, double now)
        {
            if (!IsHacked(slot)) return 0f;
            float radius = CyberLocations.Stage(CyberLocations.LocationOf(nodes[slot].Kind), nodes[slot].Stage).Radius +
                           CyberLocations.RadiusPerLevel * upgradeLevels[(int)CyberUpgrade.Radius];
            return Jammed(slot, now) ? radius * 0.5f : radius;
        }

        /// <summary>A working hacked location of at least <paramref name="tier"/> whose radius covers the point.</summary>
        public bool AbilityCovers(int tier, float x, float z, double now)
        {
            if (tier <= 0) return false;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Working(i) || Tier(i) < tier) continue;
                float radius = RadiusOf(i, now);
                float dx = nodes[i].X - x, dz = nodes[i].Z - z;
                if (dx * dx + dz * dz <= radius * radius) return true;
            }
            return false;
        }

        /// <summary>Any working location of at least this tier, wherever it is; the panel's readiness hint.</summary>
        public bool AnyTier(int tier)
        {
            for (int i = 0; i < SlotCount; i++)
                if (Working(i) && Tier(i) >= tier) return true;
            return false;
        }

        /// <summary>A working location carrying this capstone whose radius covers the point.</summary>
        public bool CapstoneCovers(Capstone capstone, float x, float z, double now)
        {
            if (capstone == Capstone.None) return false;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Working(i) || nodes[i].Capstone != capstone) continue;
                float radius = RadiusOf(i, now);
                float dx = nodes[i].X - x, dz = nodes[i].Z - z;
                if (dx * dx + dz * dz <= radius * radius) return true;
            }
            return false;
        }

        /// <summary>The working location of at least this tier whose radius covers the point, best first.</summary>
        public bool TryCovering(int tier, float x, float z, double now, out int slot)
        {
            slot = -1;
            if (tier <= 0) return false;
            int bestTier = -1;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Working(i) || Tier(i) < tier || Tier(i) <= bestTier) continue;
                float radius = RadiusOf(i, now);
                float dx = nodes[i].X - x, dz = nodes[i].Z - z;
                if (dx * dx + dz * dz > radius * radius) continue;
                slot = i;
                bestTier = Tier(i);
            }
            return slot >= 0;
        }

        /// <summary>The working capstone location covering the point, best first.</summary>
        public bool TryCovering(Capstone capstone, float x, float z, double now, out int slot)
        {
            slot = -1;
            if (capstone == Capstone.None) return false;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Working(i) || nodes[i].Capstone != capstone) continue;
                float radius = RadiusOf(i, now);
                float dx = nodes[i].X - x, dz = nodes[i].Z - z;
                if (dx * dx + dz * dz > radius * radius) continue;
                slot = i;
                return true;
            }
            return false;
        }

        /// <summary>Capstone recharge left, seconds.</summary>
        public float CapstoneRechargeRemaining(Capstone capstone, double now) =>
            capstone == Capstone.None ? 0f : (float)Math.Max(0.0, capstoneReady[(int)capstone] - now);

        /// <summary>Host: spend a capstone use. The caller has already checked coverage.</summary>
        public bool TryUseCapstone(Capstone capstone, double now)
        {
            if (capstone == Capstone.None || CapstoneRechargeRemaining(capstone, now) > 0f) return false;
            capstoneReady[(int)capstone] = now + Capstones.RechargeSeconds;
            return true;
        }

        /// <summary>A working location's ear: stage 2 or better covering the point.</summary>
        public bool EarCovers(float x, float z, double now) => AbilityCovers(1, x, z, now);

        /// <summary>Any working hacked location covering the point, whatever its stage.</summary>
        public bool HackedCovers(float x, float z, double now)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Working(i) || !nodes[i].Hacked) continue;
                float radius = RadiusOf(i, now);
                float dx = nodes[i].X - x, dz = nodes[i].Z - z;
                if (dx * dx + dz * dz <= radius * radius) return true;
            }
            return false;
        }

        public bool ReachCovers(float x, float z)
        {
            float reach = Reach;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Online(i)) continue;
                float dx = nodes[i].X - x, dz = nodes[i].Z - z;
                if (dx * dx + dz * dz <= reach * reach) return true;
            }
            return false;
        }

        // ---- Breach -------------------------------------------------------------------------

        private int breachTarget = -1;
        private BreachPhase breachPhase;
        private bool breachQuiet = true;
        private float breachTrace;
        private double breachPhaseEnds;
        private int choiceTarget = -1;
        private double choiceDeadline;

        public bool BreachActive => breachTarget >= 0 && breachPhase != BreachPhase.None;
        public int BreachTarget => breachTarget;
        public BreachPhase BreachPhase => breachPhase;
        public bool BreachQuiet => breachQuiet;
        public float BreachTrace => breachTrace;
        public bool BreachAwaitingChoice => choiceTarget >= 0;
        public int ChoiceTarget => choiceTarget;

        /// <summary>Seconds left to pick the capstone before the model picks REVEAL itself.</summary>
        public float ChoiceRemaining(double now) =>
            choiceTarget >= 0 && choiceDeadline > now ? (float)(choiceDeadline - now) : 0f;

        public float BreachPhaseRemaining(double now) =>
            breachPhaseEnds > now ? (float)(breachPhaseEnds - now) : 0f;

        public float LockoutRemaining(int slot, double now) =>
            Exists(slot) && nodes[slot].LockoutUntil > now ? (float)(nodes[slot].LockoutUntil - now) : 0f;

        /// <summary>Computing a phase costs at a stage, in the chosen mode.</summary>
        public static float PhaseCost(BreachPhase phase, int stage, bool quiet)
        {
            float cost;
            switch (phase)
            {
                case BreachPhase.Probe: cost = CyberLocations.ProbeCost; break;
                case BreachPhase.Exploit: cost = CyberLocations.ExploitCostPerStage * Math.Max(1, stage); break;
                case BreachPhase.Extract: cost = CyberLocations.ExtractCostPerStage * Math.Max(1, stage); break;
                default: return 0f;
            }
            return quiet ? cost : cost * CyberLocations.ForceCostScale;
        }

        public static float PhaseSeconds(BreachPhase phase, bool quiet)
        {
            float seconds;
            switch (phase)
            {
                case BreachPhase.Probe: seconds = CyberLocations.ProbeSeconds; break;
                case BreachPhase.Exploit: seconds = CyberLocations.ExploitSeconds; break;
                case BreachPhase.Extract: seconds = CyberLocations.ExtractSeconds; break;
                default: return 0f;
            }
            return quiet ? seconds : seconds * CyberLocations.ForceDurationScale;
        }

        public float SpoofRechargeRemaining(double now) =>
            (float)Math.Max(0.0, capstoneReady[0] - now);

        public BreachDenial CheckBreach(int slot, double now)
        {
            if (!HasCommand || !CommandOnline) return BreachDenial.NoCommand;
            if (!Exists(slot) || slot < TargetBase) return BreachDenial.NoTarget;
            if (!CyberLocations.Hackable(CyberLocations.LocationOf(nodes[slot].Kind))) return BreachDenial.NotHackable;
            // Re-breaching a location you hold is how it climbs a stage; a mastered one is done.
            if (nodes[slot].Hacked && nodes[slot].Stage >= CyberLocations.StageCount) return BreachDenial.AlreadyMine;
            if (nodes[slot].Hacked && !Working(slot)) return BreachDenial.NotHackable;
            if (BreachActive) return BreachDenial.Running;
            if (BreachAwaitingChoice) return BreachDenial.AwaitingChoice;
            if (LockoutRemaining(slot, now) > 0f) return BreachDenial.Locked;
            if (!ReachCovers(nodes[slot].X, nodes[slot].Z)) return BreachDenial.OutOfReach;
            if (Computing + 0.001f < CyberLocations.ProbeCost) return BreachDenial.LowComputing;
            return BreachDenial.None;
        }

        /// <summary>Host: start a breach. Charges the probe phase and runs it to completion on Tick.</summary>
        public BreachDenial TryStartBreach(int slot, bool quiet, double now)
        {
            BreachDenial denial = CheckBreach(slot, now);
            if (denial != BreachDenial.None) return denial;
            if (!SpendComputing(PhaseCost(BreachPhase.Probe, 1, quiet))) return BreachDenial.LowComputing;
            breachTarget = slot;
            breachPhase = BreachPhase.Probe;
            breachQuiet = quiet;
            breachTrace = 0f;
            breachPhaseEnds = now + PhaseSeconds(BreachPhase.Probe, quiet);
            Notify(CyberNotice.BreachStarted, slot, 0, now);
            return BreachDenial.None;
        }

        /// <summary>Host: quiet or force for the next phase.</summary>
        public bool TryBreachMode(bool quiet)
        {
            if (!BreachActive) return false;
            breachQuiet = quiet;
            return true;
        }

        /// <summary>Host: burn computing to knock the trace back.</summary>
        public BreachDenial TrySpoof(double now)
        {
            if (!BreachActive) return BreachDenial.NoSession;
            if (SpoofRechargeRemaining(now) > 0f) return BreachDenial.Recharging;
            if (Computing + 0.001f < CyberLocations.SpoofCost) return BreachDenial.LowComputing;
            SpendComputing(CyberLocations.SpoofCost);
            capstoneReady[0] = now + CyberLocations.SpoofRecharge;
            breachTrace = Math.Max(0f, breachTrace - CyberLocations.SpoofTrace);
            return BreachDenial.None;
        }

        /// <summary>Host: leave the session safely; the computing spent is gone.</summary>
        public bool TryDisconnect(double now)
        {
            if (!BreachActive) return false;
            int slot = breachTarget;
            EndBreach();
            Notify(CyberNotice.BreachDisconnected, slot, 0, now);
            return true;
        }

        /// <summary>Host: pick the capstone a stage-4 location takes.</summary>
        public bool TryChooseCapstone(Capstone capstone, double now)
        {
            if (!BreachAwaitingChoice || !IsHacked(choiceTarget) || nodes[choiceTarget].Stage != CyberLocations.StageCount ||
                nodes[choiceTarget].Capstone != Capstone.None || capstone == Capstone.None || !Capstones.Known((byte)capstone)) return false;
            int slot = choiceTarget;
            nodes[slot].Capstone = capstone;
            choiceTarget = -1;
            choiceDeadline = 0.0;
            Notify(CyberNotice.CapstoneChosen, slot, (byte)capstone, now);
            return true;
        }

        private void EndBreach()
        {
            breachTarget = -1;
            breachPhase = BreachPhase.None;
            breachTrace = 0f;
            breachPhaseEnds = 0.0;
        }

        private float TraceScale(int slot, bool quiet)
        {
            LocationKind kind = CyberLocations.LocationOf(nodes[slot].Kind);
            int stage = Math.Max(1, (int)nodes[slot].Stage + 1);
            float scale = 1f + CyberLocations.StageTraceScale * (stage - 1);
            scale *= 1f - CyberLocations.TracePerLevel * upgradeLevels[(int)CyberUpgrade.Trace];
            // A far target is a weak connection: the defender traces it faster.
            float distance = DistanceToNearest(slot);
            float reach = Math.Max(1f, Reach);
            scale *= 1f - CyberLocations.DistanceTraceScale * Math.Min(1f, distance / reach);
            if (!quiet) scale *= CyberLocations.ForceTraceScale;
            return Math.Max(0.1f, scale);
        }

        private float DistanceToNearest(int slot)
        {
            float best = float.MaxValue;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Online(i) || i == slot) continue;
                float dx = nodes[i].X - nodes[slot].X, dz = nodes[i].Z - nodes[slot].Z;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                if (distance < best) best = distance;
            }
            return best == float.MaxValue ? 0f : best;
        }

        private void StepBreach(double now, float dt)
        {
            if (!BreachActive) return;
            float seconds = PhaseSeconds(breachPhase, breachQuiet);
            if (seconds <= 0f)
            {
                EndBreach();
                return;
            }
            float contribution = TraceContribution(breachPhase) * TraceScale(breachTarget, breachQuiet);
            breachTrace = Math.Min(1f, breachTrace + contribution / seconds * dt);
            if (breachTrace >= 1f)
            {
                Backtrace(now);
                return;
            }
            if (now < breachPhaseEnds) return;
            switch (breachPhase)
            {
                case BreachPhase.Probe:
                    Advance(BreachPhase.Exploit, now);
                    return;
                case BreachPhase.Exploit:
                    Advance(BreachPhase.Extract, now);
                    return;
                default:
                    Complete(now);
                    return;
            }
        }

        private void Advance(BreachPhase phase, double now)
        {
            int slot = breachTarget;
            float cost = PhaseCost(phase, Math.Max(1, (int)nodes[slot].Stage + 1), breachQuiet);
            if (Computing + 0.001f < cost)
            {
                // No computing to continue: the session ends without loot, not with a backtrace.
                EndBreach();
                Notify(CyberNotice.BreachStalled, slot, 0, now);
                return;
            }
            SpendComputing(cost);
            breachPhase = phase;
            breachPhaseEnds = now + PhaseSeconds(phase, breachQuiet);
            Notify(CyberNotice.BreachPhaseDone, slot, (byte)phase, now);
        }

        private void Complete(double now)
        {
            int slot = breachTarget;
            int stage = Math.Min(CyberLocations.StageCount, (int)nodes[slot].Stage + 1);
            nodes[slot].Stage = (byte)stage;
            nodes[slot].Hacked = true;
            float loot = 10f + 10f * stage;
            if (CyberLocations.LocationOf(nodes[slot].Kind) == LocationKind.City) loot *= 1.5f;
            GrantIntel(loot);
            EndBreach();
            Notify(CyberNotice.StageUp, slot, (byte)stage, now);
            if (stage >= CyberLocations.StageCount)
            {
                choiceTarget = slot;
                choiceDeadline = now + CyberLocations.PendingChoiceSeconds;
                Notify(CyberNotice.CapstoneReady, slot, 0, now);
            }
        }

        private void Backtrace(double now)
        {
            int slot = breachTarget;
            nodes[slot].LockoutUntil = now + CyberLocations.LockoutSeconds;
            Heat = Math.Min(HeatMaximum, Heat + OffensiveHeatSpike);
            EndBreach();
            Notify(CyberNotice.BreachBacktrace, slot, 0, now);
        }

        private static float TraceContribution(BreachPhase phase)
        {
            switch (phase)
            {
                case BreachPhase.Probe: return CyberLocations.ProbeTrace;
                case BreachPhase.Exploit: return CyberLocations.ExploitTrace;
                default: return CyberLocations.ExtractTrace;
            }
        }

        // ---- Verbs --------------------------------------------------------------------------

        public float RechargeRemaining(CyberVerb verb, double now) =>
            (byte)verb < VerbCount ? (float)Math.Max(0.0, verbReady[(byte)verb] - now) : 0f;

        public static float VerbCost(CyberVerb verb)
        {
            switch (verb)
            {
                case CyberVerb.Isolate: return 8f;
                case CyberVerb.Patch: return 18f;
                case CyberVerb.Honeypot: return 14f;
                case CyberVerb.Trace: return 22f;
                default: return 20f;
            }
        }

        public static float VerbRecharge(CyberVerb verb)
        {
            switch (verb)
            {
                case CyberVerb.Isolate: return 4f;
                case CyberVerb.Patch: return 12f;
                case CyberVerb.Honeypot: return 25f;
                case CyberVerb.Trace: return 20f;
                default: return 25f;
            }
        }

        public static bool TargetsIncident(CyberVerb verb) => verb == CyberVerb.Trace || verb == CyberVerb.BurnThrough;

        /// <summary>Everything the host re-checks for a verb. <paramref name="target"/> is a slot or,
        /// for TRACE and BURN THROUGH, an incident index.</summary>
        public CyberDenial Check(CyberVerb verb, int target, double now)
        {
            if ((byte)verb >= VerbCount) return CyberDenial.NoTarget;
            if (!HasCommand) return CyberDenial.NoCommand;
            if (TargetsIncident(verb))
            {
                if (!IncidentActive(target)) return CyberDenial.NoTarget;
                CyberIncident incident = incidents[target];
                if (verb == CyberVerb.Trace)
                {
                    if (!Traceable(incident.Kind)) return CyberDenial.NotTraceable;
                    if (incident.Tracing) return CyberDenial.AlreadyTracing;
                    if (!EarCovers(incident.X, incident.Z, now)) return CyberDenial.NeedsEar;
                }
                else
                {
                    if (incident.Kind != IncidentKind.Raid) return CyberDenial.NotTraceable;
                    if (!HackedCovers(incident.X, incident.Z, now)) return CyberDenial.NeedsCoverage;
                }
            }
            else
            {
                if (!Exists(target)) return CyberDenial.NoTarget;
                CyberNode node = nodes[target];
                if (!Online(target)) return CyberDenial.Offline;
                switch (verb)
                {
                    case CyberVerb.Isolate:
                        if (node.Kind == NodeKind.Command) return CyberDenial.CommandProtected;
                        break;
                    case CyberVerb.Patch:
                        if (!node.Compromised) return CyberDenial.NotCompromised;
                        if (node.PatchDone > 0.0) return CyberDenial.AlreadyPatching;
                        break;
                    case CyberVerb.Honeypot:
                        if (node.Kind == NodeKind.Command) return CyberDenial.CommandProtected;
                        if (node.HoneypotUntil > now) return CyberDenial.AlreadyBaited;
                        break;
                }
            }
            // Re-joining an isolated node is free and immediate.
            if (verb == CyberVerb.Isolate && Exists(target) && nodes[target].Isolated) return CyberDenial.None;
            if (RechargeRemaining(verb, now) > 0f) return CyberDenial.Recharging;
            if (Computing + 0.001f < VerbCost(verb)) return CyberDenial.LowComputing;
            return CyberDenial.None;
        }

        public CyberDenial TryVerb(CyberVerb verb, int target, double now)
        {
            CyberDenial denial = Check(verb, target, now);
            if (denial != CyberDenial.None) return denial;
            bool free = verb == CyberVerb.Isolate && nodes[target].Isolated;
            switch (verb)
            {
                case CyberVerb.Isolate:
                    nodes[target].Isolated = !nodes[target].Isolated;
                    Notify(nodes[target].Isolated ? CyberNotice.Isolated : CyberNotice.Rejoined, target, 0, now);
                    break;
                case CyberVerb.Patch:
                    nodes[target].PatchDone = now + CyberLocations.PatchSeconds;
                    break;
                case CyberVerb.Honeypot:
                    nodes[target].HoneypotUntil = now + CyberLocations.HoneypotSeconds;
                    Notify(CyberNotice.Baited, target, 0, now);
                    break;
                case CyberVerb.Trace:
                    incidents[target].Tracing = true;
                    Notify(CyberNotice.TraceStarted, incidents[target].Site, incidents[target].Origin, now);
                    break;
                case CyberVerb.BurnThrough:
                    Resolve(target, IncidentOutcome.Broken, now);
                    Notify(CyberNotice.RaidBroken, -1, incidents[target].Origin, now);
                    break;
            }
            if (!free)
            {
                SpendComputing(VerbCost(verb));
                verbReady[(byte)verb] = now + VerbRecharge(verb);
            }
            return CyberDenial.None;
        }

        // ---- Tick ---------------------------------------------------------------------------

        private double lastTick;

        /// <summary>Host: resources, patches, bait, lockouts, the breach, links and the campaign.</summary>
        public void Tick(double now, float deltaTime, float campaignIntensity)
        {
            lastTick = now;
            float dt = Math.Max(0f, Math.Min(deltaTime, 5f));
            for (int i = 0; i < SlotCount; i++)
            {
                if (nodes[i].Kind == NodeKind.None) continue;
                if (nodes[i].PatchDone > 0.0 && now >= nodes[i].PatchDone)
                {
                    nodes[i].PatchDone = 0.0;
                    nodes[i].Compromised = false;
                    Notify(CyberNotice.Patched, i, 0, now);
                }
                if (nodes[i].HoneypotUntil > 0.0 && now >= nodes[i].HoneypotUntil) nodes[i].HoneypotUntil = 0.0;
            }
            SelfRepair(now);

            Computing = Math.Min(ComputingCapacity(), Computing + ComputingIncome() * dt);
            Intel = Math.Min(IntelCapacity(), Intel + IntelIncome() * dt);

            if (BreachAwaitingChoice && now >= choiceDeadline)
            {
                // The operator never chose; the location keeps REVEAL as the watch floor's pick.
                TryChooseCapstone(Capstone.Reveal, now);
            }
            StepBreach(now, dt);
            TickCampaign(now, dt, campaignIntensity);
        }

        private void SelfRepair(double now)
        {
            if (CommandCompromised) return;
            for (int i = 0; i < SlotCount; i++)
            {
                CyberNode node = nodes[i];
                if (!node.Compromised || node.Kind == NodeKind.Command || node.PatchDone > 0.0) continue;
                if (now - node.CompromisedAt < CyberLocations.SelfRepairSeconds || IntrusionAt(i)) continue;
                nodes[i].Compromised = false;
                Notify(CyberNotice.SelfRepaired, i, 0, now);
            }
        }

        private bool IntrusionAt(int slot)
        {
            for (int i = 0; i < IncidentSlots; i++)
                if (IncidentActive(i) && incidents[i].Kind == IncidentKind.Intrusion && incidents[i].Site == slot) return true;
            return false;
        }

        // ---- Airbase infrastructure ------------------------------------------------------------

        private readonly bool[] homeSeen = new bool[SlotCount];
        private readonly bool[] targetSeen = new bool[SlotCount];

        /// <summary>Host, once a second: start a reconcile of the home nodes.</summary>
        public void BeginInfrastructure() => Array.Clear(homeSeen, 0, SlotCount);

        /// <summary>
        /// Host: this airbase is a home node this pass. <paramref name="anchor"/> identifies the
        /// base; <paramref name="down"/> says its anchor building is destroyed. Returns the slot,
        /// or -1 when there is no room.
        /// </summary>
        public int ReportInfrastructure(int anchor, NodeKind kind, float x, float z, bool down, double now)
        {
            if (!CyberLocations.IsHome(kind) || !Finite(x) || !Finite(z)) return -1;
            int slot = -1;
            for (int i = 0; i < CyberLocations.HomeSlots; i++)
            {
                if (!nodes[i].Static || nodes[i].Anchor != anchor || nodes[i].Kind == NodeKind.None) continue;
                slot = i;
                break;
            }
            if (slot < 0)
            {
                slot = FreeHomeSlot();
                if (slot < 0) return -1;
                nodes[slot] = new CyberNode { Kind = kind, X = x, Z = z, Static = true, Anchor = anchor, Down = down };
                homeSeen[slot] = true;
                // The home network is raised by the host, not bought by the player: announce the
                // command, and further bases only once the network is established, so a fresh
                // match does not open with six identical "joined" lines.
                if (kind == NodeKind.Command) Notify(CyberNotice.CommandUp, slot, 0, now);
                else if (HackedCount > 0) Notify(CyberNotice.BaseJoined, slot, 0, now);
                return slot;
            }

            homeSeen[slot] = true;
            CyberNode node = nodes[slot];
            if (node.Kind != kind)
            {
                node.Kind = kind;
                node.Isolated = node.Isolated && kind != NodeKind.Command;
                if (kind == NodeKind.Command) Notify(CyberNotice.CommandMoved, slot, 0, now);
            }
            if (node.Down != down) Notify(down ? CyberNotice.NodeDown : CyberNotice.NodeRestored, slot, 0, now);
            node.Down = down;
            node.X = x;
            node.Z = z;
            nodes[slot] = node;
            return slot;
        }

        /// <summary>Host: every home node not reported since <see cref="BeginInfrastructure"/>
        /// went with its base.</summary>
        public void EndInfrastructure(double now)
        {
            for (int i = 0; i < CyberLocations.HomeSlots; i++)
            {
                if (!nodes[i].Static || homeSeen[i] || nodes[i].Kind == NodeKind.None) continue;
                bool command = nodes[i].Kind == NodeKind.Command;
                Notify(command ? CyberNotice.CommandLost : CyberNotice.BaseLost, i, 0, now);
                nodes[i] = default;
                DropNodeReferences(i);
            }
        }

        /// <summary>Test and single-base seam: one home node, reported alongside the ones already there.</summary>
        public int PlaceStatic(int anchor, NodeKind kind, float x, float z, double now = 0.0)
        {
            for (int i = 0; i < CyberLocations.HomeSlots; i++) homeSeen[i] = nodes[i].Static;
            return ReportInfrastructure(anchor, kind, x, z, false, now);
        }

        /// <summary>Host, once a second: start a reconcile of the scene's hackable locations.</summary>
        public void BeginLocations() => Array.Clear(targetSeen, 0, SlotCount);

        /// <summary>Host: a hackable location this pass. Returns its slot, or -1 when full.</summary>
        public int ReportLocation(int anchor, LocationKind kind, float x, float z, double now)
        {
            if (!CyberLocations.Hackable(kind) || !Finite(x) || !Finite(z)) return -1;
            int slot = -1;
            for (int i = TargetBase; i < SlotCount; i++)
            {
                if (nodes[i].Kind == NodeKind.None || nodes[i].Anchor != anchor) continue;
                slot = i;
                break;
            }
            if (slot < 0)
            {
                slot = FreeTargetSlot();
                if (slot < 0) return -1;
                nodes[slot] = new CyberNode
                {
                    Kind = kind == LocationKind.City ? NodeKind.City : NodeKind.Airfield,
                    X = x,
                    Z = z,
                    Anchor = anchor
                };
            }
            targetSeen[slot] = true;
            nodes[slot].X = x;
            nodes[slot].Z = z;
            return slot;
        }

        /// <summary>Host: a location that was not reported this pass is gone. A hacked location
        /// whose airfield was retaken is lost with it.</summary>
        public void EndLocations(double now)
        {
            for (int i = TargetBase; i < SlotCount; i++)
            {
                if (nodes[i].Kind == NodeKind.None || targetSeen[i]) continue;
                if (nodes[i].Hacked) Notify(CyberNotice.LocationLost, i, 0, now);
                if (breachTarget == i) EndBreach();
                if (choiceTarget == i)
                {
                    choiceTarget = -1;
                    choiceDeadline = 0.0;
                }
                nodes[i] = default;
                DropNodeReferences(i);
            }
        }

        private int FreeHomeSlot()
        {
            for (int i = 0; i < CyberLocations.HomeSlots; i++)
                if (nodes[i].Kind == NodeKind.None) return i;
            return -1;
        }

        private int FreeTargetSlot()
        {
            for (int i = TargetBase; i < SlotCount; i++)
                if (nodes[i].Kind == NodeKind.None) return i;
            return -1;
        }

        public void Clear()
        {
            Array.Clear(nodes, 0, SlotCount);
            Array.Clear(verbReady, 0, VerbCount);
            Array.Clear(capstoneReady, 0, 3);
            Array.Clear(upgradeLevels, 0, 4);
            Computing = 0f;
            Intel = 0f;
            OriginCount = 0;
            lastTick = 0.0;
            EndBreach();
            choiceTarget = -1;
            choiceDeadline = 0.0;
            ClearCampaign();
        }

        // ---- Snapshot -----------------------------------------------------------------------

        /// <summary>Host: the network with clocks relative to <paramref name="now"/>.</summary>
        public void Export(double now, CyberSnapshot into)
        {
            into.Clear();
            int count = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                CyberNode node = nodes[i];
                if (node.Kind == NodeKind.None) continue;
                if (!node.Static && !node.Hacked && !CyberLocations.Hackable(CyberLocations.LocationOf(node.Kind))) continue;
                into.Slot[count] = (byte)i;
                into.Kind[count] = (byte)node.Kind;
                into.Stage[count] = node.Stage;
                into.X[count] = node.X;
                into.Z[count] = node.Z;
                into.Flags[count] = (byte)((node.Static ? 1 : 0) | (node.Hacked ? 2 : 0) | (node.Down ? 4 : 0) |
                                           (node.Isolated ? 8 : 0) | (node.Compromised ? 16 : 0));
                into.Capstone[count] = (byte)node.Capstone;
                into.PatchIn[count] = SecondsByte(node.PatchDone > 0.0 ? node.PatchDone - now : 0.0);
                into.BaitIn[count] = SecondsByte(node.HoneypotUntil > now ? node.HoneypotUntil - now : 0.0);
                into.LockIn[count] = SecondsByte(node.LockoutUntil > now ? node.LockoutUntil - now : 0.0);
                count++;
            }
            into.NodeCount = (byte)count;
            into.Computing = Computing;
            into.Intel = Intel;
            for (int u = 0; u < 4; u++) into.Upgrade[u] = (byte)upgradeLevels[u];
            // The choice flag discriminates the same bounded target/timer fields; wire shape is unchanged.
            int sessionTarget = BreachAwaitingChoice ? choiceTarget : breachTarget;
            into.BreachTarget = sessionTarget < 0 ? (byte)255 : (byte)sessionTarget;
            into.BreachPhase = (byte)breachPhase;
            into.BreachFlags = (byte)((breachQuiet ? 1 : 0) | (choiceTarget >= 0 ? 2 : 0));
            into.BreachTrace = breachTrace;
            into.BreachIn = BreachAwaitingChoice ? ChoiceRemaining(now) : BreachPhaseRemaining(now);
            into.SpoofIn = SpoofRechargeRemaining(now);
            for (int v = 0; v < VerbCount; v++) into.Recharge[v] = RechargeRemaining((CyberVerb)v, now);
            ExportCampaign(now, into);
        }

        /// <summary>Client: rebuild from a host snapshot. Out-of-range values are dropped or clamped.</summary>
        public void Mirror(CyberSnapshot from, double now)
        {
            if (from == null) return;
            bool[] seen = mirrorSeen;
            Array.Clear(seen, 0, SlotCount);
            int count = Math.Min((int)from.NodeCount, SlotCount);
            for (int i = 0; i < count; i++)
            {
                int slot = from.Slot[i];
                if (slot >= SlotCount || !CyberLocations.Known(from.Kind[i]) || !Finite(from.X[i]) || !Finite(from.Z[i]))
                    continue;
                seen[slot] = true;
                byte flags = from.Flags[i];
                CyberNode node = nodes[slot];
                node.Kind = (NodeKind)from.Kind[i];
                node.Stage = (byte)Math.Max(0, Math.Min(CyberLocations.StageCount, (int)from.Stage[i]));
                node.X = from.X[i];
                node.Z = from.Z[i];
                node.Static = (flags & 1) != 0 && CyberLocations.IsHome(node.Kind);
                node.Hacked = (flags & 2) != 0 && CyberLocations.IsHacked(node.Kind);
                node.Down = (flags & 4) != 0 && node.Static;
                node.Isolated = (flags & 8) != 0;
                node.Compromised = (flags & 16) != 0;
                node.Capstone = Capstones.Known(from.Capstone[i]) ? (Capstone)from.Capstone[i] : Capstone.None;
                node.PatchDone = Rebase(node.PatchDone, from.PatchIn[i], now, CyberLocations.PatchSeconds);
                node.HoneypotUntil = Rebase(node.HoneypotUntil, from.BaitIn[i], now, CyberLocations.HoneypotSeconds);
                node.LockoutUntil = Rebase(node.LockoutUntil, from.LockIn[i], now, CyberLocations.LockoutSeconds);
                nodes[slot] = node;
            }
            for (int i = 0; i < SlotCount; i++)
                if (!seen[i]) nodes[i] = default;

            Computing = Finite(from.Computing) ? Math.Max(0f, Math.Min(from.Computing, 100000f)) : 0f;
            Intel = Finite(from.Intel) ? Math.Max(0f, Math.Min(from.Intel, 100000f)) : 0f;
            for (int u = 0; u < 4; u++) upgradeLevels[u] = Math.Max(0, Math.Min(CyberLocations.UpgradeLevels, (int)from.Upgrade[u]));

            int sessionTarget = from.BreachTarget < SlotCount && Exists(from.BreachTarget) ? from.BreachTarget : -1;
            bool choosing = (from.BreachFlags & 2) != 0 && sessionTarget >= TargetBase && IsHacked(sessionTarget) &&
                            nodes[sessionTarget].Stage == CyberLocations.StageCount && nodes[sessionTarget].Capstone == Capstone.None &&
                            from.BreachPhase == (byte)BreachPhase.None;
            breachTarget = choosing ? -1 : sessionTarget;
            breachPhase = from.BreachPhase <= (byte)BreachPhase.Extract ? (BreachPhase)from.BreachPhase : BreachPhase.None;
            if (breachTarget < 0 || breachPhase == BreachPhase.None) breachTarget = -1;
            breachQuiet = (from.BreachFlags & 1) != 0;
            breachTrace = Math.Max(0f, Math.Min(1f, from.BreachTrace));
            breachPhaseEnds = Rebase(breachPhaseEnds, from.BreachIn, now, 120f);
            choiceTarget = choosing ? sessionTarget : -1;
            choiceDeadline = choosing ? Rebase(choiceDeadline, from.BreachIn, now, CyberLocations.PendingChoiceSeconds) : 0.0;
            capstoneReady[0] = Rebase(capstoneReady[0], from.SpoofIn, now, CyberLocations.SpoofRecharge);
            for (int v = 0; v < VerbCount; v++)
                verbReady[v] = Rebase(verbReady[v], from.Recharge[v], now, 120f);
            MirrorCampaign(from, now);
        }

        /// <summary>Keeps a local absolute clock unless the host's relative reading moved it by
        /// more than half a second; zero or garbage clears it.</summary>
        internal static double Rebase(double current, float remaining, double now, float maximum)
        {
            if (!Finite(remaining) || remaining <= 0f) return 0.0;
            double target = now + Math.Min(remaining, maximum);
            return Math.Abs(current - target) > 0.5 ? target : current;
        }

        /// <summary>Seconds as one wire byte: a snapshot timer is a hint, not a stopwatch.</summary>
        internal static byte SecondsByte(double seconds) =>
            seconds <= 0.0 ? (byte)0 : (byte)Math.Min(255.0, Math.Ceiling(seconds));

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Wire-shaped copy of a network: nodes, resources, upgrades, the breach and the campaign.</summary>
    internal sealed class CyberSnapshot
    {
        public byte NodeCount;
        public readonly byte[] Slot = new byte[CyberNetwork.SlotCount];
        public readonly byte[] Kind = new byte[CyberNetwork.SlotCount];
        public readonly byte[] Stage = new byte[CyberNetwork.SlotCount];
        public readonly float[] X = new float[CyberNetwork.SlotCount];
        public readonly float[] Z = new float[CyberNetwork.SlotCount];
        public readonly byte[] Flags = new byte[CyberNetwork.SlotCount];
        public readonly byte[] Capstone = new byte[CyberNetwork.SlotCount];
        public readonly byte[] PatchIn = new byte[CyberNetwork.SlotCount];
        public readonly byte[] BaitIn = new byte[CyberNetwork.SlotCount];
        public readonly byte[] LockIn = new byte[CyberNetwork.SlotCount];
        public float Computing;
        public float Intel;
        public readonly byte[] Upgrade = new byte[4];

        public byte BreachTarget;
        public byte BreachPhase;
        public byte BreachFlags;
        public float BreachTrace;
        public float BreachIn;
        public float SpoofIn;
        public readonly float[] Recharge = new float[CyberNetwork.VerbCount];

        public byte Heat;
        public int Defended;
        public int Breached;
        public float NextIncidentIn;
        public float ExposedIn;
        public byte IncidentCount;
        public readonly byte[] IncidentKind = new byte[CyberNetwork.IncidentSlots];
        public readonly byte[] IncidentState = new byte[CyberNetwork.IncidentSlots];
        public readonly byte[] IncidentSite = new byte[CyberNetwork.IncidentSlots];
        public readonly byte[] IncidentOrigin = new byte[CyberNetwork.IncidentSlots];
        public readonly float[] IncidentX = new float[CyberNetwork.IncidentSlots];
        public readonly float[] IncidentZ = new float[CyberNetwork.IncidentSlots];
        public readonly float[] IncidentAge = new float[CyberNetwork.IncidentSlots];
        public readonly float[] IncidentLeft = new float[CyberNetwork.IncidentSlots];
        public readonly byte[] IncidentTrace = new byte[CyberNetwork.IncidentSlots];
        public readonly byte[] Foothold = new byte[CyberNetwork.MaximumOrigins];

        public int NoticeSerial;
        public byte NoticeCount;
        public readonly byte[] NoticeKind = new byte[CyberNetwork.NoticeSlots];
        public readonly byte[] NoticeSite = new byte[CyberNetwork.NoticeSlots];
        public readonly byte[] NoticeOrigin = new byte[CyberNetwork.NoticeSlots];

        public void Clear()
        {
            NodeCount = 0;
            Array.Clear(Slot, 0, Slot.Length);
            Array.Clear(Kind, 0, Kind.Length);
            Array.Clear(Stage, 0, Stage.Length);
            Array.Clear(X, 0, X.Length);
            Array.Clear(Z, 0, Z.Length);
            Array.Clear(Flags, 0, Flags.Length);
            Array.Clear(Capstone, 0, Capstone.Length);
            Array.Clear(PatchIn, 0, PatchIn.Length);
            Array.Clear(BaitIn, 0, BaitIn.Length);
            Array.Clear(LockIn, 0, LockIn.Length);
            Computing = 0f;
            Intel = 0f;
            Array.Clear(Upgrade, 0, Upgrade.Length);
            BreachTarget = 255;
            BreachPhase = 0;
            BreachFlags = 0;
            BreachTrace = 0f;
            BreachIn = 0f;
            SpoofIn = 0f;
            Array.Clear(Recharge, 0, Recharge.Length);
            Heat = 0;
            Defended = 0;
            Breached = 0;
            NextIncidentIn = 0f;
            ExposedIn = 0f;
            IncidentCount = 0;
            Array.Clear(IncidentKind, 0, IncidentKind.Length);
            Array.Clear(IncidentState, 0, IncidentState.Length);
            Array.Clear(IncidentSite, 0, IncidentSite.Length);
            Array.Clear(IncidentOrigin, 0, IncidentOrigin.Length);
            Array.Clear(IncidentX, 0, IncidentX.Length);
            Array.Clear(IncidentZ, 0, IncidentZ.Length);
            Array.Clear(IncidentAge, 0, IncidentAge.Length);
            Array.Clear(IncidentLeft, 0, IncidentLeft.Length);
            Array.Clear(IncidentTrace, 0, IncidentTrace.Length);
            Array.Clear(Foothold, 0, Foothold.Length);
            NoticeSerial = 0;
            NoticeCount = 0;
            Array.Clear(NoticeKind, 0, NoticeKind.Length);
            Array.Clear(NoticeSite, 0, NoticeSite.Length);
            Array.Clear(NoticeOrigin, 0, NoticeOrigin.Length);
        }
    }
}
