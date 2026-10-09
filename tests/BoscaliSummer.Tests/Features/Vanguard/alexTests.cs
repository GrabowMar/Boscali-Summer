using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins the ALE-X KITE rules: reel, deploy from the pod, layers, the cut, HUD words and animation maths.</summary>
    internal static class AlexTests
    {
        public static void Run()
        {
            Line();
            Deploy();
            Layers();
            Cut();
            Words();
            Animation();
        }

        private static void Line()
        {
            TestAssert.Eq(TowedTrail.LineFor(TowedTrail.NoThreat), 60f, "no radar missile on the host: 60 m idle line");
            TestAssert.Eq(TowedTrail.LineFor(12000f), 60f, "a radar missile beyond 10 km does not move the reel");
            TestAssert.Eq(TowedTrail.LineFor(10000f), 150f, "inside 10 km: 150 m");
            TestAssert.Eq(TowedTrail.LineFor(4001f), 150f, "just outside 4 km: still 150 m");
            TestAssert.Eq(TowedTrail.LineFor(4000f), 250f, "inside 4 km: 250 m");
            TestAssert.Eq(TowedTrail.LineFor(0f), 250f, "a missile on the host: 250 m");
            TestAssert.Near(TowedTrail.Reel(60f, 250f, 1f), 100f, "reel pays out 40 m/s");
            TestAssert.Near(TowedTrail.Reel(250f, 60f, 0.5f), 230f, "reel takes in 40 m/s");
            TestAssert.Near(TowedTrail.Reel(60f, 250f, 10f), 250f, "reel stops at the wanted length");
            TestAssert.Near(TowedTrail.Drop(100f), 8f, "a 100 m fiber hangs the decoy 8 m down");
            TestAssert.That(TowedTrail.Snaps(7.5f, 500f) && TowedTrail.Snaps(2f, 40f) && !TowedTrail.Snaps(4f, 500f),
                "fiber snaps above 7 g or below 50 m");
        }

        // The deploy fix: the decoy starts AT the pod exit with no fiber out and pays line at 40 m/s,
        // instead of appearing 100 m astern (and so at the exhaust) on the first physics ticks.
        private static void Deploy()
        {
            Vector3 fwd = Vector3.forward, up = Vector3.up;
            Vector3 exit = new Vector3(0.4f, -1.1f, -1.5f);
            TestAssert.That((TowedTrail.TrailPoint(exit, fwd, up, 0f) - exit).magnitude < 1e-6f, "no fiber out: the decoy sits at the pod exit point");
            TestAssert.That((TowedTrail.TrailPoint(exit, fwd, up, 60f) - new Vector3(0.4f, -5.9f, -61.5f)).magnitude < 1e-3f,
                "60 m of fiber: 60 m astern of the exit point, 4.8 m below");

            const float dt = 0.02f;
            float line = 0f, wanted = TowedTrail.LineFor(TowedTrail.NoThreat);
            Vector3 rel = Vector3.zero, host = Vector3.zero;
            float at05 = -1f, at15 = -1f, ticksTo60 = -1f;
            for (int tick = 1; tick <= 400; tick++)
            {
                if (tick == 200) wanted = TowedTrail.LineFor(9000f); // a radar missile appears at 9 km at t = 4 s
                line = TowedTrail.Reel(line, wanted, dt);
                if (ticksTo60 < 0f && line >= TowedTrail.IdleLine) ticksTo60 = tick;
                rel = TowedTrail.Follow(rel, TowedTrail.Offset(fwd, up, line), dt);
                host += fwd * 200f * dt; // the exit point moves with the host at 200 m/s
                if (tick == 25) at05 = rel.magnitude;
                if (tick == 75) at15 = rel.magnitude;
            }
            TestAssert.That(at05 > 5f && at05 < 21f, "half a second after release the decoy is still near the pod (" + at05 + " m)");
            TestAssert.That(at15 > 50f && at15 < 62f, "1.5 s after release the idle line is out (" + at15 + " m)");
            TestAssert.That(ticksTo60 >= 75f && ticksTo60 <= 76f, "60 m of idle line takes 1.5 s (75 ticks) at 40 m/s, got " + ticksTo60);
            TestAssert.Near(line, 150f, "a radar missile inside 10 km reels the line out to 150 m");
            TestAssert.That(Mathf.Abs(rel.magnitude - Mathf.Sqrt(150f * 150f + 12f * 12f)) < 0.5f,
                "host speed adds no steady lag: the decoy is exactly line + drop behind the exit point");
            TestAssert.That(host.z > 1500f, "the simulated host really moved");
            Vector3 swing = TowedTrail.Follow(Vector3.zero, new Vector3(0f, 0f, -100f), TowedTrail.Lag);
            TestAssert.Near(swing.z, -63.21f, "one lag constant closes 63 % of the gap");
        }

        private static void Layers()
        {
            TestAssert.That(AlexKite.Deflects("ARH", 5999f) && AlexKite.Deflects("SARH", 6000f) && !AlexKite.Deflects("ARH", 6001f) &&
                !AlexKite.Deflects("IR", 1000f), "DEFLECT: radar missiles inside 6 km only");
            TestAssert.That((AlexKite.DeflectShift(new Vector3(0f, 0f, -250f)) - new Vector3(0f, 0f, -100f)).magnitude < 1e-3f,
                "DEFLECT pulls the aimpoint 40 % of the way to a decoy 250 m astern: 100 m off the host");
            TestAssert.That(Mathf.Abs(AlexKite.SuppressRefund(0.02f) - 0.02f / 3f) < 1e-6f, "SUPPRESS gives back a third of every tick");
            int plain = 0, suppressed = 0;
            float held = 0f;
            while (held <= 0.5f) { held += 0.02f; plain++; }
            held = 0f;
            while (held <= 0.5f) { held += 0.02f - AlexKite.SuppressRefund(0.02f); suppressed++; }
            TestAssert.That(plain >= 25 && plain <= 26 && suppressed / (float)plain > 1.4f && suppressed / (float)plain < 1.6f,
                "a 0.5 s turret lock takes 50 % longer under SUPPRESS (" + plain + " vs " + suppressed + " ticks)");
            TestAssert.That(SeductionRule.SeducesTowed("ARH", 2999f, -0.9f, 0.3f) && !SeductionRule.SeducesTowed("ARH", 3001f, -0.9f, 0.01f),
                "SEDUCE: inside 3 km, rear shots far likelier");
            TestAssert.Eq(AlexKite.PipMask(true, false, true), (byte)5, "pip mask SUP + SED");
            TestAssert.Eq(AlexKite.PipMask(true, true, true), (byte)7, "all three pips");
        }

        private static void Cut()
        {
            TestAssert.Near(AlexKite.FreeSpeedFactor(0f), 1f, "a cut decoy leaves at the host's speed");
            TestAssert.Near(AlexKite.FreeSpeedFactor(10f), 0.65f, "halfway it has slowed to 65 %");
            TestAssert.Near(AlexKite.FreeSpeedFactor(20f), 0.3f, "after 20 s it is at 30 %");
            TestAssert.Near(AlexKite.FreeSpeedFactor(60f), 0.3f, "it never slows below the floor");
            TestAssert.That(!AlexKite.AiCuts(0, 99f) && !AlexKite.AiCuts(1, 0.9f) && AlexKite.AiCuts(1, 1f) && AlexKite.AiCuts(3, 9f),
                "an AI host cuts 1 s after the kite first holds a missile");
            TestAssert.That(AlexKite.AiDeploys(10000f) && !AlexKite.AiDeploys(10001f) && AlexKite.AiDeploys(0f) && !AlexKite.AiDeploys(TowedTrail.NoThreat),
                "an AI host with a pod deploys for a radar missile inside 10 km");
            // Two seconds after the cut the dragged missile follows a decoy that is 100+ m behind and slowing.
            Vector3 hostVelocity = new Vector3(0f, 0f, 220f);
            float separation = 150f;
            for (float t = 0f; t < 4f; t += 0.02f)
                separation += (hostVelocity.z - hostVelocity.z * AlexKite.FreeSpeedFactor(t)) * 0.02f;
            TestAssert.That(separation > 180f, "four seconds after the cut the host has opened the gap past " + separation + " m");
            TestAssert.Eq((byte)KiteState.Deploy, (byte)0, "KiteState wire value");
            TestAssert.Eq((byte)KiteState.Free, (byte)2, "KiteState wire value");
            TestAssert.Eq((byte)KiteState.Gone, (byte)3, "KiteState wire value");
            TestAssert.Eq((byte)KiteCut.Pilot, (byte)1, "KiteCut wire value");
            TestAssert.Eq((byte)KiteCut.Auto, (byte)5, "KiteCut wire value");
        }

        private static void Words()
        {
            TestAssert.Eq(AlexKite.PipText(7), "SUP DEF SED", "all layers lit");
            TestAssert.Eq(AlexKite.PipText(2), "--- DEF ---", "only DEFLECT lit");
            TestAssert.Eq(AlexKite.PipText(0), "--- --- ---", "quiet");
            TestAssert.Eq(AlexKite.StatusText(6), "KITE  --- DEF SED", "held line text");
            TestAssert.Eq(AlexKite.LineDetail(150f, 0, true), "LINE 150 m", "line detail");
            TestAssert.Eq(AlexKite.LineDetail(249.6f, 2, true), "LINE 250 m  ·  2 ON KITE  ·  FIRE TO CUT", "line detail with captures");
            TestAssert.Eq(AlexKite.LineDetail(60f, 1, false), "LINE 60 m  ·  1 ON KITE", "AI sees no cut cue");
            TestAssert.Eq(AlexKite.Severity(0, 0), 0, "quiet is info");
            TestAssert.Eq(AlexKite.Severity(AlexKite.PipDef, 0), 1, "a layer working is caution");
            TestAssert.Eq(AlexKite.Severity(AlexKite.PipSed, 0), 2, "a missile in seduce range and not yet on the kite is warning");
            TestAssert.Eq(AlexKite.Severity(AlexKite.PipSed, 1), 1, "once it is on the kite it is caution again");
            TestAssert.Eq(AlexKite.CutText(KiteCut.Pilot, 2), "KITE CUT — 2 MISSILES DRAGGED", "cut with two dragged");
            TestAssert.Eq(AlexKite.CutText(KiteCut.Pilot, 1), "KITE CUT — 1 MISSILE DRAGGED", "cut with one dragged");
            TestAssert.Eq(AlexKite.CutText(KiteCut.Auto, 0), "KITE CUT", "cut with none");
            TestAssert.Eq(AlexKite.CutText(KiteCut.Snap, 0), "KITE SNAPPED — OVERLOAD", "snap");
            TestAssert.Eq(AlexKite.CutText(KiteCut.Low, 3), "KITE SNAPPED — TOO LOW — 3 MISSILES DRAGGED", "snap low with three dragged");
        }

        private static void Animation()
        {
            TestAssert.Near(AlexKite.FinPop(0f), 0f, "fins are stowed at release");
            TestAssert.Near(AlexKite.FinPop(0.2f), 0.5f, "fins are half out mid-pop");
            TestAssert.Near(AlexKite.FinPop(1f), 1f, "fins are out after 0.35 s");
            TestAssert.Eq(AlexKite.ReelSpin(0.5f), 0f, "a still fiber does not spin the drum");
            TestAssert.Near(AlexKite.ReelSpin(40f), 480f, "40 m/s of payout is 480 deg/s");
            TestAssert.Near(AlexKite.ReelSpin(-40f), -480f, "reeling in spins it back");
            TestAssert.Near(AlexKite.ReelSpin(500f), 720f, "spin is capped at 1.5 x the reel speed");
            TestAssert.Near(AlexKite.Strobe(0.05f, false), 1f, "strobe flashes at the start of each second");
            TestAssert.Near(AlexKite.Strobe(0.5f, false), 0.06f, "and idles dim between flashes");
            TestAssert.Near(AlexKite.Strobe(0.38f, true), 1f, "a cut decoy flashes every 0.35 s");
            TestAssert.Near(AlexKite.Strobe(0.38f, false), 0.06f, "... which the towed rate does not");

            Vector3 turned = AlexKite.Rotate(Vector3.forward, Vector3.up, 90f);
            TestAssert.That((turned - Vector3.right).magnitude < 1e-5f, "positive turn about up takes forward to right (Unity AngleAxis)");
            Vector3 fwd = Vector3.forward;
            // A stowed fin lies up the side; popping port (-X) from a +Y centroid needs +, starboard needs -.
            TestAssert.Eq(AlexKite.PopSign(Vector3.up * 0.1f, fwd, Vector3.left), 1f, "FinL pops one way from a fin lying up the body");
            TestAssert.Eq(AlexKite.PopSign(Vector3.up * 0.1f, fwd, Vector3.right), -1f, "FinR pops the other way");
            TestAssert.Eq(AlexKite.PopSign(Vector3.left * 0.1f, fwd, Vector3.up), -1f, "a fin lying along port pops up with the opposite sign");
            TestAssert.Eq(AlexKite.PopSign(Vector3.zero, fwd, Vector3.up), 1f, "a fin with no extent defaults to +");
        }
    }
}
