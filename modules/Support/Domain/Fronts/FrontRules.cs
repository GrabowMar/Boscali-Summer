using System;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    internal enum Front : byte { Space = 0, Cyber = 1, Sof = 2 }

    /// <summary>One enum for every front's posture; <see cref="FrontRules.IsValid"/> says which belong to which front.</summary>
    internal enum FrontDirective : byte { Recon = 0, Strike = 1, Defend = 2, Balanced = 3, Attack = 4, Sabotage = 5, Hold = 6 }

    internal enum ProgrammeId : byte
    {
        Readiness = 0, LaunchSatellite = 1, UplinkSite = 2, Asat = 3, DataCenter = 4, EwTruck = 5, ZeroDay = 6, TrainTeam = 7, Camp = 8, Fob = 9
    }

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
        public const int FrontCount = 3, MaxQueue = 4, MaxReadiness = 5, TeamCap = 4, LogCapacity = 8;
        public const float DefaultShare = 0.03f, DefaultShareCap = 250f, MaxShareCap = 5000f,
            /// <summary>Programme funds one donated allocation point buys (a sortie's ~40 allocation then buys about half a minute of the faction's whole trickle).</summary>
            DonateRate = 3f, MaxShare = 0.10f, ShareIntervalSeconds = 60f, LockSeconds = 60f;
        public const int AsatCyberReadiness = 3;

        public static float ClampShare(float share) => float.IsNaN(share) ? DefaultShare : Math.Max(0f, Math.Min(MaxShare, share));

        /// <summary>The table of spec 3.5. READINESS is listed at rung 1 (cost 150, 120 s); rung n costs 150*n and builds 120*n s.</summary>
        public static readonly FrontProgramme[] Programmes =
        {
            new FrontProgramme(ProgrammeId.Readiness, null, "READINESS", 150f, 120f, "NEXT RUNG ONLY, MAX 5"),
            new FrontProgramme(ProgrammeId.LaunchSatellite, Front.Space, "LAUNCH SATELLITE", 300f, 240f, "A SATELLITE DOWN"),
            new FrontProgramme(ProgrammeId.UplinkSite, Front.Space, "UPLINK SITE", 200f, 180f, "AN UPLINK BELOW MAX"),
            new FrontProgramme(ProgrammeId.Asat, Front.Space, "ASAT", 900f, 180f, "CYBER READINESS 3"),
            new FrontProgramme(ProgrammeId.DataCenter, Front.Cyber, "DATA CENTER", 250f, 180f, "A DATA CENTER BELOW MAX"),
            new FrontProgramme(ProgrammeId.EwTruck, Front.Cyber, "EW TRUCK", 120f, 120f, "A TRUCK BELOW MAX"),
            new FrontProgramme(ProgrammeId.ZeroDay, Front.Cyber, "ZERO-DAY", 600f, 180f, ""),
            new FrontProgramme(ProgrammeId.TrainTeam, Front.Sof, "TRAIN TEAM", 120f, 120f, "FEWER THAN 4 TEAMS"),
            new FrontProgramme(ProgrammeId.Camp, Front.Sof, "CAMP", 150f, 180f, "A CAMP BELOW MAX"),
            new FrontProgramme(ProgrammeId.Fob, Front.Sof, "FOB", 700f, 180f, "A HELD BUILDING"),
        };

        public static FrontProgramme Info(ProgrammeId id) => Programmes[(int)id];

        public static bool BelongsTo(Front front, ProgrammeId id) =>
            (int)id < Programmes.Length && (id == ProgrammeId.Readiness || Programmes[(int)id].Front == front);

        /// <summary>Config-scaled cost of a programme (READINESS at the given current rung n: 150*n).</summary>
        public static float Cost(ProgrammeId id, int rung, float scale) =>
            Math.Max(1f, (float)Math.Round(Info(id).Cost * (id == ProgrammeId.Readiness ? Math.Max(1, rung) : 1) * Sane(scale)));

        public static float Cost(ProgrammeId id, float scale) => Cost(id, 1, scale);

        public static float BuildSeconds(ProgrammeId id, int rung) => Info(id).BuildSeconds * (id == ProgrammeId.Readiness ? Math.Max(1, rung) : 1);

        private static float Sane(float scale) => float.IsFinite(scale) && scale > 0f ? scale : 1f;

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
            int s = (int)Math.Ceiling(Math.Max(0f, seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
