using System;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Tests.Features.Immersion
{
    internal static class PilotPhysiologyTests
    {
        private const float Dt = 1f / 60f;

        private static PilotVitalsSample Vanilla(float g, float bp = 1f, float stamina = 1f, bool conscious = true, int threats = 0, int hits = 0) =>
            new PilotVitalsSample { G = g, HasVanilla = true, BloodPressure = bp, Stamina = stamina, Conscious = conscious, Threats = threats, Hits = hits };

        private static int Count(PilotPhysiology p, PilotVitalsSample s, float seconds, PilotCue cue)
        {
            int n = 0;
            for (float t = 0f; t < seconds; t += Dt) if ((p.Step(s, Dt) & cue) != 0) n++;
            return n;
        }

        public static void Run()
        {
            var rest = new PilotPhysiology();
            int breaths = Count(rest, Vanilla(1f), 60f, PilotCue.Inhale);
            TestAssert.That(breaths >= 11 && breaths <= 13, "Rest breathing is about 12 per minute, got " + breaths);
            TestAssert.That(rest.Exertion01 < .05f && !rest.Straining, "1 G cruise is calm.");
            TestAssert.That(Count(rest, Vanilla(1f), 30f, PilotCue.Grunt | PilotCue.GSuitInflate | PilotCue.Heartbeat) == 0,
                "Cruise emits no grunts, suit hiss or heartbeat.");

            var pull = new PilotPhysiology();
            Count(pull, Vanilla(7f), 1.5f, PilotCue.None);
            TestAssert.That(pull.Straining, "Sustained 7 G engages the straining manoeuvre.");
            int grunts = Count(pull, Vanilla(7f), 20f, PilotCue.Grunt);
            TestAssert.That(grunts >= 5 && grunts <= 7, "AGSM grunts about every 3.4 s, got " + grunts);
            TestAssert.That(!pull.Ragged, "Full stamina is not ragged.");
            Count(pull, Vanilla(7f, stamina: .2f), 1f, PilotCue.None);
            TestAssert.That(pull.Ragged && pull.Exertion01 >= .8f, "Low stamina while straining is ragged and exerting.");

            var spike = new PilotPhysiology();
            Count(spike, Vanilla(9f), .2f, PilotCue.None);
            TestAssert.That(!spike.Straining, "A 0.2 s G spike does not start straining.");

            var suit = new PilotPhysiology();
            int inflate = 0, vent = 0;
            for (int i = 0; i < 600; i++)
            {
                float g = i < 120 ? 1f + i / 40f : i < 480 ? 2.2f + .2f * (float)Math.Sin(i * .3f) : 1f;
                PilotCue c = suit.Step(Vanilla(g), Dt);
                if ((c & PilotCue.GSuitInflate) != 0) inflate++;
                if ((c & PilotCue.GSuitVent) != 0) vent++;
            }
            TestAssert.That(inflate == 1 && vent == 1, "G-suit inflates and vents once with hysteresis, got " + inflate + "/" + vent);

            var stress = new PilotPhysiology();
            stress.Step(Vanilla(1f, threats: 1), Dt);
            TestAssert.That(stress.Exertion01 >= .6f, "A known inbound missile raises exertion at once.");
            Count(stress, Vanilla(1f), 12f, PilotCue.None);
            TestAssert.That(stress.Stress01 < .25f, "Stress decays after the threat ends.");
            var hit = new PilotPhysiology();
            hit.Step(Vanilla(1f, hits: 2), Dt);
            TestAssert.That(hit.Stress01 >= .69f && hit.Stress01 <= 1f, "Hits add bounded stress.");

            var struck = new PilotPhysiology();
            PilotVitalsSample warhead = Vanilla(1f, hits: 1);
            warhead.Blast = true;
            struck.Step(warhead, Dt);
            TestAssert.That(struck.Stress01 == 1f && struck.Pulse01 == 1f, "A warhead hit makes stress critical at once and the heart audible.");
            int pounding = Count(struck, Vanilla(1f), 5f, PilotCue.Heartbeat);
            TestAssert.That(struck.Stress01 == 1f && pounding >= 12 && pounding <= 14,
                "Stress is held critical for seconds and the heart pounds at 160 bpm, got " + pounding);
            Count(struck, Vanilla(1f), 12f, PilotCue.None);
            TestAssert.That(struck.Pulse01 == 0f && Count(struck, Vanilla(1f), 5f, PilotCue.Heartbeat) == 0,
                "The heartbeat fades once the stress eases.");

            var crippled = new PilotPhysiology();
            PilotVitalsSample wrecked = Vanilla(1f);
            wrecked.Critical = true;
            int limping = Count(crippled, wrecked, 10f, PilotCue.Heartbeat);
            TestAssert.That(crippled.Pulse01 >= .7f && crippled.Stress01 >= .8f && limping >= 20,
                "A critically damaged aircraft keeps the heartbeat going, got " + limping);
            Count(crippled, Vanilla(1f), 15f, PilotCue.None);
            TestAssert.That(crippled.Pulse01 == 0f, "Once repaired, the heart settles.");

            var loc = new PilotPhysiology();
            Count(loc, Vanilla(8f, bp: .3f, stamina: 0f), 1f, PilotCue.None);
            TestAssert.That((loc.Step(Vanilla(8f, bp: .15f, stamina: 0f, conscious: false), Dt) & PilotCue.LostConsciousness) != 0,
                "Vanilla losing consciousness emits one loss cue.");
            TestAssert.That(Count(loc, Vanilla(1f, bp: .2f, stamina: 0f, conscious: false), 4f, PilotCue.Inhale | PilotCue.Exhale | PilotCue.Grunt) == 0,
                "No breathing while unconscious.");
            TestAssert.That(Count(loc, Vanilla(1f, bp: .2f, stamina: 0f, conscious: false), 4f, PilotCue.Heartbeat) > 0,
                "The heartbeat continues through blackout.");
            TestAssert.That((loc.Step(Vanilla(1f, bp: .2f, stamina: .1f), Dt) & PilotCue.RegainedConsciousness) != 0 && loc.Exertion01 > .9f,
                "Waking emits one regain cue with exertion held high.");
            Count(loc, Vanilla(1f, bp: 1f, stamina: 1f), 20f, PilotCue.None);
            TestAssert.That(loc.Exertion01 < .15f, "Post-blackout exertion decays.");

            var grey = new PilotPhysiology();
            int beats = Count(grey, Vanilla(6f, bp: .4f, stamina: .5f), 10f, PilotCue.Heartbeat);
            TestAssert.That(Math.Abs(grey.HeartRate - 135f) < .5f && beats >= 21 && beats <= 24, "Greyout heartbeat at 135 bpm, got " + beats);

            var fallback = new PilotPhysiology();
            bool lost = false, regained = false;
            for (float t = 0f; t < 40f; t += Dt)
            {
                PilotCue c = fallback.Step(new PilotVitalsSample { G = lost ? 1f : 9f, HasVanilla = false }, Dt);
                lost |= (c & PilotCue.LostConsciousness) != 0;
                regained |= (c & PilotCue.RegainedConsciousness) != 0;
            }
            TestAssert.That(lost && regained && fallback.Conscious, "Without GLOC the vanilla equations drive blackout and wake.");

            var guard = new PilotPhysiology();
            Count(guard, Vanilla(3f), 2f, PilotCue.None);
            float lift = guard.ChestLift01, load = guard.LoadG;
            foreach (float bad in new[] { 0f, -1f, .3f, float.NaN, float.PositiveInfinity })
                TestAssert.That(guard.Step(Vanilla(3f), bad) == PilotCue.None, "Pause/hitch/invalid dt emits nothing.");
            TestAssert.That(guard.Step(Vanilla(float.NaN), Dt) == PilotCue.None && guard.ChestLift01 == lift && guard.LoadG == load,
                "NaN G leaves state untouched.");

            var smooth = new PilotPhysiology();
            float prev = smooth.ChestLift01, worst = 0f;
            for (int i = 0; i < 1200; i++)
            {
                smooth.Step(Vanilla(i < 600 ? 1f : 4.5f, threats: i == 300 ? 1 : 0), Dt);
                worst = Math.Max(worst, Math.Abs(smooth.ChestLift01 - prev));
                prev = smooth.ChestLift01;
                TestAssert.That(smooth.ChestLift01 >= 0f && smooth.ChestLift01 <= 1f, "Chest lift stays in [0,1].");
            }
            TestAssert.That(worst < .12f, "Rate changes never jump the chest, worst step " + worst);

            pull.Reset();
            TestAssert.That(!pull.Straining && pull.Exertion01 == 0f && pull.Stress01 == 0f && pull.BloodPressure == 1f && pull.Conscious,
                "Reset returns a fresh pilot.");
        }
    }
}
