using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The STRIKE page's rules: a track's phase and progress from its feed, the soonest, danger close, the layout, the words.</summary>
    internal static class StrikeMathTests
    {
        public static void Run()
        {
            Phases();
            Progress();
            Selection();
            Layout();
            Words();
        }

        private static StrikeTrack Track(StrikeKind kind, float eta, float lead = 15f, int rounds = 1, float gap = 0f, int legs = 0, float linger = 0f, float range = 6400f) =>
            new StrikeTrack { Kind = kind, Eta = eta, Lead = lead, Rounds = rounds, RoundGap = gap, Legs = legs, Linger = linger, RangeM = range };

        private static void Phases()
        {
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Rod, 10f)), StrikePhase.Warn, "A rod is only warned before its release");
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Rod, 8f)), StrikePhase.Fall, "It falls from the release, eight seconds out");
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Rod, 3f)), StrikePhase.Fall, "...and is falling until it lands");
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Rod, -1f)), StrikePhase.Impact, "A landing stays on the track as IMPACT");
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Rod, -3.3f)), StrikePhase.Done, "...for the hold, then the track is DONE");
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Prsm, 6f, 6.5f)), StrikePhase.Flight, "PRSM flies");
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Prsm, -1f, 6.5f, 6, 0.35f)), StrikePhase.Impact, "A salvo keeps landing");
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Prsm, -3.9f, 6.5f, 6, 0.35f)), StrikePhase.Impact, "...past the hold of its first round, while the last one has not had its turn");
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Prsm, -5.1f, 6.5f, 6, 0.35f)), StrikePhase.Done, "...until the last round's hold is over");
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Emp, -5f, 3f, linger: 30f)), StrikePhase.Effect, "An EMP's effect runs on after the impact");
            TestAssert.Eq(StrikeMath.Phase(Track(StrikeKind.Emp, -31f, 3f, linger: 30f)), StrikePhase.Done, "...and ends");
            TestAssert.That(StrikeMath.Live(Track(StrikeKind.Decoy, -9f, 5.5f, linger: 20f)) && !StrikeMath.Live(Track(StrikeKind.Rod, -9f)), "Live while in the air, landing or in effect");
            TestAssert.That(StrikeMath.InFlight(Track(StrikeKind.Cruise, 0.1f)) && !StrikeMath.InFlight(Track(StrikeKind.Cruise, 0f)), "In flight until it lands");
        }

        private static void Progress()
        {
            TestAssert.Near(StrikeMath.Along(Track(StrikeKind.Rod, 10f)), 0f, "A warned rod has not moved");
            TestAssert.Near(StrikeMath.Along(Track(StrikeKind.Rod, 4f)), 0.5f, "Half way down its fall");
            TestAssert.Near(StrikeMath.Along(Track(StrikeKind.Rod, -1f)), 1f, "Landed");
            TestAssert.Near(StrikeMath.Curve(Track(StrikeKind.Rod, 4f)), (float)Math.Pow(0.5, 1.7), "The rod falls faster as it goes");
            TestAssert.Near(StrikeMath.Along(Track(StrikeKind.Prsm, 3.25f, 6.5f)), 0.5f, "PRSM flies evenly");
            TestAssert.Near(StrikeMath.Curve(Track(StrikeKind.Prsm, 3.25f, 6.5f)), 0.5f, "...on a straight curve parameter");
            TestAssert.Near(StrikeMath.Dwell(Track(StrikeKind.Rod, 15f)), 1f, "The warning ring is full at the call");
            TestAssert.Near(StrikeMath.Dwell(Track(StrikeKind.Rod, 8f)), 0f, "...and empty at the release");
            TestAssert.Near(StrikeMath.Dwell(Track(StrikeKind.Rod, 11.5f)), 0.5f, "...and counts down between");
            TestAssert.Eq(StrikeMath.Leg(Track(StrikeKind.Cruise, 20f, 22f, legs: 2)), 1, "The cruise starts on its first leg");
            TestAssert.Eq(StrikeMath.Leg(Track(StrikeKind.Cruise, 11f, 22f, legs: 2)), 2, "...goes to the second at a third...");
            TestAssert.Eq(StrikeMath.Leg(Track(StrikeKind.Cruise, 2f, 22f, legs: 2)), 3, "...and flies the last run to the target");
            TestAssert.Eq(StrikeMath.LegCount(Track(StrikeKind.Cruise, 2f, legs: 2)), 3, "Two waypoints make three legs");
            TestAssert.Eq(StrikeMath.LegCount(Track(StrikeKind.Cruise, 2f)), 1, "A direct cruise is one leg");
            TestAssert.Eq(StrikeMath.Splashes(Track(StrikeKind.Prsm, 1f, rounds: 6, gap: 0.35f)), 0, "Nothing has landed yet");
            TestAssert.Eq(StrikeMath.Splashes(Track(StrikeKind.Prsm, -0.4f, rounds: 6, gap: 0.35f)), 2, "A round lands every gap");
            TestAssert.Eq(StrikeMath.Splashes(Track(StrikeKind.Prsm, -9f, rounds: 6, gap: 0.35f)), 6, "...up to the salvo");
            TestAssert.Eq(StrikeMath.Splashes(Track(StrikeKind.Prsm, -0.1f)), 1, "A single round is one splash");
            TestAssert.Near(StrikeMath.SinceLast(Track(StrikeKind.Prsm, -2f, rounds: 6, gap: 0.35f)), 0.25f, "Seconds since the last round landed");
        }

        private static void Selection()
        {
            var tracks = new[] { Track(StrikeKind.Rod, 12f), Track(StrikeKind.Prsm, 3f), Track(StrikeKind.Cruise, 16f), Track(StrikeKind.Emp, -4f) };
            TestAssert.Eq(StrikeMath.Soonest(tracks, 3), 1, "The soonest in the air is the headline");
            TestAssert.Eq(StrikeMath.Soonest(tracks, 0), -1, "Nothing in the air: no soonest");
            TestAssert.Eq(StrikeMath.Soonest(new[] { Track(StrikeKind.Rod, -2f) }, 1), -1, "A landed strike is not the soonest");
            TestAssert.That(StrikeMath.Close(Track(StrikeKind.Prsm, 6f, range: 900f)), "A PRSM 0.9 km out is danger close");
            TestAssert.That(!StrikeMath.Close(Track(StrikeKind.Prsm, 6f, range: 6400f)), "...6 km out it is not");
            TestAssert.That(!StrikeMath.Close(Track(StrikeKind.Prsm, -1f, range: 900f)), "...and once landed it is over");
            TestAssert.That(!StrikeMath.Close(Track(StrikeKind.Emp, 3f, range: 500f)), "An EMP over you is not a blast");
            TestAssert.That(StrikeMath.Kinetic(StrikeKind.Rod) && StrikeMath.Kinetic(StrikeKind.Cruise) && !StrikeMath.Kinetic(StrikeKind.Decoy), "Kinetic kinds");
            TestAssert.That(StrikeMath.TraceTone(59f) == ScopeTone.Ink && StrikeMath.TraceTone(60f) == ScopeTone.Amber && StrikeMath.TraceTone(84f) == ScopeTone.Amber && StrikeMath.TraceTone(85f) == ScopeTone.Red, "Trace cautions at 60 and warns at 85");
            var cooling = new PerkTile { State = PerkPhase.Cooling, Cooldown = 150f, Total = 300f };
            TestAssert.Near(StrikeMath.PerkFill(cooling), 0.5f, "A perk's ring is the cooled share");
            TestAssert.Near(StrikeMath.PerkFill(new PerkTile { State = PerkPhase.Ready, Total = 30f }), 1f, "A ready perk is a full ring");
            TestAssert.Near(StrikeMath.SatFill(10f, 20f), 0.5f, "The SAT window's ring is its share left");
            TestAssert.That(StrikeMath.SatLate(3.9f) && !StrikeMath.SatLate(4.1f) && !StrikeMath.SatLate(0f), "The window cautions in its last four seconds");
        }

        private static void Layout()
        {
            TestAssert.Near(StrikeMath.Pitch(3, StrikeMath.BesideLower), 46f, "Three tracks beside the lower blocks are the prototype's 46");
            TestAssert.Near(StrikeMath.Pitch(2, StrikeMath.BesideLower), 70f, "Two share the room");
            TestAssert.Near(StrikeMath.Pitch(1, StrikeMath.BesideLower), 140f, "One track takes the room beside the lower blocks");
            TestAssert.Near(StrikeMath.Pitch(1, 240f), 150f, "...and with the whole page free, up to the cap");
            TestAssert.Near(StrikeMath.Pitch(1, 40f), 46f, "...but never less than 46");
            TestAssert.Near(StrikeMath.Pitch(0, 100f), 46f, "No track: the base pitch");
            TestAssert.Near(StrikeMath.LowerTop(2, 274f), 174f, "The lower blocks sit at the foot of a page with a strip");
            TestAssert.Near(StrikeMath.LowerTop(1, 300f), 200f, "...and of one without");
            TestAssert.Near(StrikeMath.LowerTop(0, 274f), 32f + (274f - 32f - 100f) / 2f, "With no track they centre in the free room");
            TestAssert.Near(StrikeMath.Start(1, 150f, 240f, false), 32f + 45f, "A lone track sits centred in the free room");
            TestAssert.Near(StrikeMath.Start(3, 80f, 240f, false), 32f, "...a full stack from the top");
            TestAssert.Near(StrikeMath.Start(1, 140f, 140f, true), 32f, "...and beside the lower blocks always from the top");
            TestAssert.Near(StrikeMath.Depth(46f), 1f, "The prototype's track is one unit deep");
            TestAssert.Near(StrikeMath.Depth(76f), 2f, "A taller track draws deeper");
        }

        private static void Words()
        {
            TestAssert.That(OpsScopeWords.Name(StrikeKind.Rod) == "KINETIC ROD" && OpsScopeWords.Short(StrikeKind.Rod) == "ROD", "The rod's words");
            TestAssert.That(OpsScopeWords.Name(StrikeKind.Emp) == "EMP STRIKE" && OpsScopeWords.Name(StrikeKind.Decoy) == "DECOY BARRAGE" && OpsScopeWords.Short(StrikeKind.Prsm) == "PRSM", "The other strikes' words");
            TestAssert.That(OpsScopeWords.Node(ChainNodeKind.Uplink) == "UPL" && OpsScopeWords.Node(ChainNodeKind.SamC2) == "C2" && OpsScopeWords.Node(ChainNodeKind.DataCenter) == "DC", "Node words");
            TestAssert.That(OpsScopeWords.Clock(291f) == "4:51" && OpsScopeWords.Clock(0f) == "0:00" && OpsScopeWords.Clock(-5f) == "0:00" && OpsScopeWords.Clock(59.2f) == "1:00", "Clocks round up");
            TestAssert.That(ReferenceEquals(OpsScopeWords.Clock(30f), OpsScopeWords.Clock(30f)), "A clock is cached, not rebuilt");
            TestAssert.That(OpsScopeWords.Perk(new PerkTile { State = PerkPhase.Ready }) == "RDY" && OpsScopeWords.Perk(new PerkTile { State = PerkPhase.Armed }) == "ARMED" && OpsScopeWords.Perk(new PerkTile { State = PerkPhase.Cooling, Cooldown = 120f }) == "2:00", "Perk words");
            StrikeTrack cruise = Track(StrikeKind.Cruise, 11f, 22f, legs: 2);
            TestAssert.That(OpsScopeWords.Phase(cruise, StrikePhase.Flight) == "LEG 2/3" && OpsScopeWords.Phase(Track(StrikeKind.Cruise, 5f), StrikePhase.Flight) == "FLIGHT", "A routed cruise names its leg");
            TestAssert.That(OpsScopeWords.Phase(Track(StrikeKind.Prsm, -1f), StrikePhase.Impact) == "SPLASH" && OpsScopeWords.Phase(Track(StrikeKind.Rod, -1f), StrikePhase.Impact) == "IMPACT", "PRSM splashes, the rest impact");
            TestAssert.That(OpsScopeWords.Phase(Track(StrikeKind.Rod, 10f), StrikePhase.Warn) == "WARN" && OpsScopeWords.Phase(Track(StrikeKind.Rod, 5f), StrikePhase.Fall) == "FALL" && OpsScopeWords.Phase(Track(StrikeKind.Emp, -3f), StrikePhase.Effect) == "ACTIVE", "Phase words");
            TestAssert.That(OpsScopeWords.Money(14200) == "$14.2K" && OpsScopeWords.Money(830) == "$830" && OpsScopeWords.Money(120000) == "$120K" && OpsScopeWords.Money(-5) == "$0", "Payout money");
            TestAssert.That(OpsScopeWords.Multiplier(1.15f) == "x1.15" && OpsScopeWords.Multiplier(1f) == null && OpsScopeWords.Multiplier(0f) == null, "The multiplier shows only when it is not 1");
        }
    }
}
