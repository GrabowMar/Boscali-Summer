using System.Linq;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The control registry behind the panel's ids and the sim's Press (annex 05 P9, P11).</summary>
    internal static class StrControlsTests
    {
        public static void Run()
        {
            APressRunsTheGateThenTheVerb();
            ARefusalSendsNothingAndKeepsItsWords();
            AnUnknownIdIsARefusalWithAReason();
            RegisteringAgainReplacesAndClearEmpties();
            AVerbThatDeclinesIsNotAnOk();
            AControlWithoutAGateIsAlwaysAllowed();
        }

        private static void APressRunsTheGateThenTheVerb()
        {
            var c = new StrControls();
            int gates = 0, presses = 0;
            c.Register("prop.1.pick", () => { gates++; return new StrAct(StrWhy.None, ""); }, () => { presses++; return new StrAct(StrWhy.None, "ORDER SENT", true); });
            StrControls.PressResult r = c.Press("prop.1.pick");
            TestAssert.That(r.Ok && r.Act.Sent && r.Act.Text == "ORDER SENT", "an allowed press returns the verb's answer");
            TestAssert.That(gates == 1 && presses == 1, "the gate ran once and so did the verb");
        }

        private static void ARefusalSendsNothingAndKeepsItsWords()
        {
            var c = new StrControls();
            int presses = 0;
            var refusal = new StrAct(StrWhy.Locked, "LOCKED BY RAPTOR 2-3 · 01:15");
            c.Register("prop.2.pick", () => refusal, () => { presses++; return new StrAct(StrWhy.None, "", true); });
            StrControls.PressResult r = c.Press("prop.2.pick");
            TestAssert.That(!r.Ok && r.Act.Why == StrWhy.Locked && r.Act.Text == refusal.Text, "a refused gate returns the words the button shows");
            TestAssert.That(presses == 0, "a refused press reaches no verb");
            TestAssert.That(c.Gate("prop.2.pick").Why == StrWhy.Locked, "the harness can read a reason without pressing");
        }

        private static void AnUnknownIdIsARefusalWithAReason()
        {
            var c = new StrControls();
            StrControls.PressResult r = c.Press("front.3.redirect");
            TestAssert.That(!r.Ok && r.Act.Text.Length >= 12 && r.Act.Text.Contains("FRONT.3.REDIRECT"), "an unknown id explains itself: " + r.Act.Text);
            TestAssert.That(!c.Press(null).Ok && c.Press(null).Act.Text.Length > 0 && !c.Press("").Ok, "a null or empty id is a refusal too");
            TestAssert.That(!c.Has("front.3.redirect") && !c.Has(null), "nothing is registered");
        }

        private static void RegisteringAgainReplacesAndClearEmpties()
        {
            var c = new StrControls();
            string said = "";
            c.Register("replan", null, () => { said = "first"; return new StrAct(StrWhy.None, "first"); });
            c.Register("replan", null, () => { said = "second"; return new StrAct(StrWhy.None, "second"); });
            c.Register("posture.bold", null, () => new StrAct(StrWhy.None, "bold"));
            c.Register("", null, () => new StrAct(StrWhy.None, ""));
            c.Register("tab.log", null, null);
            TestAssert.That(c.Count == 2 && c.Ids.OrderBy(s => s).SequenceEqual(new[] { "posture.bold", "replan" }), "a blank id and a null press register nothing: " + string.Join(",", c.Ids));
            c.Press("replan");
            TestAssert.That(said == "second", "the later registration wins");
            TestAssert.That(c.Collisions == 1, "a second claim on one id is counted: " + c.Collisions);
            c.Clear();
            TestAssert.That(c.Count == 0 && c.Collisions == 0 && !c.Has("replan") && !c.Press("replan").Ok, "clear leaves nothing to press and no collisions");
        }

        private static void AVerbThatDeclinesIsNotAnOk()
        {
            var c = new StrControls();
            c.Register("sortie.1.wing", () => new StrAct(StrWhy.None, ""), () => new StrAct(StrWhy.WingRefused, "THE WING DID NOT TAKE THE ORDER"));
            StrControls.PressResult r = c.Press("sortie.1.wing");
            TestAssert.That(!r.Ok && r.Act.Why == StrWhy.WingRefused, "a verb the host refused is reported, not an ok");
        }

        private static void AControlWithoutAGateIsAlwaysAllowed()
        {
            var c = new StrControls();
            c.Register("hero.locate", null, () => new StrAct(StrWhy.None, "FOCUSED"));
            TestAssert.That(c.Gate("hero.locate").Allowed && c.Press("hero.locate").Ok, "no gate means allowed");
        }
    }
}
