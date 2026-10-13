using System;
using System.Globalization;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>The single vocabulary of the war snapshot: every pilot-visible enum value has words, every empty state has a way out (spec C-04, N-19).</summary>
    internal static class TheaterWordsTests
    {
        private static readonly string[] Verbs =
            { "enable", "ask", "take", "move", "bring", "raise", "pick", "wait", "fly", "see", "set", "give", "restart" };

        public static void Run()
        {
            EveryValueHasWords();
            EmptyStatesAreTitleDetailWithAWayOut();
            ResultAndAnnounceFit();
            PostureCaptionStatesTheRule();
            DashAndBlankValues();
            ParameterisedWords();
        }

        private static void Words(string label, string text, string value)
        {
            TestAssert.That(!string.IsNullOrWhiteSpace(text), label + " " + value + " has words");
            TestAssert.That(text != value, label + " " + value + " is not the enum name");
            TestAssert.That(text == text.ToUpperInvariant() || label == "Empty" || label == "Wave" || label == "ViewState" || label == "Command",
                label + " " + value + " is upper case");
        }

        private static void EveryValueHasWords()
        {
            foreach (TheaterOpKind v in Enum.GetValues(typeof(TheaterOpKind))) Words("Kind", TheaterWords.Kind(v), v.ToString());
            foreach (TheaterPhase v in Enum.GetValues(typeof(TheaterPhase)))
                foreach (TheaterPhaseNote n in Enum.GetValues(typeof(TheaterPhaseNote)))
                    Words("Phase", TheaterWords.Phase(v, n), v + "/" + n);
            foreach (TheaterOutcome v in Enum.GetValues(typeof(TheaterOutcome)))
                if (v == TheaterOutcome.None) TestAssert.That(TheaterWords.Outcome(v) == "", "Outcome None is blank");
                else Words("Outcome", TheaterWords.Outcome(v), v.ToString());
            foreach (TheaterWhy v in Enum.GetValues(typeof(TheaterWhy)))
                if (v == TheaterWhy.None) TestAssert.That(TheaterWords.Why(v) == "", "Why None is blank");
                else Words("Why", TheaterWords.Why(v), v.ToString());
            foreach (TheaterCommandCode v in Enum.GetValues(typeof(TheaterCommandCode)))
                if (v == TheaterCommandCode.None) TestAssert.That(TheaterWords.Command(v, "", 0f) == "", "Command None is blank");
                else Words("Command", TheaterWords.Command(v, "VIPER", 30f), v.ToString());
            foreach (WaveBlock v in Enum.GetValues(typeof(WaveBlock)))
                if (v == WaveBlock.None) TestAssert.That(TheaterWords.Wave(v) == "", "Wave None is blank");
                else Words("Wave", TheaterWords.Wave(v), v.ToString());
            foreach (TheaterThreatWhy v in Enum.GetValues(typeof(TheaterThreatWhy)))
            {
                if (v == TheaterThreatWhy.None) { TestAssert.That(TheaterWords.ThreatTitle(v) == "", "ThreatTitle None is blank"); continue; }
                Words("ThreatTitle", TheaterWords.ThreatTitle(v), v.ToString());
                for (byte level = 1; level <= 5; level++)
                    TestAssert.That(TheaterWords.Threat(level, v) == "DEFCON " + level + " - " + TheaterWords.ThreatTitle(v), "Threat prints the level and the rule");
            }
            foreach (TheaterFrontStatus v in Enum.GetValues(typeof(TheaterFrontStatus))) Words("FrontStatus", TheaterWords.FrontStatus(v), v.ToString());
            foreach (TheaterWarPosture v in Enum.GetValues(typeof(TheaterWarPosture))) Words("Posture", TheaterWords.Posture(v), v.ToString());
            foreach (TheaterCarrier v in Enum.GetValues(typeof(TheaterCarrier)))
                if (v == TheaterCarrier.None) TestAssert.That(TheaterWords.Carrier(v) == "-", "Carrier None is a dash");
                else Words("Carrier", TheaterWords.Carrier(v), v.ToString());
            foreach (TheaterRisk v in Enum.GetValues(typeof(TheaterRisk)))
                if (v == TheaterRisk.Unknown) TestAssert.That(TheaterWords.Risk(v) == "-", "Risk Unknown is a dash");
                else Words("Risk", TheaterWords.Risk(v), v.ToString());
            foreach (GroundReason v in Enum.GetValues(typeof(GroundReason)))
            {
                string text = TheaterWords.Ground(v);
                if (v == GroundReason.Unknown || v == GroundReason.Pending || v == GroundReason.NotHost)
                    TestAssert.That(text == "-", "Ground " + v + " is a dash, never a confident word");
                else Words("Ground", text, v.ToString());
            }
        }

        private static void EmptyStatesAreTitleDetailWithAWayOut()
        {
            foreach (TheaterEmptyReason v in Enum.GetValues(typeof(TheaterEmptyReason)))
            {
                string text = TheaterWords.Empty(v, 12f);
                if (v == TheaterEmptyReason.None) { TestAssert.That(text == "", "Empty None is blank"); continue; }
                Words("Empty", text, v.ToString());
                int cut = text.IndexOf(" - ", StringComparison.Ordinal);
                TestAssert.That(cut > 0 && cut + 3 < text.Length, "Empty " + v + " reads TITLE - detail: " + text);
                string detail = text.ToLowerInvariant();
                bool way = false;
                foreach (string verb in Verbs) way |= detail.Contains(verb);
                TestAssert.That(way, "Empty " + v + " says what to do: " + text);
            }
            TestAssert.That(TheaterWords.Empty(TheaterEmptyReason.FirstReview, 12f).Contains("12 S"), "the first review prints its countdown");
            TestAssert.That(TheaterWords.Empty(TheaterEmptyReason.CalledOff, 12.2f).Contains("13 S"), "the veto countdown rounds up");
            foreach (TheaterViewState v in Enum.GetValues(typeof(TheaterViewState)))
            {
                string text = TheaterWords.ViewState(v, 20f);
                if (v == TheaterViewState.Live) { TestAssert.That(text == "", "a live view has no banner words"); continue; }
                Words("ViewState", text, v.ToString());
            }
            TestAssert.That(TheaterWords.ViewState(TheaterViewState.Stale, 20f).Contains("20 S OLD"), "a stale report prints its age");
            TestAssert.That(TheaterWords.ViewState(TheaterViewState.HostLacksModule, 0f) == TheaterWords.Empty(TheaterEmptyReason.HostLacksModule, 0f),
                "the view state words are the empty-reason words");
        }

        private static void ResultAndAnnounceFit()
        {
            string name = new string('N', 32), picker = new string('P', 32);
            foreach (TheaterOpKind kind in Enum.GetValues(typeof(TheaterOpKind)))
                foreach (TheaterOutcome outcome in Enum.GetValues(typeof(TheaterOutcome)))
                {
                    string line = TheaterWords.Result(kind, name, outcome, 150, picker);
                    TestAssert.That(line.Length <= 64, "a staff-log line fits the log: " + line);
                    TestAssert.That(outcome == TheaterOutcome.None || line.Length > 0, "an ended operation has a line");
                }
            TestAssert.That(TheaterWords.Result(TheaterOpKind.Assault, "PORT AURELIA", TheaterOutcome.Secured, 150, "")
                == "PORT AURELIA TAKEN - THE LINE MOVES UP +150", "a secured assault names the amount paid");
            TestAssert.That(TheaterWords.Result(TheaterOpKind.Defend, "PORT AURELIA", TheaterOutcome.Secured, 0, "")
                == "PORT AURELIA HELD - ENEMY BROKEN OFF", "no amount is printed when nothing was paid");
            TestAssert.That(TheaterWords.Result(TheaterOpKind.Assault, "PORT AURELIA", TheaterOutcome.CalledOff, 0, "viper 1-1")
                == "PORT AURELIA CALLED OFF - VIPER 1-1", "a call-off names the picker in upper case");
            TestAssert.That(TheaterWords.Announce("PORT AURELIA", TheaterOutcome.Secured, 150)
                == "OPERATION PORT AURELIA SECURED - +150 FUNDS, MORALE UP", "the radio announces a payment");
            TestAssert.That(TheaterWords.Announce("PORT AURELIA", TheaterOutcome.Secured, 0)
                == "OPERATION PORT AURELIA SECURED", "inside the cooldown the radio names no funds");
            TestAssert.That(TheaterWords.Announce("PORT AURELIA", TheaterOutcome.Repulsed, 0).Contains("STAFF REASSESSING"),
                "a lost operation says the staff is reassessing");
            TestAssert.That(TheaterWords.Announce("X", TheaterOutcome.CalledOff, 0) == "", "a call-off is not announced on the radio");
        }

        private static void PostureCaptionStatesTheRule()
        {
            foreach (TheaterWarPosture posture in Enum.GetValues(typeof(TheaterWarPosture)))
            {
                string ratio = PostureRule.Ratio(posture).ToString("0.0#", CultureInfo.InvariantCulture);
                TestAssert.That(TheaterWords.Posture(posture).Contains(" " + ratio + " "), "the posture caption prints the ratio the staff applies: " + ratio);
            }
            TestAssert.That(PostureRule.Ratio(TheaterWarPosture.Cautious) == 1.25f && PostureRule.Ratio(TheaterWarPosture.Steady) == 1f &&
                PostureRule.Ratio(TheaterWarPosture.Bold) == .75f, "the three ratios are the ones LivingWarRules uses today");
        }

        private static void DashAndBlankValues()
        {
            TestAssert.That(TheaterWords.Threat(0, TheaterThreatWhy.Nominal) == "-", "an unmeasured threat level is a dash");
            TestAssert.That(TheaterWords.Threat(3, TheaterThreatWhy.None) == "DEFCON 3", "a level without a rule still prints the level");
        }

        private static void ParameterisedWords()
        {
            TestAssert.That(TheaterWords.Command(TheaterCommandCode.Locked, "viper 1-1", 31.4f) == "LOCKED BY VIPER 1-1 FOR 32 S", "the lock names the picker and rounds up");
            TestAssert.That(TheaterWords.Command(TheaterCommandCode.Locked, "", 5f) == "LOCKED FOR 5 S", "a lock without a callsign still reads");
            TestAssert.That(TheaterWords.Phase(TheaterPhase.Awaiting, TheaterPhaseNote.NoSteering) != TheaterWords.Phase(TheaterPhase.Awaiting, TheaterPhaseNote.NoForces),
                "the two awaiting notes read differently");
            TestAssert.That(TheaterWords.Phase(TheaterPhase.Advancing, TheaterPhaseNote.NoForces) == "ADVANCING", "a note only qualifies the awaiting phase");
            TestAssert.That(TheaterWords.Aim(TheaterOpKind.Defend, false, true) != TheaterWords.Aim(TheaterOpKind.Defend, false, false), "a base defence reads differently");
            TestAssert.That(TheaterWords.Aim(TheaterOpKind.Assault, true, false) != TheaterWords.Aim(TheaterOpKind.Assault, false, false), "a mission target reads differently from a sector");
        }
    }
}
