using System;
using BoscaliSummer.Modules.Spooktober.Domain;

namespace BoscaliSummer.Tests.Features.Spooktober
{
    /// <summary>Pins the fallen ace's lives, rebirths, blink gate, phantom veer, repair and mood machine.</summary>
    internal static class AceRulesTests
    {
        private const string Head = "FALLEN ACE \u00b7 ";
        private const string Full = "\u2588";
        private const string Empty = "\u2591";

        private static AceSense Sense(float now, bool target = true, float dist = 5000f, bool missile = false) =>
            new AceSense { Now = now, HasTarget = target, TargetDistance = dist, MissileInbound = missile };

        public static void Run()
        {
            // lives and rebirth
            var a = new AceRules(AceTuning.Default);
            TestAssert.That(a.Lives == 3 && a.LivesLine() == Head + Full + Full + Full, "fresh lives line: " + a.LivesLine());
            TestAssert.That(a.AbsorbLethal() && a.AbsorbLethal(), "first two lethal hits are rebirths");
            TestAssert.That(a.Lives == 1 && a.LivesLine() == Head + Full + Empty + Empty, "lives line after two absorbs: " + a.LivesLine());
            TestAssert.That(!a.AbsorbLethal() && a.Lives == 1, "the third lethal hit is the real death and keeps the last life");
            TestAssert.That(new AceRules(AceTuning.Default).Lives == 3, "despawn is not defeat: a new rules object has all lives, only AbsorbLethal()==false is defeat");
            var oneLife = AceTuning.Default;
            oneLife.Lives = 1;
            TestAssert.That(!new AceRules(oneLife).AbsorbLethal(), "a one-life ace dies on the first lethal hit");
            var noLives = AceTuning.Default;
            noLives.Lives = 0;
            var clamped = new AceRules(noLives);
            TestAssert.That(clamped.Lives == 1 && clamped.LivesLine() == Head + Full, "a zero-life config is clamped to one life: " + clamped.LivesLine());

            // phantom veer
            TestAssert.That(AceRules.PhantomVeers(3500f, 3000f) && !AceRules.PhantomVeers(2500f, 3000f) && !AceRules.PhantomVeers(3000f, 3000f), "phantom veers only past its range");

            // blink gate
            var b = new AceRules(AceTuning.Default);
            TestAssert.That(!b.WantsBlink(new AceSense { Now = 100f, HullLostInWindow = 0.1f }), "a scratch does not blink");
            TestAssert.That(b.WantsBlink(new AceSense { Now = 100f, HullLostInWindow = 0.2f }), "0.2 hull lost in the window blinks (and the scratch above did not eat the cooldown)");
            TestAssert.That(!b.WantsBlink(new AceSense { Now = 105f, HullLostInWindow = 0.2f }), "cooldown: no second blink 5 s later");
            TestAssert.That(b.WantsBlink(new AceSense { Now = 121f, HullLostInWindow = 0.2f }), "cooldown over 21 s later");
            TestAssert.That(!b.WantsBlink(new AceSense { Now = 125f, HullLostInWindow = 0.2f }), "the blink at 121 restarted the cooldown");

            // repair
            var rep = new AceRules(AceTuning.Default);
            TestAssert.That(rep.RepairNow(new AceSense { SinceHit = 2f }, 0.5f) == 0f, "no repair right after a hit");
            TestAssert.That(Math.Abs(rep.RepairNow(new AceSense { SinceHit = 7f }, 0.5f) - 0.01f * 0.5f) < 1e-6f, "repair is 0.01 per second once the delay passed");

            // mood machine
            var m = new AceRules(AceTuning.Default);
            TestAssert.That(m.Mood == AceMood.Hunt, "starts hunting");
            TestAssert.That(m.Tick(Sense(0f, target: false), 0.5f) == AceMood.Hunt, "no target: hunt");
            TestAssert.That(m.Tick(Sense(1f, missile: true), 0.5f) == AceMood.Feint && m.Mood == AceMood.Feint && m.MoodSince == 1f, "a missile inbound feints at once");
            TestAssert.That(m.Tick(Sense(3.9f, missile: true), 0.5f) == AceMood.Feint, "feint lasts 3 s");
            TestAssert.That(m.Tick(Sense(4.1f), 0.5f) == AceMood.Hunt && m.MoodSince == 4.1f, "feint over after 3 s: back to hunt");

            var hunt = new AceRules(AceTuning.Default);
            TestAssert.That(hunt.Tick(Sense(0f, dist: 2000f), 1f) == AceMood.Hunt, "hunt begins with the longest timer at roll 1");
            TestAssert.That(hunt.Tick(Sense(29f, dist: 2000f), 0.5f) == AceMood.Hunt, "hunt holds before its timer");
            TestAssert.That(hunt.Tick(Sense(31f, dist: 2000f), 0.5f) == AceMood.Strike, "close hunt after 31 s becomes a strike");
            TestAssert.That(hunt.Tick(Sense(32f, dist: 2000f), 0.5f) == AceMood.Strike, "strike holds before its timer");
            TestAssert.That(hunt.Tick(Sense(33f, target: false), 0.5f) == AceMood.Hunt, "losing the target drops any mood to hunt");

            var far = new AceRules(AceTuning.Default);
            far.Tick(Sense(0f, dist: 3000f), 1f);
            TestAssert.That(far.Tick(Sense(31f, dist: 3000f), 0.4f) == AceMood.Hunt && far.MoodSince == 31f, "far hunt on a low roll stays hunt and restarts its timer");
            TestAssert.That(far.Tick(Sense(62f, dist: 3000f), 0.9f) == AceMood.Stalk, "far hunt on a high roll stalks");

            var stalk = new AceRules(AceTuning.Default);
            stalk.Tick(Sense(0f, dist: 3000f), 1f);
            stalk.Tick(Sense(31f, dist: 3000f), 1f);
            TestAssert.That(stalk.Mood == AceMood.Stalk, "stalk entered at 31 s");
            TestAssert.That(stalk.Tick(Sense(42.9f, dist: 3000f), 0.5f) == AceMood.Stalk, "stalk (roll 1) still runs at 11.9 s");
            TestAssert.That(stalk.Tick(Sense(43.1f, dist: 3000f), 0.5f) == AceMood.Hunt, "stalk ends by 12 s at most");

            var quick = new AceRules(AceTuning.Default);
            quick.Tick(Sense(0f, dist: 3000f), 0f);
            TestAssert.That(quick.Tick(Sense(10f, dist: 3000f), 0.6f) == AceMood.Stalk, "roll 0 hunt times out after 10 s");
            TestAssert.That(quick.Tick(Sense(20.3f, dist: 3000f), 0.5f) == AceMood.Stalk, "stalk entered on roll 0.6 runs 10.4 s (8 + 0.6 * 4)");
            TestAssert.That(quick.Tick(Sense(20.5f, dist: 3000f), 0.5f) == AceMood.Hunt, "and ends right after");

            var strike = new AceRules(AceTuning.Default);
            strike.Tick(Sense(0f, dist: 2000f), 0f);
            strike.Tick(Sense(10f, dist: 2000f), 0.5f);
            TestAssert.That(strike.Mood == AceMood.Strike, "strike entered");
            TestAssert.That(strike.Tick(Sense(30.5f, dist: 2000f), 0.7f) == AceMood.Stalk, "strike on a high roll stalks");
            var strike2 = new AceRules(AceTuning.Default);
            strike2.Tick(Sense(0f, dist: 2000f), 0f);
            strike2.Tick(Sense(10f, dist: 2000f), 0.5f);
            TestAssert.That(strike2.Tick(Sense(30.5f, dist: 2000f), 0.6f) == AceMood.Hunt,"strike on roll 0.6 or less goes back to hunt");
        }
    }
}
