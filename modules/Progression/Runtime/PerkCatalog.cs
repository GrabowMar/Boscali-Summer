using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Progression.Runtime
{
    /// <summary>
    /// One grade of a qualification: a lane, the grade it occupies, and either a multiplier
    /// or a support capability, never both. Grade 1 of every lane is that lane's tool — the
    /// support authorisation — and grades 2..6 hang off the grade before them.
    /// </summary>
    internal readonly struct PerkDefinition
    {
        /// <summary>Every node costs exactly one pick: grades pace the board, not prices.</summary>
        public const byte PickCost = 1;

        public readonly byte Id;
        public readonly string Lane;
        public readonly byte Grade;
        public readonly string Name;
        public readonly string Description;
        public readonly byte Cost;
        public readonly PerkEffect Effect;
        public readonly float Multiplier;
        public readonly string Capability;

        /// <summary>The node's glyph, an <c>AvIcon</c> member name (kept a string so the catalogue stays engine-free).</summary>
        public readonly string Icon;

        public PerkDefinition(
            byte id, string lane, byte grade, string name, string description,
            PerkEffect effect, float multiplier, string icon)
        {
            Id = id;
            Lane = lane;
            Grade = grade;
            Name = name;
            Description = description;
            Cost = PickCost;
            Effect = effect;
            Multiplier = multiplier;
            Capability = null;
            Icon = icon;
        }

        public PerkDefinition(
            byte id, string lane, byte grade, string name, string description,
            string capability, string icon)
        {
            Id = id;
            Lane = lane;
            Grade = grade;
            Name = name;
            Description = description;
            Cost = PickCost;
            Effect = PerkEffect.FuelUse;
            Multiplier = 1f;
            Capability = capability;
            Icon = icon;
        }

        /// <summary>A lane's tool: the node that grants a support capability.</summary>
        public bool IsTool => Capability != null;
    }

    /// <summary>
    /// The career board: four qualifications of six grades. Grade 1 is the lane's OPS
    /// authorisation, grades 2..6 are its passives, and the grade-6 node is the capstone a
    /// pilot can only reach by staying in one lane. A career may hold at most
    /// <see cref="AuthorisationLimit"/> tools, so two lanes open and two stay closed.
    /// </summary>
    internal static class PerkCatalog
    {
        /// <summary>The one heading the SQD board draws every lane under.</summary>
        public const string Qualifications = "QUALIFICATIONS";

        // Lane names are both the SQD lane captions and the OPS lock copy.
        public const string Strike = "STRIKE";
        public const string Recon = "RECON";
        public const string Signals = "SIGNALS";
        public const string Engineer = "ENGINEER";

        /// <summary>Grades per lane; also the deepest the board ever gets.</summary>
        public const int MaximumDepth = 6;

        /// <summary>Tolls a career may hold. One per lane, so this is also the lane cap.</summary>
        public const int AuthorisationLimit = 2;

        /// <summary>
        /// Ordered so that <c>All[i].Id == i</c>, each lane occupies a contiguous id block
        /// starting at its grade 1, and grades run 1..<see cref="MaximumDepth"/> without
        /// gaps. A prerequisite is derived from that order (<see cref="PrerequisiteOf"/>),
        /// so it can never point forward or across lanes. <c>ProgressionTests</c> asserts the
        /// shape, the mask width, the authorisation cap, and that this table and
        /// <c>SupportCatalog</c> agree on every capability.
        /// </summary>
        public static readonly PerkDefinition[] All =
        {
            // Each lane pays for its own kind of flying: STRIKE kills and fast fire missions, RECON
            // spotting and range, SIGNALS jamming and cheap support, ENGINEER service runs and captures.
            // Grade 6 is the lane's big capstone.
            new PerkDefinition(0, Strike, 1, "Strike Qualification",
                "Authorises Rod-from-God kinetic strikes from a STRIKE satellite.",
                SupportCapabilities.Artillery, "Bomb"),
            new PerkDefinition(1, Strike, 2, "Target Priority",
                "Kills pay 15% more allocation.", PerkEffect.CombatReward, 1.15f, "Crosshair"),
            new PerkDefinition(2, Strike, 3, "Hot Turnaround",
                "Support re-tasks 15% faster after every call.", PerkEffect.SupportCooldown, 0.85f, "Clock"),
            new PerkDefinition(3, Strike, 4, "Strike Package",
                "Support calls cost 15% less allocation.", PerkEffect.SupportCost, 0.85f, "Coins"),
            new PerkDefinition(4, Strike, 5, "Bounty Hunter",
                "Kills pay a further 25% more.", PerkEffect.CombatReward, 1.25f, "Skull"),
            new PerkDefinition(5, Strike, 6, "Time on Target",
                "Capstone: support re-tasks 30% faster.", PerkEffect.SupportCooldown, 0.70f, "Burst"),

            new PerkDefinition(6, Recon, 1, "Recon Qualification",
                "Authorises reconnaissance sweeps and JTAC target marking.", SupportCapabilities.Recon, "Satellite"),
            new PerkDefinition(7, Recon, 2, "Lean Cruise",
                "Burn 8% less fuel.", PerkEffect.FuelUse, 0.92f, "Gauge"),
            new PerkDefinition(8, Recon, 3, "Spotter's Fee",
                "Spotting enemy units pays 30% more.", PerkEffect.ReconReward, 1.30f, "Eye"),
            new PerkDefinition(9, Recon, 4, "Extended Patrol",
                "Burn a further 12% less fuel.", PerkEffect.FuelUse, 0.88f, "FlightPath"),
            new PerkDefinition(10, Recon, 5, "Pathfinder",
                "Captures and pilot rescues pay 20% more.", PerkEffect.ObjectiveReward, 1.20f, "MapPin"),
            new PerkDefinition(11, Recon, 6, "Long Watch",
                "Capstone: spotting pays a further 50% more.", PerkEffect.ReconReward, 1.50f, "Camera"),

            new PerkDefinition(12, Signals, 1, "Signals Qualification",
                "Authorises EMP and radar-disruption strikes from an EW satellite.",
                SupportCapabilities.Emp, "Broadcast"),
            new PerkDefinition(13, Signals, 2, "Jammer's Cut",
                "Jamming enemy radar pays 30% more.", PerkEffect.JammingReward, 1.30f, "WaveSine"),
            new PerkDefinition(14, Signals, 3, "Signal Discipline",
                "Support calls cost 15% less allocation.", PerkEffect.SupportCost, 0.85f, "Radio"),
            new PerkDefinition(15, Signals, 4, "Escort Duty",
                "Kills pay 15% more allocation.", PerkEffect.CombatReward, 1.15f, "Formation"),
            new PerkDefinition(16, Signals, 5, "Blackout Tempo",
                "Jamming pays a further 40% more.", PerkEffect.JammingReward, 1.40f, "EyeOff"),
            new PerkDefinition(17, Signals, 6, "Full Spectrum",
                "Capstone: support calls cost 30% less.", PerkEffect.SupportCost, 0.70f, "RadarDish"),

            new PerkDefinition(18, Engineer, 1, "Engineer Qualification",
                "Authorises controlled-zone fortification.",
                SupportCapabilities.Fortify, "Shield"),
            new PerkDefinition(19, Engineer, 2, "Ground Crew",
                "Supply, refuel and repair runs pay 20% more.", PerkEffect.ServiceReward, 1.20f, "Truck"),
            new PerkDefinition(20, Engineer, 3, "Zone Control",
                "Captures and pilot rescues pay 20% more.", PerkEffect.ObjectiveReward, 1.20f, "Territory"),
            new PerkDefinition(21, Engineer, 4, "Field Refit",
                "Service runs pay a further 25% more.", PerkEffect.ServiceReward, 1.25f, "Settings"),
            new PerkDefinition(22, Engineer, 5, "Bastion Doctrine",
                "Support calls cost 15% less allocation.", PerkEffect.SupportCost, 0.85f, "Building"),
            new PerkDefinition(23, Engineer, 6, "Depot Network",
                "Capstone: service runs pay a further 50% more.", PerkEffect.ServiceReward, 1.50f, "BuildingFactory")
        };

        /// <summary>
        /// The node a grade hangs off: the previous grade in the same lane, or
        /// <see cref="PerkView.NoPrerequisite"/> for a lane's tool.
        /// </summary>
        public static byte PrerequisiteOf(byte id) =>
            IsDefined(id) && All[id].Grade > 1 ? (byte)(id - 1) : PerkView.NoPrerequisite;

        public static string CapabilityCode(string capability)
        {
            if (capability == SupportCapabilities.Recon) return "SAT";
            if (capability == SupportCapabilities.Fortify) return "ENG";
            if (capability == SupportCapabilities.Artillery) return "STK";
            if (capability == SupportCapabilities.Emp) return "EW";
            return "AUT";
        }

        /// <summary>
        /// Short label for what a passive grade buys, sized for one board cell: "+15% KILLS",
        /// "-10% COOLDOWN". A tool grants an authorisation instead of a multiplier, so it has no
        /// label and the panel names the tool's state instead. Pure, so the board's copy is
        /// testable without the game.
        /// </summary>
        public static string EffectLabel(byte id)
        {
            if (!IsDefined(id)) return string.Empty;
            PerkDefinition definition = All[id];
            if (definition.IsTool) return string.Empty;

            int percent = (int)Math.Round(Math.Abs(definition.Multiplier - 1f) * 100f);
            switch (definition.Effect)
            {
                case PerkEffect.CombatReward: return "+" + percent + "% KILLS";
                case PerkEffect.ReconReward: return "+" + percent + "% SPOTTING";
                case PerkEffect.JammingReward: return "+" + percent + "% JAMMING";
                case PerkEffect.ServiceReward: return "+" + percent + "% SERVICE";
                case PerkEffect.ObjectiveReward: return "+" + percent + "% CAPTURE";
                case PerkEffect.FuelUse: return "-" + percent + "% FUEL";
                case PerkEffect.SupportCost: return "-" + percent + "% PRICE";
                case PerkEffect.SupportCooldown: return "-" + percent + "% COOLDOWN";
                default: return string.Empty;
            }
        }

        // Multiplier runs on every fuel draw; the passives per effect never change after startup.
        private static readonly PerkDefinition[][] PassivesByEffect = BuildPassives();

        private static PerkDefinition[][] BuildPassives()
        {
            var table = new PerkDefinition[Enum.GetValues(typeof(PerkEffect)).Length][];
            for (int effect = 0; effect < table.Length; effect++)
            {
                var list = new System.Collections.Generic.List<PerkDefinition>();
                for (int i = 0; i < All.Length; i++)
                    if (All[i].Capability == null && (int)All[i].Effect == effect) list.Add(All[i]);
                table[effect] = list.ToArray();
            }
            return table;
        }

        /// <summary>The capability-free perks that carry <paramref name="effect"/>, in catalogue order.</summary>
        public static PerkDefinition[] Passives(PerkEffect effect) => PassivesByEffect[(int)effect];

        public static bool IsDefined(byte id) => id < All.Length;

        public static PerkDefinition Get(byte id)
        {
            if (!IsDefined(id)) throw new ArgumentOutOfRangeException(nameof(id));
            return All[id];
        }
    }

    /// <summary>Score-derived pick budget. Pure so it is testable without the game.</summary>
    internal static class PerkPoints
    {
        /// <summary>
        /// One pick per qualification grade. Grade <c>n</c> costs <c>n × scorePerPoint</c>,
        /// so grades get longer as they get better: grade 1 lands early, the grade-6
        /// capstone needs the long sortie. Aces pay bonus picks on top of the ladder.
        /// </summary>
        public static int Earned(int score, int scorePerPoint, int maximumPoints)
        {
            int cap = Math.Min(Math.Max(0, maximumPoints), PerkCatalog.MaximumDepth);
            if (scorePerPoint <= 0 || cap <= 0) return 0;

            long remaining = Math.Max(0, score);
            long next = scorePerPoint;
            int picks = 0;
            while (picks < cap && remaining >= next)
            {
                remaining -= next;
                picks++;
                next += scorePerPoint;
            }
            return picks;
        }

        public static int EarnedForPilot(int score, int origin, int scorePerPoint, int maximumPoints, int bonus)
        {
            // Normalize before subtracting or adding so extreme inputs cannot wrap into rewards.
            int pilotScore = Math.Max(0, Math.Max(0, score) - Math.Max(0, origin));
            int scorePoints = Earned(pilotScore, scorePerPoint, Math.Min(20, maximumPoints));
            return Math.Min(20, scorePoints + Math.Max(0, Math.Min(20, bonus)));
        }

        /// <summary>
        /// Score still missing for the next grade, or -1 once the ladder is exhausted (or
        /// the dial is invalid). Pure, so the SQD hint never invents a number.
        /// </summary>
        public static int RemainingToNext(int score, int scorePerPoint)
        {
            if (scorePerPoint <= 0) return -1;

            long remaining = Math.Max(0, score);
            long next = scorePerPoint;
            for (int grade = 0; grade < PerkCatalog.MaximumDepth; grade++)
            {
                if (remaining < next) return (int)Math.Min(int.MaxValue, next - remaining);
                remaining -= next;
                next += scorePerPoint;
            }
            return -1;
        }
    }

    internal sealed class PerkState
    {
        public uint Mask { get; private set; }

        public PerkState(uint mask = 0u) => Mask = mask;

        public int SpentPoints
        {
            get
            {
                int spent = 0;
                for (int i = 0; i < PerkCatalog.All.Length; i++)
                    if (Has(PerkCatalog.All[i].Id)) spent += PerkCatalog.All[i].Cost;
                return spent;
            }
        }

        /// <summary>Tools committed, whether or not they are still legal under today's cap.</summary>
        public int Authorisations
        {
            get
            {
                int held = 0;
                for (int i = 0; i < PerkCatalog.All.Length; i++)
                    if (PerkCatalog.All[i].IsTool && Has(PerkCatalog.All[i].Id)) held++;
                return held;
            }
        }

        public int AvailablePoints(int earnedPoints) => Math.Max(0, earnedPoints - SpentPoints);

        public bool Has(byte id) => (Mask & (1u << id)) != 0u;

        /// <summary>A lane's tool, or a grade whose predecessor is already committed.</summary>
        public bool PrerequisiteMet(byte id) =>
            PerkCatalog.IsDefined(id) &&
            (PerkCatalog.PrerequisiteOf(id) == PerkView.NoPrerequisite ||
             Has(PerkCatalog.PrerequisiteOf(id)));

        /// <summary>
        /// Why the host would refuse this grade, one of the <see cref="PerkView"/> block
        /// constants. An undefined or already-owned grade reports no block: there is nothing
        /// to refuse. <see cref="CanUnlock"/> and the SQD view both read this, so the panel
        /// can never advertise a pick the host would turn down.
        /// </summary>
        public byte BlockOf(byte id, int earnedPoints)
        {
            if (!PerkCatalog.IsDefined(id) || Has(id)) return PerkView.BlockNone;
            PerkDefinition definition = PerkCatalog.Get(id);
            if (!PrerequisiteMet(id)) return PerkView.BlockGrade;
            if (definition.IsTool && Authorisations >= PerkCatalog.AuthorisationLimit)
                return PerkView.BlockCap;
            return AvailablePoints(earnedPoints) >= definition.Cost
                ? PerkView.BlockNone
                : PerkView.BlockPoints;
        }

        public bool CanUnlock(byte id, int earnedPoints) =>
            PerkCatalog.IsDefined(id) && !Has(id) &&
            BlockOf(id, earnedPoints) == PerkView.BlockNone;

        public bool TryUnlock(byte id, int earnedPoints)
        {
            if (!CanUnlock(id, earnedPoints)) return false;
            Mask |= 1u << id;
            return true;
        }

        /// <summary>Debug bypass: grants a grade without picks, order or the career cap.</summary>
        public bool ForceUnlock(byte id)
        {
            if (!PerkCatalog.IsDefined(id) || Has(id)) return false;
            Mask |= 1u << id;
            return true;
        }
    }
}
