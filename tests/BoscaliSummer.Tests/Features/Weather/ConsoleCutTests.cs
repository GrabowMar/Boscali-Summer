using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The console's tape controls of slice 4b: the STORY stepper and its words, the CUT edits (story, re-roll, pin, scenario) as one rebase plus one placement, scenarios as
    /// <c>Rebased(...).With(story, pin, sets)</c> presets, and the enable rules of the controls that left the 4B tag.
    /// </summary>
    internal static class ConsoleCutTests
    {
        public static void Run()
        {
            StoryStepperWalksTheFiveStories();
            StoryWordsFitTheWindow();
            StoryControlsAreLiveForTheHost();
            WithIsTheChainInOneEdit();
            StoryAndRerollAreCutsThatPlaceTheNewTape();
            ScenariosAreStoryPinAndSetsPresets();
        }

        private static void StoryStepperWalksTheFiveStories()
        {
            TestAssert.That(WeatherStories.Count == 5 && WeatherKey.StoryCount == 5, "the first five stories are on the stepper");
            TestAssert.That(MetOfficeRules.StoryStep(-1, 5, 1) == 0 && MetOfficeRules.StoryStep(-1, 5, -1) == 4, "from AUTO the arrows enter at the first or the last story");
            TestAssert.That(MetOfficeRules.StoryStep(4, 5, 1) == 0 && MetOfficeRules.StoryStep(0, 5, -1) == 4 && MetOfficeRules.StoryStep(2, 5, 1) == 3, "and wrap");
            TestAssert.That(MetOfficeRules.StoryStep(0, 0, 1) == 0, "an empty table cannot divide by zero");
        }

        private static void StoryWordsFitTheWindow()
        {
            TestAssert.That(MetOfficeText.StoryValue(-1, 5, "Cold front", true) == "AUTO · COLD FRONT", "AUTO shows the story under the camera");
            TestAssert.That(MetOfficeText.StoryValue(0, 5, "Frontal system", true) == "FRONTAL SYSTEM 1 / 5", "a chosen story shows its number");
            TestAssert.That(MetOfficeText.StoryValue(-1, 5, "", false) == "— / 5", "no tape, no story");
            for (int s = -1; s < WeatherStories.Count; s++)
                TestAssert.That(MetOfficeText.LogLine(0f, "HOST", MetOfficeText.StoryNote(s)).Length <= MetOfficeText.LogColumns && MetOfficeText.StoryNote(s).Contains("▲ CUT"),
                    "the log line of story " + s + " fits the card and says CUT");
            TestAssert.That(MetOfficeText.StoryNote(-1) == "STORY AUTO ▲ CUT" && MetOfficeText.StoryNote(1).StartsWith("STORY 2 "), "the notes");
        }

        private static MetOfficeModel Ready() { var m = new MetOfficeModel { Host = true, Captured = true, HasCamera = true }; return m; }

        private static void StoryControlsAreLiveForTheHost()
        {
            foreach (MetControl id in new[] { MetControl.StoryAuto, MetControl.StoryPrev, MetControl.StoryNext })
            {
                TestAssert.That(MetCatalog.Info(id).Avail == MetAvail.Live && MetCatalog.Info(id).Effect == MetEffect.Cut, id + " is live and a CUT");
                TestAssert.That(MetOfficeRules.State(id, Ready()).Enabled, id + " is on for a ready host");
                MetOfficeModel client = Ready(); client.Host = false;
                TestAssert.That(!MetOfficeRules.State(id, client).Enabled && MetOfficeRules.State(id, client).Why == MetOfficeRules.Client, id + " names the host seat for a client");
                TestAssert.That(MetCatalog.Info(id).Help.Length <= MetCatalog.HelpLimit && !MetCatalog.Info(id).Help.Contains("4B"), id + " has a one-line help that no longer says 4B");
            }
            TestAssert.That(MetOfficeRules.Status(Ready()).Contains("S5") && !MetOfficeRules.Status(Ready()).Contains("LEGACY"), "the ready status says the tape is held until S5");
            TestAssert.That(MetCatalog.TagHelp.Length <= MetCatalog.HelpLimit, "the tag help fits one line");
        }

        private static void WithIsTheChainInOneEdit()
        {
            var basis = new WeatherKey(9u, 100f, true, 3, sets: 5, layoutSalt: 2, hasAnchor: true, anchorX: 10f, anchorZ: 20f, story: 1, skyPin: 4, openingBase: 1500f, headingDeg: 120f, offset: 33000f, systemSpeed: 31f);
            WeatherKey one = basis.With(2, 40, 18), chain = basis.WithStory(2).WithSkyPin(40).WithSets(18);
            TestAssert.That(one.Equals(chain), "With is WithStory + WithSkyPin + WithSets in one edit");
            TestAssert.That(one.Offset == basis.Offset && one.Epoch == basis.Epoch && one.Dynamic == basis.Dynamic && one.HeadingDeg == basis.HeadingDeg && one.HasAnchor, "and keeps the kinematics and the frame");
            TestAssert.That(one.TapeId != basis.TapeId && basis.With(basis.Story, basis.SkyPin, 18).TapeId == basis.TapeId, "story and pin change the tape identity, the sets do not");
        }

        private static void StoryAndRerollAreCutsThatPlaceTheNewTape()
        {
            for (uint seed = 1; seed <= 30; seed++)
            {
                var live = new WeatherKey(seed, 0f, true, (byte)(seed % 7), layoutSalt: (byte)(seed == 5 ? 255 : 3), headingDeg: seed * 37f % 360f, offset: 90000f, systemSpeed: 27f, sets: 3);
                float now = 1500f;
                foreach (WeatherKey cut in new[] { TapeCuts.Story(live, now, (byte)(seed % 5)), TapeCuts.Story(live, now, WeatherKey.None), TapeCuts.Reroll(live, now) })
                {
                    TestAssert.That(cut.HeadingDeg == live.HeadingDeg && cut.SystemSpeed == live.SystemSpeed && cut.Sets == live.Sets && cut.Seed == live.Seed && cut.Dynamic == live.Dynamic,
                        "a cut keeps the frame, the speed, the sets and the hold state");
                    TestAssert.That(cut.Epoch == now && Math.Abs(cut.OffsetAt(now) - cut.Offset) < 1e-3 && cut.Offset >= -400000f && cut.Offset <= 3000000f,
                        "the new tape is placed with the clock reference at now (seed " + seed + "): D is the offset Solve gave");
                    TapeOpening.Solve(WeatherTape.For(cut), cut, false);
                }
                WeatherKey reroll = TapeCuts.Reroll(live, now);
                TestAssert.That(reroll.TapeId != live.TapeId && reroll.LayoutSalt == (byte)(live.LayoutSalt + 1), "a re-roll is a cut: new layout salt, new tape identity (wraps at 255)");
                WeatherKey story = TapeCuts.Story(live, now, 2);
                TestAssert.That(story.Story == 2 && story.TapeId != live.TapeId && TapeCuts.Story(story, now, WeatherKey.None).Story == WeatherKey.None, "STORY sets the story and AUTO hands it back");
                WeatherKey same = TapeCuts.Story(live, now, live.Story);
                TestAssert.That(same.TapeId == live.TapeId, "choosing the story that is already chosen is not a cut");
            }
        }

        /// <summary>The recipe each scenario button prints is what the preset does (MetCatalog.ScenarioRecipe).</summary>
        private static void ScenariosAreStoryPinAndSetsPresets()
        {
            var basis = new WeatherKey(90210u, 900f, true, 4, 63, 7, true, -12000f, 18000f, story: 3, skyPin: 9);
            (WeatherScenario scenario, byte story, byte pin, byte sets)[] want =
            {
                (WeatherScenario.HurricaneEye, WeatherKey.None, 43, Superstructures.StormEyeSet),
                (WeatherScenario.SquallAssault, (byte)StoryKind.ColdFront, WeatherKey.None, (byte)(Superstructures.SquallLineSet | Superstructures.SupercellSet)),
                (WeatherScenario.MountainWave, WeatherKey.None, 7, Superstructures.LenticularSet),
                (WeatherScenario.SeaFog, WeatherKey.None, 0, Superstructures.FogBankSet),
                (WeatherScenario.FrontalPassage, (byte)StoryKind.FrontalSystem, WeatherKey.None, 0),
                (WeatherScenario.ResetAll, WeatherKey.None, WeatherKey.None, 0),
            };
            foreach (var w in want)
            {
                WeatherKey k = WeatherScenarios.Apply(w.scenario, basis, true, 0f, 0f, 0f, 1f);
                TestAssert.That(k.Story == w.story && k.SkyPin == w.pin && k.Sets == w.sets, w.scenario + " is story " + w.story + ", pin " + w.pin + ", sets " + w.sets + " (got " + k.Story + ", " + k.SkyPin + ", " + k.Sets + ")");
                TestAssert.That(k.Seed == basis.Seed && k.LayoutSalt == basis.LayoutSalt && k.HeadingDeg == basis.HeadingDeg, "a scenario keeps the layout and the frame");
                TestAssert.That(MetCatalog.ScenarioRecipe[(int)w.scenario].Length <= 21, "the recipe line fits the button: " + MetCatalog.ScenarioRecipe[(int)w.scenario]);
            }
            TestAssert.That(MetCatalog.ScenarioRecipe[0].Contains("44") && MetCatalog.ScenarioRecipe[2].Contains("8") && MetCatalog.ScenarioRecipe[3].Contains("1"), "the pin scenarios print their sky numbers");
        }
    }
}
