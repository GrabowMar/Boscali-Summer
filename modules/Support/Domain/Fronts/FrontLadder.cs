namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>Builds a front has already queued, counted as coming by the ladder (Birds is a kind mask).</summary>
    internal struct QueuedCounts
    {
        public int Pads, DataCenters, Trucks, Camps, Teams, Fobs;
        public byte Birds;
    }

    /// <summary>The build ladder: infrastructure first, assets behind it, capacity counts live plus queued. Pure.</summary>
    internal static class FrontLadder
    {
        private static int Bits(int mask) { int n = 0; for (; mask != 0; mask &= mask - 1) n++; return n; }

        private static int Bases(in FrontWorld w, in QueuedCounts q) => w.CampsLive + q.Camps + (w.FobUp || q.Fobs > 0 ? 1 : 0);

        /// <summary>None when the build may be queued; otherwise the refusal naming its fix.</summary>
        public static FrontOutcome Check(Front f, ProgrammeId id, byte kind, in FrontWorld w, in QueuedCounts q)
        {
            if (!FrontRules.BelongsTo(f, id)) return FrontOutcome.NotAProgramme;
            int pads = w.PadsLive + q.Pads, dcs = w.DataCentersLive + q.DataCenters, bases = Bases(w, q);
            switch (id)
            {
                case ProgrammeId.UplinkSite: return pads >= FrontRules.PadCap ? FrontOutcome.UplinksMax : FrontOutcome.None;
                case ProgrammeId.LaunchSatellite:
                    int up = w.BirdsUp | q.Birds;
                    if (Bits(up) >= FrontRules.BirdCap) return FrontOutcome.BirdsUp;
                    if (kind < 8 && (up & (1 << kind)) != 0) return FrontOutcome.KindUp;
                    if (pads == 0) return FrontOutcome.NoPad;
                    return Bits(up) + 1 > FrontRules.BirdsPerPad * pads ? FrontOutcome.PadFull : FrontOutcome.None;
                case ProgrammeId.DataCenter: return dcs >= FrontRules.DataCenterCap ? FrontOutcome.DataCentersMax : FrontOutcome.None;
                case ProgrammeId.EwTruck:
                    if (w.TrucksLive + q.Trucks >= FrontRules.TruckCap) return FrontOutcome.TrucksMax;
                    if (dcs == 0) return FrontOutcome.NoDataCenter;
                    return w.TrucksLive + q.Trucks + 1 > dcs ? FrontOutcome.DataCenterFull : FrontOutcome.None;
                case ProgrammeId.Camp: return w.CampsLive + q.Camps >= FrontRules.CampCap ? FrontOutcome.CampsMax : FrontOutcome.None;
                case ProgrammeId.TrainTeam:
                    if (w.Teams + q.Teams >= FrontRules.TeamCap) return FrontOutcome.TeamCap;
                    if (bases == 0) return FrontOutcome.NoCamp;
                    return w.Teams + q.Teams + 1 > FrontRules.TeamsPerBase * bases ? FrontOutcome.CampFull : FrontOutcome.None;
                case ProgrammeId.Fob:
                    if (w.FobUp || q.Fobs > 0) return FrontOutcome.FobMax;
                    return w.HasHeldBuilding ? FrontOutcome.None : FrontOutcome.NoHeldBuilding;
                default: return FrontOutcome.None;
            }
        }

        /// <summary>The prerequisite is live now (live counts only); infrastructure and operations are always ready.</summary>
        public static bool Ready(ProgrammeId id, in FrontWorld w)
        {
            switch (id)
            {
                case ProgrammeId.LaunchSatellite: return Bits(w.BirdsUp) + 1 <= FrontRules.BirdsPerPad * w.PadsLive;
                case ProgrammeId.EwTruck: return w.TrucksLive + 1 <= w.DataCentersLive;
                case ProgrammeId.TrainTeam: return w.Teams + 1 <= FrontRules.TeamsPerBase * (w.CampsLive + (w.FobUp ? 1 : 0));
                default: return true;
            }
        }

        /// <summary>The next ladder step (queued rows count as present), or false when the ladder is complete.</summary>
        public static bool Next(Front f, in FrontWorld w, in QueuedCounts q, out ProgrammeId id, out byte kind)
        {
            kind = 0; id = ProgrammeId.Readiness;
            switch (f)
            {
                case Front.Space:
                    int up = w.BirdsUp | q.Birds, pads = w.PadsLive + q.Pads, birds = Bits(up);
                    if (birds < FrontRules.BirdCap && birds + 1 <= FrontRules.BirdsPerPad * pads)
                    {
                        while ((up & (1 << kind)) != 0) kind++;
                        id = ProgrammeId.LaunchSatellite; return true;
                    }
                    id = ProgrammeId.UplinkSite; return pads < FrontRules.PadCap;
                case Front.Cyber:
                    int dcs = w.DataCentersLive + q.DataCenters, trucks = w.TrucksLive + q.Trucks;
                    if (trucks < FrontRules.TruckCap && trucks + 1 <= dcs) { id = ProgrammeId.EwTruck; return true; }
                    id = ProgrammeId.DataCenter; return dcs < FrontRules.DataCenterCap;
                case Front.Sof:
                    int teams = w.Teams + q.Teams;
                    if (teams < FrontRules.TeamCap && teams + 1 <= FrontRules.TeamsPerBase * Bases(w, q)) { id = ProgrammeId.TrainTeam; return true; }
                    id = ProgrammeId.Camp; return w.CampsLive + q.Camps < FrontRules.CampCap;
                default: return false;
            }
        }
    }
}
