using System;
using BoscaliSummer.Core.Chatter;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>One event of the wingman net as the code raises it: the packs must hold it with this priority and may use these facts (docs/CHATTER_PACKS.md).</summary>
    internal readonly struct WingCue
    {
        public readonly string Event;
        public readonly ChatterPriority Priority;
        public readonly string[] Facts;
        /// <summary>Every fact is there every time the event is raised (a line may rely on its tokens).</summary>
        public readonly bool FactsAlways;
        /// <summary>One call for the whole wing inside the repeat window, whoever says it.</summary>
        public readonly bool WingWide;

        public WingCue(string evt, ChatterPriority priority, bool wingWide = false, bool factsAlways = true, params string[] facts)
        {
            Event = evt;
            Priority = priority;
            WingWide = wingWide;
            FactsAlways = factsAlways;
            Facts = facts ?? Array.Empty<string>();
        }
    }

    /// <summary>The wingman net's events. The chatter test holds the built-in packs to this table.</summary>
    internal static class WingCues
    {
        public const string Copy = "wing.copy", Unable = "wing.unable", TaskDone = "wing.taskdone",
            Taxiing = "wing.taxiing", HoldingShort = "wing.holdingshort", LiningUp = "wing.liningup", Rolling = "wing.rolling",
            Airborne = "wing.airborne", Down = "wing.down", GoAround = "wing.goaround",
            Engaging = "wing.engaging", Fox2 = "wing.fox2", Fox3 = "wing.fox3", Magnum = "wing.magnum", Rifle = "wing.rifle", Splash = "wing.splash",
            Bandit = "wing.bandit", Contact = "wing.contact", BogeyDope = "wing.bogeydope", Clean = "wing.clean",
            Panic = "wing.panic", DefensiveClear = "wing.defensiveclear", PullUp = "wing.pullup", BreakOff = "wing.breakoff",
            Maneuvering = "wing.maneuvering", ManeuverDone = "wing.maneuverdone", FallingBack = "wing.fallingback", Rejoining = "wing.rejoining",
            Winchester = "wing.winchester", OutOfAmmo = "wing.outofammo", Joker = "wing.joker", Bingo = "wing.bingo", FallingBehind = "wing.fallingbehind",
            Damaged = "wing.damaged", AirframeLost = "wing.airframelost", Ejected = "wing.ejected", EscortLost = "wing.escortlost";

        private const ChatterPriority Amb = ChatterPriority.Ambient, Sta = ChatterPriority.Status, Tac = ChatterPriority.Tactical, Emg = ChatterPriority.Emergency;

        public static readonly WingCue[] All =
        {
            new WingCue(Copy, Sta, wingWide: true), new WingCue(Unable, Sta, wingWide: true), new WingCue(TaskDone, Sta, wingWide: true),
            new WingCue(Taxiing, Amb), new WingCue(HoldingShort, Amb), new WingCue(LiningUp, Amb), new WingCue(Rolling, Sta),
            new WingCue(Airborne, Sta), new WingCue(Down, Amb), new WingCue(GoAround, Sta),
            new WingCue(Engaging, Tac, wingWide: true),
            new WingCue(Fox2, Tac, false, false, "target"), new WingCue(Fox3, Tac, false, false, "target"),
            new WingCue(Magnum, Tac, false, false, "target"), new WingCue(Rifle, Tac, false, false, "target"), new WingCue(Splash, Tac, false, false, "target"),
            new WingCue(Bandit, Tac, false, true, "bra", "type"), new WingCue(Contact, Sta, false, true, "bra", "type"),
            new WingCue(BogeyDope, Tac, false, true, "bra", "type"), new WingCue(Clean, Tac),
            new WingCue(Panic, Emg), new WingCue(DefensiveClear, Tac), new WingCue(PullUp, Emg), new WingCue(BreakOff, Emg),
            new WingCue(Maneuvering, Tac, true, true, "detail"), new WingCue(ManeuverDone, Sta),
            new WingCue(FallingBack, Tac, wingWide: true), new WingCue(Rejoining, Sta, wingWide: true),
            new WingCue(Winchester, Sta), new WingCue(OutOfAmmo, Sta), new WingCue(Joker, Sta, false, true, "n"), new WingCue(Bingo, Sta), new WingCue(FallingBehind, Sta),
            new WingCue(Damaged, Sta), new WingCue(AirframeLost, Tac, false, true, "n"), new WingCue(Ejected, Tac, false, true, "n"), new WingCue(EscortLost, Tac, wingWide: true),
        };

        public static bool WingWide(string evt)
        {
            foreach (WingCue cue in All) if (cue.Event == evt) return cue.WingWide;
            return false;
        }

        /// <summary>The launch call: at an aircraft Fox 2 (infrared) or Fox 3; at the ground Magnum (anti-radiation) or Rifle.</summary>
        public static string Shot(bool air, bool infrared, bool antiRadar) => air ? (infrared ? Fox2 : Fox3) : antiRadar ? Magnum : Rifle;
    }
}
