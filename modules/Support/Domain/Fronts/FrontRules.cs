using System;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    internal enum Front : byte { Space = 0, Cyber = 1, Sof = 2 }

    /// <summary>One enum for every front's posture; <see cref="FrontRules.IsValid"/> says which belong to which front.</summary>
    internal enum FrontDirective : byte { Recon = 0, Strike = 1, Defend = 2, Balanced = 3, Attack = 4, Sabotage = 5, Hold = 6 }

    /// <summary>Front programmes; <c>Readiness</c> (0) is retired with OPS M1 (skills replaced it): never queued, the value stays reserved.</summary>
    internal enum ProgrammeId : byte
    {
        Readiness = 0, LaunchSatellite = 1, UplinkSite = 2, Asat = 3, DataCenter = 4, EwTruck = 5, ZeroDay = 6, TrainTeam = 7, Camp = 8, Fob = 9
    }

    /// <summary>The ladder tier of a programme: infrastructure is built first, assets need it, operations need neither.</summary>
    internal enum BuildClass : byte { Infrastructure, Asset, Operation }

    internal readonly struct FrontProgramme
    {
        public readonly ProgrammeId Id;
        /// <summary>The one front the programme belongs to, or null for READINESS (every front has its own ladder).</summary>
        public readonly Front? Front;
        public readonly string Name;
        public readonly float Cost;
        public readonly float BuildSeconds;
        public readonly string Requires;
        public FrontProgramme(ProgrammeId id, Front? front, string name, float cost, float build, string requires)
        { Id = id; Front = front; Name = name; Cost = cost; BuildSeconds = build; Requires = requires; }
    }

    internal static class FrontRules
    {
        public const int FrontCount = 3, MaxQueue = 4, TeamCap = 4, LogCapacity = 8;
        public const int PadCap = 2, BirdsPerPad = 2, BirdCap = 3, DataCenterCap = 2, TruckCap = 2, CampCap = 2, TeamsPerBase = 2;
        public const float SeedFunds = 500f, BudgetCap = 1000f, MaxSeed = BudgetCap, QuartermasterIdleSeconds = 180f,
            TruckReachMetres = 20000f, TruckPackSeconds = 45f, PadPickMetres = 8000f, FobPickMetres = 1000f;
        public const float DefaultShare = 0.03f, DefaultShareCap = 250f, MaxShareCap = 5000f,
            /// <summary>Programme funds one donated allocation point buys (a sortie's ~40 allocation then buys about half a minute of the faction's whole trickle).</summary>
            DonateRate = 3f, MaxShare = 0.10f, ShareIntervalSeconds = 60f, LockSeconds = 60f;

        public static float ClampShare(float share) => float.IsNaN(share) ? DefaultShare : Math.Clamp(share, 0f, MaxShare);

        /// <summary>The table of spec 3.5, indexed by <see cref="ProgrammeId"/>. Row 0 (READINESS) is retired with OPS M1 and only keeps the index aligned: it belongs to no front.</summary>
        public static readonly FrontProgramme[] Programmes =
        {
            new FrontProgramme(ProgrammeId.Readiness, null, "READINESS", 0f, 0f, "RETIRED"),
            new FrontProgramme(ProgrammeId.LaunchSatellite, Front.Space, "SATELLITE", 300f, 90f, "A FREE PAD SLOT AND A KIND NOT UP"),
            new FrontProgramme(ProgrammeId.UplinkSite, Front.Space, "LAUNCH PAD", 200f, 120f, "A PAD BELOW MAX"),
            new FrontProgramme(ProgrammeId.Asat, Front.Space, "ASAT", 900f, 180f, ""),
            new FrontProgramme(ProgrammeId.DataCenter, Front.Cyber, "DATA CENTER", 250f, 120f, "A DATA CENTER BELOW MAX"),
            new FrontProgramme(ProgrammeId.EwTruck, Front.Cyber, "EW TRUCK", 120f, 90f, "A DATA CENTER WITH NO TRUCK"),
            new FrontProgramme(ProgrammeId.ZeroDay, Front.Cyber, "ZERO-DAY", 600f, 180f, ""),
            new FrontProgramme(ProgrammeId.TrainTeam, Front.Sof, "TEAM", 120f, 90f, "A CAMP OR FOB WITH ROOM"),
            new FrontProgramme(ProgrammeId.Camp, Front.Sof, "CAMP", 150f, 120f, "A CAMP BELOW MAX"),
            new FrontProgramme(ProgrammeId.Fob, Front.Sof, "FOB", 700f, 180f, "A HELD BUILDING"),
        };

        public static BuildClass Class(ProgrammeId id) =>
            id == ProgrammeId.UplinkSite || id == ProgrammeId.DataCenter || id == ProgrammeId.Camp || id == ProgrammeId.Fob ? BuildClass.Infrastructure :
            id == ProgrammeId.LaunchSatellite || id == ProgrammeId.EwTruck || id == ProgrammeId.TrainTeam ? BuildClass.Asset : BuildClass.Operation;

        /// <summary>On the ladder: infrastructure or asset, the FOB excepted (it is a one-off the ladder never walks to).</summary>

        public static FrontProgramme Info(ProgrammeId id) => Programmes[(int)id];

        public static bool BelongsTo(Front front, ProgrammeId id) =>
            (int)id < Programmes.Length && id != ProgrammeId.Readiness && Programmes[(int)id].Front == front;

        /// <summary>Config-scaled cost of a programme.</summary>
        public static float Cost(ProgrammeId id, float scale) => Math.Max(1f, (float)Math.Round(Info(id).Cost * Sane(scale)));

        public static float BuildSeconds(ProgrammeId id) => Info(id).BuildSeconds;

        /// <summary>A scale or factor from config or the wire: itself when finite and above 0, else 1.</summary>
        internal static float Sane(float scale) =>float.IsFinite(scale) && scale > 0f ? scale : 1f;

        public static bool IsValid(Front front, FrontDirective d)
        {
            switch (front)
            {
                case Front.Space: return d == FrontDirective.Recon || d == FrontDirective.Strike || d == FrontDirective.Defend;
                case Front.Cyber: return d == FrontDirective.Defend || d == FrontDirective.Balanced || d == FrontDirective.Attack;
                case Front.Sof: return d == FrontDirective.Recon || d == FrontDirective.Sabotage || d == FrontDirective.Hold;
                default: return false;
            }
        }

        public static FrontDirective DefaultDirective(Front front) => front == Front.Cyber ? FrontDirective.Balanced : FrontDirective.Recon;

        public static string Name(Front f) => f == Front.Space ? "SPACE" : f == Front.Cyber ? "CYBER" : "SOF";

        public static string Name(FrontDirective d) => d.ToString().ToUpperInvariant();

        /// <summary>m:ss, rounded up, as the lock reason shows it.</summary>
        public static string Clock(float seconds)
        {
            if (!float.IsFinite(seconds)) return "0:00";
            int s = (int)Math.Ceiling(Math.Min(Math.Max(0f, seconds), 1e9f));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
