using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>The chaff dispenser's decisions: its load, when it runs, when it is refilled, and the rows the menu gives it.</summary>
    internal static class ChaffRulesTests
    {
        public static void Run()
        {
            TestAssert.That(ChaffRules.Load(48) == 48 && ChaffRules.Load(8) == ChaffRules.MinLoad && ChaffRules.Load(400) == ChaffRules.MaxLoad, "the load follows the flare load, within bounds");
            TestAssert.That(ChaffRules.IsRadarSeeker("ARH") && ChaffRules.IsRadarSeeker("SARH") && !ChaffRules.IsRadarSeeker("IR") && !ChaffRules.IsRadarSeeker(null), "chaff answers the two radar seekers only");

            float beamClose = ChaffRules.SeduceChance(2000f, 1f), tailClose = ChaffRules.SeduceChance(2000f, 0f);
            TestAssert.Near(beamClose, ChaffRules.SeduceBase, "seduction: full chance on the beam inside 3 km");
            TestAssert.That(tailClose > 0f && tailClose < beamClose * 0.5f, "tail-on still has a chance, well under the beam's");
            TestAssert.That(ChaffRules.SeduceChance(9000f, 1f) == 0f && ChaffRules.SeduceChance(5500f, 1f) < beamClose, "nothing past 8 km, less on the way in");
            float salvo = 1f - (float)System.Math.Pow(1f - beamClose, 5);
            TestAssert.That(salvo > 0.5f && salvo < 0.6f, "a one-second salvo on the beam seduces about half the time: " + salvo);

            TestAssert.That(ChaffRules.ShouldRun(salvo: true, linked: false, countermeasureHeld: false, radarMissileInbound: false, ammo: 1), "a salvo runs whatever else is true");
            TestAssert.That(!ChaffRules.ShouldRun(true, true, true, true, 0), "an empty dispenser never runs");
            TestAssert.That(ChaffRules.ShouldRun(false, true, true, true, 10), "linked: the countermeasure button runs it at a radar missile");
            TestAssert.That(!ChaffRules.ShouldRun(false, true, true, false, 10), "linked: flares at a heat seeker spend no chaff");
            TestAssert.That(!ChaffRules.ShouldRun(false, true, false, true, 10), "linked: nothing without the button");
            TestAssert.That(!ChaffRules.ShouldRun(false, false, true, true, 10), "unlinked: the button leaves the chaff alone");

            TestAssert.That(ChaffRules.NextIsChaff(1, 2) && ChaffRules.NextIsChaff(0, 1), "Next Countermeasure from the last station lands on chaff");
            TestAssert.That(!ChaffRules.NextIsChaff(0, 2) && !ChaffRules.NextIsChaff(255, 2) && !ChaffRules.NextIsChaff(0, 0), "not from an earlier one, from the game's 'none', or with no station");

            TestAssert.That(ChaffRules.Refilled(rearmWasPending: true, rearmPending: false, flaresBefore: 10, flaresNow: 10), "a finished rearm refills it");
            TestAssert.That(ChaffRules.Refilled(false, false, 10, 48), "so do flares coming back up");
            TestAssert.That(!ChaffRules.Refilled(true, true, 10, 10) && !ChaffRules.Refilled(false, false, 10, 8), "a pending rearm or flares going down do not");

            MenuWorld w = MenuWorlds.Default();
            MenuModel m = MenuCatalog.Build(w);
            var defence = m.Category("defence").Rows;
            TestAssert.That(defence[0].Id == "defence/flares" && defence[1].Id == "defence/chaff", "POP CHAFF sits right under POP FLARES");
            TestAssert.That(m.Row("defence/chaff").State == "CHAFF: 40 / 48" && !m.Row("defence/chaff").IsBlocked, "and counts the bundles");
            TestAssert.That(m.Row("defence/chafflink").IsOn && m.Row("defence/chafflink").Label == "UNLINK CHAFF" && m.Row("defence/chafflink").State == "CHAFF: ON CM KEY", "the link switch says the button dispenses it");
            w.Chaff = 0; w.ChaffLinked = false;
            m = MenuCatalog.Build(w);
            TestAssert.That(m.Row("defence/chaff").Blocked == "CHAFF: EMPTY" && m.Row("defence/chafflink").Label == "LINK CHAFF TO CM KEY", "empty keeps the row, dimmed; unlinked offers the link");
            w.HasChaff = false;
            m = MenuCatalog.Build(w);
            TestAssert.That(m.Row("defence/chaff") == null && m.Row("defence/chafflink") == null, "no dispenser fitted: no chaff rows");
            TestAssert.That(MenuCatalog.Build(MenuWorlds.Ground()).Row("defence/chaff") == null && MenuCatalog.Build(MenuWorlds.Ground()).Row("defence/chafflink") != null, "on the apron only the switch remains, like the flares");

            m = MenuCatalog.Build(MenuWorlds.Default());
            TestAssert.That(m.Category("targets").Rows[0].Id == "targets/threat" && m.Row("targets/threat").State == "HOSTILE AIR: 3 · 18.4 KM", "LOCK NEAREST THREAT leads TARGETS and names the range");
            TestAssert.That(m.Row("targets/inbound").Blocked == "NO MISSILE INBOUND", "nothing inbound: the missile row stays, dimmed");
            w = MenuWorlds.Default(); w.Threats = 0; w.ThreatKm = -1f; w.Inbound = 2;
            m = MenuCatalog.Build(w);
            TestAssert.That(m.Row("targets/threat").Blocked == "NO HOSTILE AIR TRACKED" && !m.Row("targets/inbound").IsBlocked && m.Row("targets/inbound").State == "INBOUND: 2", "and the other way round");

            m = MenuCatalog.Build(MenuWorlds.Default());
            MenuRow bay = m.Row("weapons/bay");
            TestAssert.That(bay != null && bay.Label == "OPEN BAY DOORS" && !bay.Checkbox, "an airframe with a bay offers to hold it open");
            w = MenuWorlds.Default(); w.BayDoorsOpen = true;
            TestAssert.That(MenuCatalog.Build(w).Row("weapons/bay").Label == "CLOSE BAY DOORS", "and to hand it back");
            w.HasBayDoors = false;
            TestAssert.That(MenuCatalog.Build(w).Row("weapons/bay") == null, "no bay, no row");
        }
    }
}
