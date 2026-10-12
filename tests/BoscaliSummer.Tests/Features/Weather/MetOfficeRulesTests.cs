using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The Met Office's fixed data and its enable table (brief 2.4): every control has an effect class, an arrival tag, a help sentence that fits the footer, and a reason whenever it is off.</summary>
    internal static class MetOfficeRulesTests
    {
        public static void Run()
        {
            EveryControlIsDescribed();
            EffectClassesMatchTheMockup();
            ArrivalTagsMatchTheStaging();
            HostSeatAndClockGates();
            ConditionGates();
            ModelResetKeepsItsArrays();
            ModelSanitizeKeepsTheChartFinite();
            ScenarioAndSetTablesMatchTheTape();
            HotKeysAreUnique();
            ColumnsEndTogether();
        }

        /// <summary>The reviewed defect: the three columns ended at 525, 554 and 542 of 566 units and left a ragged dead band above the command bar. Every column now fills the body to within a unit.</summary>
        private static void ColumnsEndTogether()
        {
            for (int column = 1; column <= 3; column++)
                TestAssert.That(Math.Abs(MetOfficeLayout.ColumnHeight(column) - MetOfficeLayout.BodyHeight) <= 1f && MetOfficeLayout.ColumnHeight(column) <= MetOfficeLayout.BodyHeight + 0.01f,
                    "column " + column + " is " + MetOfficeLayout.ColumnHeight(column) + " of " + MetOfficeLayout.BodyHeight + " units");
        }

        private static MetOfficeModel Ready() => new MetOfficeModel { Host = true, Captured = true, HasCamera = true, Pace = 3 };

        private static void EveryControlIsDescribed()
        {
            foreach (MetControl id in (MetControl[])Enum.GetValues(typeof(MetControl)))
            {
                MetControlInfo info = MetCatalog.Info(id);
                TestAssert.That(info.Id == id && !string.IsNullOrEmpty(info.Name), id + " has a catalog row");
                TestAssert.That(info.Help != null && info.Help.Length > 20 && info.Help.Length <= MetCatalog.HelpLimit,
                    id + " help fits one footer line (" + (info.Help == null ? 0 : info.Help.Length) + " chars)");
                TestAssert.That(MetOfficeRules.Tip(id, new MetOfficeModel()).Length <= MetCatalog.HelpLimit, id + " tip fits the footer in every state");
            }
            foreach (string part in MetCatalog.AllPartHelp())
                TestAssert.That(part.Length > 20 && part.Length <= MetCatalog.HelpLimit, "part help fits one footer line (" + part.Length + " chars): " + part);
            TestAssert.That(MetCatalog.Info(MetControl.Hold).Help.Contains("INTERIM") && MetCatalog.Info(MetControl.PaceUp).Help.Contains("INTERIM") &&
                MetCatalog.Info(MetControl.PinAuto).Help.Contains("resumes"), "hold, pace and AUTO say what main really does to the sky step and the tape");
        }

        private static void EffectClassesMatchTheMockup()
        {
            TestAssert.That(MetCatalog.Info(MetControl.Hold).Effect == MetEffect.Smooth && MetCatalog.Info(MetControl.PaceUp).Effect == MetEffect.Smooth &&
                MetCatalog.Info(MetControl.PaceDown).Effect == MetEffect.Smooth, "hold and pace are SMOOTH rebases");
            foreach (MetControl id in new[] { MetControl.PinAuto, MetControl.PinClass, MetControl.PinSelected, MetControl.Reroll, MetControl.ScenReset,
                MetControl.ScenHurricane, MetControl.StoryAuto, MetControl.Front5 })
                TestAssert.That(MetCatalog.Info(id).Effect == MetEffect.Cut, id + " is a CUT");
            for (int i = 0; i < MetCatalog.SetBits.Length; i++)
                TestAssert.That(MetCatalog.Info(MetCatalog.From(MetControl.SetSquall, i)).Effect == MetEffect.Fade, "set-piece " + i + " fades");
            TestAssert.That(MetCatalog.Info(MetControl.AnchorHere).Effect == MetEffect.Fade && MetCatalog.Info(MetControl.RainDry).Effect == MetEffect.None,
                "the anchor fades and the rain override never cuts the sky");
            TestAssert.That(MetCatalog.EffectWord(MetEffect.Cut).Contains("CUT") && MetCatalog.EffectWord(MetEffect.None) == "", "effect words");
        }

        private static void ArrivalTagsMatchTheStaging()
        {
            foreach (MetControl id in new[] { MetControl.StoryAuto, MetControl.StoryPrev, MetControl.StoryNext })
                TestAssert.That(MetCatalog.Info(id).Avail == MetAvail.Tape4B, id + " waits for the tape console (4B)");
            foreach (MetControl id in new[] { MetControl.Front2, MetControl.Front5, MetControl.Front10, MetControl.Front30 })
                TestAssert.That(MetCatalog.Info(id).Avail == MetAvail.Motion5, id + " waits for tape motion (S5)");
            foreach (MetControl id in new[] { MetControl.Hold, MetControl.PaceUp, MetControl.PinSelected, MetControl.PinAuto, MetControl.Reroll,
                MetControl.SetSupercell, MetControl.AnchorAhead, MetControl.RainHeavy, MetControl.ScenReset })
                TestAssert.That(MetCatalog.Info(id).Avail == MetAvail.Live, id + " is live now");
            TestAssert.That(MetCatalog.AvailTag(MetAvail.Tape4B) == "4B" && MetCatalog.AvailTag(MetAvail.Motion5) == "S5", "tag words");
        }

        private static void HostSeatAndClockGates()
        {
            MetOfficeModel m = Ready();
            m.HasAnchor = true;
            foreach (MetControl id in (MetControl[])Enum.GetValues(typeof(MetControl)))
                if (MetCatalog.Info(id).Avail == MetAvail.Live && id != MetControl.PaceDown && id != MetControl.PaceUp)
                    TestAssert.That(MetOfficeRules.State(id, m).Enabled, id + " is on for a ready host");
            m.Host = false;
            foreach (MetControl id in (MetControl[])Enum.GetValues(typeof(MetControl)))
            {
                MetState s = MetOfficeRules.State(id, m);
                TestAssert.That(!s.Enabled && s.Why.Length > 0, id + " is off for a client and says why");
                if (MetCatalog.Info(id).Avail == MetAvail.Live) TestAssert.That(s.Why == MetOfficeRules.Client, id + " names the host seat for a client");
            }
            m.Host = true; m.Captured = false;
            TestAssert.That(MetOfficeRules.State(MetControl.Reroll, m).Why == MetOfficeRules.Waiting, "an unready world waits for the mission environment");
            TestAssert.That(MetOfficeRules.Status(m) == MetOfficeRules.Waiting, "and the footer says so");
            m.Host = false;
            TestAssert.That(MetOfficeRules.Status(m).StartsWith("READ ONLY"), "a client footer reads READ ONLY");
            TestAssert.That(MetOfficeRules.State(MetControl.Front5, m).Why.Contains("S5"), "an unarrived control keeps its arrival tag as the reason even for a client");
        }

        private static void ConditionGates()
        {
            MetOfficeModel m = Ready();
            m.HasCamera = false;
            TestAssert.That(!MetOfficeRules.State(MetControl.AnchorHere, m).Enabled && MetOfficeRules.State(MetControl.AnchorAhead, m).Why == MetOfficeRules.NoCamera, "no camera, no placing");
            TestAssert.That(!MetOfficeRules.State(MetControl.AnchorDefault, m).Enabled, "no anchor, no DEFAULT");
            m.HasAnchor = true;
            TestAssert.That(MetOfficeRules.State(MetControl.AnchorDefault, m).Enabled, "an anchor can be cleared even without a camera");
            m.Pace = 1;
            TestAssert.That(!MetOfficeRules.State(MetControl.PaceDown, m).Enabled && MetOfficeRules.State(MetControl.PaceUp, m).Enabled, "pace 1 is the floor");
            m.Pace = 6;
            TestAssert.That(MetOfficeRules.State(MetControl.PaceDown, m).Enabled && !MetOfficeRules.State(MetControl.PaceUp, m).Enabled, "pace 6 is the ceiling");
            TestAssert.That(MetOfficeRules.Tip(MetControl.PaceUp, m).Contains("FASTEST PACE"), "a disabled live control's tip states the reason");
            TestAssert.That(MetOfficeRules.Status(Ready()).StartsWith("HOST // ALL PLAYERS"), "a ready host footer");
        }

        private static void HotKeysAreUnique()
        {
            var seen = new System.Collections.Generic.Dictionary<char, MetControl>();
            foreach (MetControl id in (MetControl[])Enum.GetValues(typeof(MetControl)))
            {
                char key = MetCatalog.Info(id).Key;
                if (key == '\0') continue;
                TestAssert.That(!seen.ContainsKey(key), id + " shares its key with " + (seen.ContainsKey(key) ? seen[key].ToString() : ""));
                seen[key] = id;
                TestAssert.That(key != 'o', id + " must not take the Ctrl+O console key");
            }
            TestAssert.That(seen.Count == 9, "nine keys: H , . S P F R W and Backspace are all that the command line offers (got " + seen.Count + ")");
            TestAssert.That(MetCatalog.KeyLabel(MetCatalog.BackspaceKey) == "BKSP" && MetCatalog.KeyLabel('h') == "H", "key labels");
        }

        private static void ModelResetKeepsItsArrays()
        {
            var m = new MetOfficeModel();
            MetFront first = m.Fronts[0];
            m.Host = true; m.FrontCount = 2; m.Fronts[0].Count = 5; m.Sets[3].Forced = true; m.Layers[1].Present = true; m.HasAnchor = true; m.ForcedRain = 0.4f; m.BlockCount = 3;
            m.Reset();
            TestAssert.That(!m.Host && m.FrontCount == 0 && !m.Sets[3].Forced && !m.Layers[1].Present && !m.HasAnchor && !m.ForcedRain.HasValue && m.BlockCount == 0, "reset clears every field");
            TestAssert.That(ReferenceEquals(first, m.Fronts[0]) && m.Pace == TapePace.DefaultPace && m.PinnedSky == -1, "reset keeps its arrays and its defaults");
            m.Sets[5] = new MetSetPiece { Forced = true, Present = true, Placed = true, FadeProgress = 0.5f };
            m.Segments[2].Present = true; m.SkyRole = "PRE-WARM"; m.Pending = "x"; m.HeldBy = MetHeldBy.Server; m.AnchorMark = 1; m.Outlook[1].Minutes = 10;
            m.Reset();
            TestAssert.That(!m.Sets[5].Placed && m.Sets[5].FadeProgress == 0f && !m.Segments[2].Present && m.SkyRole.Length == 0 && m.Pending.Length == 0 &&
                m.HeldBy == MetHeldBy.None && m.AnchorMark == -1 && m.Outlook[1].Minutes == 0, "reset clears the seam fields for the tape slices too");
        }

        private static void ModelSanitizeKeepsTheChartFinite()
        {
            float nan = float.NaN, inf = float.PositiveInfinity;
            var m = new MetOfficeModel { Cover = nan, WindMps = inf, CamX = nan, CamZ = inf, AnchorX = nan, HalfXKm = inf, ForcedRain = nan, MissionSeconds = nan };
            m.Sets[3] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = nan, Z = 5f, Strength = nan, FadeProgress = inf };
            m.Cells[2] = new MetCell { X = nan, Z = inf, Radius = nan };
            m.Layers[1] = new MetLayer { Present = true, BaseMetres = inf, Cover = nan };
            m.Outlook[4].Rain = nan;
            m.Fronts[1].X[7] = nan; m.Fronts[1].Strength = inf; m.Fronts[1].EtaMinutes = nan;
            m.Sanitize();
            TestAssert.That(m.Cover == 0f && m.WindMps == 0f && m.CamX == 0f && m.CamZ == 0f && m.AnchorX == 0f && m.HalfXKm == 0f && !m.ForcedRain.HasValue && m.MissionSeconds == 0f,
                "non-finite scalars become zero and a non-finite rain override is dropped");
            TestAssert.That(m.Sets[3].X == 0f && m.Sets[3].Z == 5f && m.Sets[3].Strength == 0f && m.Sets[3].FadeProgress == 0f && m.Cells[2].X == 0f && m.Cells[2].Z == 0f && m.Cells[2].Radius == 0f &&
                m.Layers[1].Cover == 0f && m.Layers[1].BaseMetres == 0f && m.Outlook[4].Rain == 0f && m.Fronts[1].X[7] == 0f && m.Fronts[1].Strength == 0f, "so do the arrays the chart draws");
            TestAssert.That(float.IsNaN(m.Fronts[1].EtaMinutes), "a still front keeps its NaN ETA: that is the STATIC mark, not bad data");
            var ok = new MetOfficeModel { Cover = 1.7f };
            ok.Sanitize();
            TestAssert.That(ok.Cover == 1f, "a cover above 1 is clamped");
        }

        private static void ScenarioAndSetTablesMatchTheTape()
        {
            TestAssert.That(MetCatalog.SetBits.Length == MetOfficeModel.SetCount && MetCatalog.SetNames.Length == 6 && MetCatalog.SetWhere.Length == 6, "six set-pieces");
            byte all = 0;
            foreach (byte bit in MetCatalog.SetBits) { TestAssert.That((all & bit) == 0, "set bits are distinct"); all |= bit; }
            TestAssert.That(all == Superstructures.AllSets, "the six cells cover every console bit");
            TestAssert.That(MetCatalog.Scenarios.Length == 6 && MetCatalog.ScenarioNames.Length == 6 && MetCatalog.ScenarioRecipe.Length == 6 && MetCatalog.ScenarioLater.Length == 6,
                "six scenarios with a recipe each");
            foreach (WeatherScenario s in (WeatherScenario[])Enum.GetValues(typeof(WeatherScenario)))
                TestAssert.That(Array.IndexOf(MetCatalog.Scenarios, s) >= 0, s + " has a button");
            TestAssert.That(MetCatalog.ScenarioIndex(MetControl.ScenReset) == 5 && MetCatalog.SetIndex(MetControl.SetSeaFog) == 5, "control to table index");
            TestAssert.That(MetCatalog.ClassCodes.Length == StateTable.Count && MetCatalog.ClassCodes[(int)WeatherRegimeType.RainSquall] == "RA+" &&
                RegimeSnapshot.FromType(WeatherRegimeType.Storm).Code == "TS", "class codes follow the regime table");
            TestAssert.That(MetCatalog.RainValues.Length == 4 && !MetCatalog.RainValues[0].HasValue && MetCatalog.RainValues[3] == 1f, "rain override values");
        }
    }
}
