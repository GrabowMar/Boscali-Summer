using BoscaliSummer.Modules.Autopilot.Domain;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.QoL.Domain;
using BoscaliSummer.Tests.Features.Autopilot;

namespace BoscaliSummer.Tests.Features.QoL
{
    /// <summary>The night-vision tube: metered gain, gating on a bright source, what counts as hot, and its lamp and menu row.</summary>
    internal static class NvgGainTests
    {
        public static void Run()
        {
            const float min = 0f, max = 5f, target = 0.06f;

            // AUTO aims the average at the target and stays inside the tube's range.
            TestAssert.That(System.Math.Abs(NvgGain.Wanted(0, 0.015f, target, min, max) - 2f) < 1e-3f, "A frame two stops under the target wants two stops of gain");
            TestAssert.That(NvgGain.Wanted(0, 0f, target, min, max) == max, "A black frame wants the maximum, not infinity");
            TestAssert.That(NvgGain.Wanted(0, 10f, target, min, max) == min, "A daylight frame wants the minimum");
            TestAssert.That(NvgGain.Wanted(1, 0f, target, min, max) < NvgGain.Wanted(2, 0f, target, min, max) &&
                NvgGain.Wanted(2, 0f, target, min, max) < NvgGain.Wanted(3, 0f, target, min, max), "LOW < MED < HIGH");
            TestAssert.That(NvgGain.Wanted(3, 10f, target, min, max) == max, "A fixed step ignores the meter");
            TestAssert.That(NvgGain.Metered(0.01f, 2f) > NvgGain.Metered(0.01f, 0.01f) * 10f, "One bright patch pulls the reading up");

            // Switch-on: the tube starts under its floor and warms up.
            var gain = new NvgGain();
            gain.Start(min);
            TestAssert.That(gain.Ev < min && !gain.Gating, "A cold tube starts dark and is not gating");
            for (int i = 0; i < 600; i++) gain.Step(4f, 1f / 60f);
            TestAssert.That(gain.Ev == 4f && !gain.Gating, "Ten seconds later it has settled on the wanted gain");

            // A flash: the gain falls within a few frames, the lamp lights, and it recovers slowly.
            for (int i = 0; i < 12; i++) gain.Step(1f, 1f / 60f);
            TestAssert.That(gain.Ev < 1.5f, "A bright source cuts the gain in a fifth of a second");
            TestAssert.That(gain.Gating, "...and that counts as gating");
            for (int i = 0; i < 30; i++) gain.Step(4f, 1f / 60f);
            TestAssert.That(gain.Ev < 2.5f && gain.Gating, "Half a second after the flash the tube is still recovering");
            for (int i = 0; i < 300; i++) gain.Step(4f, 1f / 60f);
            TestAssert.That(gain.Ev == 4f && !gain.Gating, "Five seconds after it is back");

            // A lasting bright scene (dawn) stops counting as gating.
            for (int i = 0; i < 900; i++) gain.Step(1f, 1f / 60f);
            TestAssert.That(gain.Ev == 1f && !gain.Gating, "Fifteen seconds into a bright scene the lamp is out");

            // Heat comes from the game's infrared sources.
            TestAssert.That(NvgGain.Heat01(0f, false) == 0f, "A cold parked aircraft does not glow");
            TestAssert.That(NvgGain.Heat01(0f, true) > 0f, "A vehicle or ship without an infrared source is still warm");
            TestAssert.That(NvgGain.Heat01(2.5f, false) > NvgGain.Heat01(0.3f, false), "More infrared, more glow");
            TestAssert.That(NvgGain.Heat01(40f, false) == 1f, "An afterburner saturates");

            // The NVG lamp carries the gain word only while the goggles are on.
            var lamps = new Lamp[HudLamps.Count];
            HudLamps.Fill(new SystemStates { NightVision = true, NvgLegend = "NVG GATE" }, lamps);
            TestAssert.That(lamps[4].Legend == "NVG GATE" && lamps[4].State == LampState.On, "Goggles on: the lamp shows the gain state");
            HudLamps.Fill(new SystemStates { NightVision = false, NvgLegend = "NVG A" }, lamps);
            TestAssert.That(lamps[4].Legend == "NVG", "Goggles off: plain NVG");
            HudLamps.Fill(new SystemStates { NightVision = true }, lamps);
            TestAssert.That(lamps[4].Legend == "NVG", "Vanilla night vision: plain NVG");

            // The C-menu offers the gain step only while the goggles are on and metering.
            MenuWorld w = MenuWorlds.Default();
            w.NvgOn = true;
            TestAssert.That(Find(MenuCatalog.Build(w), "lights/nvg-gain") == null, "Vanilla night vision has no gain row");
            w.HasNvgGain = true;
            w.NvgGain = 2;
            MenuRow row = Find(MenuCatalog.Build(w), "lights/nvg-gain");
            TestAssert.That(row != null && row.State == "NVG GAIN: MED", "The gain row names the current step");
            w.NvgOn = false;
            TestAssert.That(Find(MenuCatalog.Build(w), "lights/nvg-gain") == null, "Goggles off: no gain row");
        }

        private static MenuRow Find(MenuModel model, string id)
        {
            foreach (MenuRow row in MenuCatalogTests.AllRows(model)) if (row.Id == id) return row;
            return null;
        }
    }
}
