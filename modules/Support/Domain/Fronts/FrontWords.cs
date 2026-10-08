using System;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>
    /// The verdict of a front command (wire value of a <c>SpaceReply</c> outcome byte; append only) and of every programme refusal. The host never sends words:
    /// the client words a code with <see cref="FrontWords"/>.
    /// </summary>
    internal enum FrontOutcome : byte
    {
        None = 0, Ok = 1, Queued = 2, Donated = 3, DirectiveSet = 4, PrioritySet = 5, FocusSet = 6, FocusCleared = 7,
        NotAProgramme = 8, AlreadyQueued = 9, QueueFull = 10, ReadinessMax = 11, BirdsUp = 12, UplinksMax = 13, NeedsCyber3 = 14, DataCentersMax = 15,
        TrucksMax = 16, TeamCap = 17, CampsMax = 18, NoHeldBuilding = 19, NoSamNet = 20, NotQueued = 21, BadAmount = 22, LowAllocation = 23,
        NoDonations = 24, Locked = 25, BadPosture = 26, BadWeights = 27, Offline = 28, Unavailable = 29, RateLimited = 30, BadFront = 31,
        /// <summary>GEO burn verdicts (Detail = seconds of burn, Charged = fuel percent spent; for NoFuel Detail = percent needed).</summary>
        Relocated = 32, NoFuel = 33, Burning = 34, NoMove = 35, BirdLost = 36
    }

    /// <summary>Every front sentence, built from a few bytes on the client (radio style, upper case). Pure.</summary>
    internal static class FrontWords
    {
        public const byte MaxOutcome = (byte)FrontOutcome.BirdLost;

        public static bool Good(FrontOutcome o) => (o >= FrontOutcome.Ok && o <= FrontOutcome.FocusCleared) || o == FrontOutcome.Relocated;

        public static string Callsign(Front f) => f == Front.Space ? "DARKSTAR" : f == Front.Cyber ? "HEXWARD" : "SHADOW";

        public static string Programme(ProgrammeId id, int rung = 0) =>
            id == ProgrammeId.Readiness && rung > 0 ? "READINESS " + Math.Min(FrontRules.MaxReadiness, rung) : FrontRules.Info(id).Name;

        public static string Refusal(FrontOutcome code, Front front)
        {
            switch (code)
            {
                case FrontOutcome.NotAProgramme: return "NOT A " + FrontRules.Name(front) + " PROGRAMME";
                case FrontOutcome.AlreadyQueued: return "ALREADY QUEUED";
                case FrontOutcome.QueueFull: return "QUEUE FULL, WAIT FOR A PROGRAMME TO FINISH";
                case FrontOutcome.ReadinessMax: return "READINESS ALREADY AT MAX";
                case FrontOutcome.BirdsUp: return "ALL SATELLITES ARE UP";
                case FrontOutcome.UplinksMax: return "UPLINKS AT MAX";
                case FrontOutcome.NeedsCyber3: return "NEEDS CYBER READINESS 3";
                case FrontOutcome.DataCentersMax: return "DATA CENTERS AT MAX";
                case FrontOutcome.TrucksMax: return "TRUCKS AT MAX";
                case FrontOutcome.TeamCap: return "TEAM CAP REACHED";
                case FrontOutcome.CampsMax: return "CAMPS AT MAX";
                case FrontOutcome.NoHeldBuilding: return "HOLD A BUILDING FIRST";
                case FrontOutcome.NoSamNet: return "NO SAM NET REVEALED YET, SWEEP FOR ONE";
                default: return "";
            }
        }

        /// <summary>The reply sentence. <paramref name="detail"/> is seconds for a lock and the allocation needed for LOW ALLOCATION; <paramref name="by"/> is who holds a lock; <paramref name="amount"/> is what a donation took.</summary>
        public static string Outcome(FrontOutcome code, Front front, int detail = 0, string by = "", int amount = 0)
        {
            string r = Refusal(code, front);
            if (r.Length > 0) return r;
            switch (code)
            {
                case FrontOutcome.Ok: return "DONE";
                case FrontOutcome.Queued: return "PROGRAMME QUEUED";
                case FrontOutcome.Donated: return "FUNDED " + amount + " ALLOCATION";
                case FrontOutcome.DirectiveSet: return FrontRules.Name(front) + " POSTURE SET";
                case FrontOutcome.PrioritySet: return "FUNDING PRIORITY SET";
                case FrontOutcome.FocusSet: return "FOCUS PIN SET";
                case FrontOutcome.FocusCleared: return "FOCUS PIN CLEARED";
                case FrontOutcome.NotQueued: return "THAT PROGRAMME IS NOT QUEUED";
                case FrontOutcome.BadAmount: return "NOTHING TO FUND";
                case FrontOutcome.LowAllocation: return "NEEDS " + Math.Max(1, detail) + " ALLOCATION";
                case FrontOutcome.NoDonations: return "DONATIONS ARE OFF ON THIS SERVER";
                case FrontOutcome.Locked: return "LOCKED " + FrontRules.Clock(detail) + " BY " + (string.IsNullOrEmpty(by) ? "?" : by);
                case FrontOutcome.BadPosture: return "NOT A " + FrontRules.Name(front) + " POSTURE";
                case FrontOutcome.BadWeights: return "GIVE AT LEAST ONE FRONT A SHARE";
                case FrontOutcome.Offline: return "FRONTS ARE OFFLINE";
                case FrontOutcome.RateLimited: return "TOO FAST, WAIT A MOMENT";
                case FrontOutcome.BadFront: return "NO SUCH FRONT";
                case FrontOutcome.Relocated: return "BURN STARTED · ETA " + FrontRules.Clock(detail) + " · FUEL -" + amount + " %";
                case FrontOutcome.NoFuel: return "NOT ENOUGH FUEL · NEEDS " + Math.Max(1, detail) + " %";
                case FrontOutcome.Burning: return "BIRD IS MID-BURN · WAIT FOR IT TO PARK";
                case FrontOutcome.NoMove: return "ALREADY OVER THAT POINT";
                case FrontOutcome.BirdLost: return "THAT SATELLITE IS LOST";
                default: return "FRONTS UNAVAILABLE";
            }
        }

        private static string Anchor(ProgrammeId id) =>
            id == ProgrammeId.LaunchSatellite ? "SATELLITE" : id == ProgrammeId.UplinkSite ? "UPLINK" : id == ProgrammeId.DataCenter ? "DATA CENTER" :
            id == ProgrammeId.EwTruck ? "EW TRUCK" : id == ProgrammeId.Camp ? "CAMP" : FrontRules.Info(id).Name;

        public static string Bird(int kind) => kind == 0 ? "OPTICAL" : kind == 1 ? "RADAR" : "KINETIC";

        /// <summary>The effect of a counter: what the front's lead does to the enemy.</summary>
        public static string CounterEffect(Front f) =>
            f == Front.Space ? "ENEMY SOF EXPOSURE +50 %" : f == Front.Cyber ? "ENEMY SPACE COOLDOWNS x1.5" : "ENEMY CYBER TRACE +30 %";

        /// <summary>One log row as a radio line. <paramref name="directiveBy"/> names the pilot behind a posture change that is still locked ("" otherwise).</summary>
        public static string Line(Front f, in FrontLogRow row, string directiveBy = "")
        {
            string call = Callsign(f) + " · ";
            var id = (ProgrammeId)Math.Max(0, Math.Min(FrontRules.Programmes.Length - 1, (int)row.A));
            switch ((FrontLogCode)row.Code)
            {
                case FrontLogCode.Queued: return call + Programme(id, row.B + 1) + " QUEUED";
                case FrontLogCode.Started: return call + Programme(id, 0) + " FUNDED, BUILD " + FrontRules.Clock(row.B);
                case FrontLogCode.Done:
                    return call + (id == ProgrammeId.Readiness ? "READINESS UP, NOW " + row.B
                        : id == ProgrammeId.LaunchSatellite ? "SATELLITE LAUNCHED" : Programme(id) + " COMPLETE");
                case FrontLogCode.Effect: return call + "SATELLITE ON STATION · " + Bird(row.B);
                case FrontLogCode.Directive:
                    return FrontRules.Name(f) + " POSTURE " + FrontRules.Name((FrontDirective)Math.Max(0, Math.Min(6, (int)row.A))) +
                        (string.IsNullOrEmpty(directiveBy) ? "" : " BY " + directiveBy);
                case FrontLogCode.Priority:
                    return "FUNDING SPACE " + row.A + " · CYBER " + row.B + " · SOF " + Math.Max(0, 100 - row.A - row.B);
                case FrontLogCode.Focus: return call + "FOCUS PIN SET";
                case FrontLogCode.Rebuild: return call + Anchor(id) + " LOST, REBUILD QUEUED";
                case FrontLogCode.Counter:
                    return call + (row.A != 0 ? "COUNTER PRESSURE ACTIVE · " + CounterEffect(f) : "COUNTER PRESSURE LAPSED");
                default: return "";
            }
        }
    }
}
