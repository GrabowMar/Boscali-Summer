using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Progression.Runtime
{
    /// <summary>
    /// One OPS skill: a lane (SPACE, CYBER or SOF), the grade it occupies, and what it unlocks.
    /// Grade 1 of every lane is free; grades 2..6 hang off the grade before them.
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

        /// <summary>The node's glyph, an <c>AvIcon</c> member name (kept a string so the catalogue stays engine-free).</summary>
        public readonly string Icon;

        public PerkDefinition(byte id, string lane, byte grade, string name, string description, string icon)
        {
            Id = id;
            Lane = lane;
            Grade = grade;
            Name = name;
            Description = description;
            Cost = grade == 1 ? (byte)0 : PickCost;
            Icon = icon;
        }
    }

    /// <summary>
    /// The career board: the three OPS systems, six grades each. Grade 1 is owned by every pilot,
    /// grade 6 is the capstone a pilot only reaches by spending a whole life's picks in one lane.
    /// </summary>
    internal static class PerkCatalog
    {
        /// <summary>The one heading the SQD board draws every lane under.</summary>
        public const string Qualifications = "OPS SKILLS";

        // Lane names are the SQD lane captions, the OPS system words and the lock copy.
        public const string Space = "SPACE";
        public const string Cyber = "CYBER";
        public const string Sof = "SOF";

        public static readonly string[] Lanes = { Space, Cyber, Sof };

        /// <summary>Grades per lane; also the most picks one pilot life earns.</summary>
        public const int MaximumDepth = 6;

        /// <summary>
        /// Ordered so that <c>All[i].Id == i</c>, each lane occupies a contiguous id block starting at
        /// its grade 1, and grades run 1..<see cref="MaximumDepth"/> without gaps. A prerequisite is
        /// derived from that order (<see cref="PrerequisiteOf"/>), so it can never point forward or
        /// across lanes. The OPS perk each grade unlocks is <c>CallSheet</c>'s (front, grade).
        /// </summary>
        public static readonly PerkDefinition[] All =
        {
            new PerkDefinition(0, Space, 1, "RECON PASS", "OPTICAL: one sweep reveals your satellite's circle.", "Satellite"),
            new PerkDefinition(1, Space, 2, "SAT CAMERA", "OPTICAL: hold the camera on a target.", "Camera"),
            new PerkDefinition(2, Space, 3, "OVERWATCH", "RADAR: follow moving units for 60 s.", "Eye"),
            new PerkDefinition(3, Space, 4, "STARFALL", "KINETIC: 3-5 small rods walked in a line.", "Burst"),
            new PerkDefinition(4, Space, 5, "ORBITAL ROD", "KINETIC: one big rod, 420 m blast.", "Bomb"),
            new PerkDefinition(5, Space, 6, "4TH MODULE SLOT", "Capstone: carry every module at once.", "Settings"),

            new PerkDefinition(6, Cyber, 1, "ELINT SWEEP", "Reveal enemy radars in your truck's reach.", "RadarDish"),
            new PerkDefinition(7, Cyber, 2, "DECOY BARRAGE", "Flares meet missiles fired at you.", "Formation"),
            new PerkDefinition(8, Cyber, 3, "RADAR BLIND", "Jam one radar deep.", "EyeOff"),
            new PerkDefinition(9, Cyber, 4, "SIPHON", "Steal enemy front funds.", "Coins"),
            new PerkDefinition(10, Cyber, 5, "SAM NET DOWN", "A whole SAM net goes dark.", "WaveSine"),
            new PerkDefinition(11, Cyber, 6, "EMP", "Capstone: a high-altitude burst blinds radars wide.", "Broadcast"),

            new PerkDefinition(12, Sof, 1, "JTAC LASE", "Your team lases targets for your bombs.", "Crosshair"),
            new PerkDefinition(13, Sof, 2, "RECON TEAM", "Your team reveals what is around it.", "MapPin"),
            new PerkDefinition(14, Sof, 3, "FORTIFY", "Dig in a held town.", "Shield"),
            new PerkDefinition(15, Sof, 4, "SABOTAGE STRIKE", "Your team destroys a revealed site.", "Skull"),
            new PerkDefinition(16, Sof, 5, "DIRECT ACTION", "Raid a site from where the team stands.", "Building"),
            new PerkDefinition(17, Sof, 6, "TIER ONE", "Capstone: missions +20% odds, exposure rises at half rate.", "Territory")
        };

        /// <summary>
        /// The node a grade hangs off: the previous grade in the same lane, or
        /// <see cref="PerkView.NoPrerequisite"/> for a lane's free grade 1.
        /// </summary>
        public static byte PrerequisiteOf(byte id) =>
            IsDefined(id) && All[id].Grade > 1 ? (byte)(id - 1) : PerkView.NoPrerequisite;

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
        /// One pick per paid grade. The n-th pick costs <c>n × scorePerPoint</c>, so picks get
        /// longer as they get better: the first lands early, the sixth needs the long sortie.
        /// Aces pay bonus picks on top of the ladder.
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
        /// Score still missing for the next pick, or -1 once the ladder is exhausted (or
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

        /// <summary>Picks committed; free grades and undefined (stale) bits cost nothing.</summary>
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

        public int AvailablePoints(int earnedPoints) => Math.Max(0, earnedPoints - SpentPoints);

        /// <summary>Grade 1 of every lane is always owned; other defined grades follow the mask.</summary>
        public bool Has(byte id) =>
            PerkCatalog.IsDefined(id) && (PerkCatalog.All[id].Grade == 1 || (Mask & (1u << id)) != 0u);

        /// <summary>The highest grade owned in <paramref name="lane"/>, or 0 for a lane the board does not have.</summary>
        public int GradeOf(string lane)
        {
            int grade = 0;
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition definition = PerkCatalog.All[i];
                if (definition.Lane == lane && definition.Grade > grade && Has(definition.Id)) grade = definition.Grade;
            }
            return grade;
        }

        /// <summary>A lane's grade 1, or a grade whose predecessor is already committed.</summary>
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

        /// <summary>Debug bypass: grants a grade without picks or order.</summary>
        public bool ForceUnlock(byte id)
        {
            if (!PerkCatalog.IsDefined(id) || Has(id)) return false;
            Mask |= 1u << id;
            return true;
        }
    }
}
