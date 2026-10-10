using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The persistent damage lamps.</summary>
    internal static class HudDamageTests
    {
        public static void Run()
        {
            var lamps = new Lamp[HudDamage.MaxLamps];

            TestAssert.That(HudDamage.Fill(new DamageState { Engines = 2 }, lamps) == 0 && !lamps[0].Present, "An undamaged aircraft has no damage lamps");

            int n = HudDamage.Fill(new DamageState { Flags = DamageFlags.EngineDamaged, Engines = 2 }, lamps);
            TestAssert.That(n == 1 && lamps[0].Legend == "ENG DMG" && lamps[0].State == LampState.Caution, "A hit engine cautions");

            n = HudDamage.Fill(new DamageState { Flags = DamageFlags.EngineDamaged, Engines = 2, EnginesOut = 1 }, lamps);
            TestAssert.That(n == 1 && lamps[0].Legend == "ENG 1 OUT" && lamps[0].State == LampState.Warning, "One of two engines out warns and counts; ENG DMG is subsumed");
            n = HudDamage.Fill(new DamageState { Engines = 1, EnginesOut = 1 }, lamps);
            TestAssert.That(n == 1 && lamps[0].Legend == "ENG OUT", "The only engine out reads ENG OUT");
            n = HudDamage.Fill(new DamageState { Engines = 2, EnginesOut = 2 }, lamps);
            TestAssert.That(lamps[0].Legend == "ENG OUT", "Every engine out reads ENG OUT");

            n = HudDamage.Fill(new DamageState { Flags = DamageFlags.FuelLeak }, lamps);
            TestAssert.That(n == 1 && lamps[0].Legend == "FUEL LEAK" && lamps[0].State == LampState.Caution, "A leak cautions");

            n = HudDamage.Fill(new DamageState { Flags = DamageFlags.Fire }, lamps);
            TestAssert.That(n == 1 && lamps[0].Legend == "FIRE" && lamps[0].State == LampState.Warning && lamps[0].Flash, "A fire flashes a warning");

            n = HudDamage.Fill(new DamageState { DetachedRatio = 0.05f }, lamps);
            TestAssert.That(n == 0, "A little loose paint is not damage");
            n = HudDamage.Fill(new DamageState { DetachedRatio = 0.13f }, lamps);
            TestAssert.That(n == 1 && lamps[0].Legend == "STR 87%" && lamps[0].State == LampState.Caution, "Structure left, as a caution");
            n = HudDamage.Fill(new DamageState { DetachedRatio = 0.4f }, lamps);
            TestAssert.That(n == 1 && lamps[0].Legend == "STR 60%" && lamps[0].State == LampState.Warning, "...a warning past a quarter lost");

            n = HudDamage.Fill(new DamageState
            {
                Flags = DamageFlags.Fire | DamageFlags.FuelLeak | DamageFlags.EngineDamaged, Engines = 2, EnginesOut = 1, DetachedRatio = 0.3f,
            }, lamps);
            TestAssert.That(n == 4 && lamps[0].Legend == "FIRE" && lamps[1].Legend == "ENG 1 OUT" && lamps[2].Legend == "FUEL LEAK" && lamps[3].Legend == "STR 70%",
                "Everything at once: severity order, capped at four");
            n = HudDamage.Fill(default, lamps);
            TestAssert.That(n == 0 && !lamps[0].Present && !lamps[3].Present, "Lamps clear when the damage does");
        }
    }
}
