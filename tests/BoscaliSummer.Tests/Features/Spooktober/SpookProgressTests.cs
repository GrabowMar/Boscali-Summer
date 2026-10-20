using BoscaliSummer.Modules.Spooktober.Domain;

namespace BoscaliSummer.Tests.Features.Spooktober
{
    /// <summary>Pins unlock, the boss chain and the save string of the Spooktober menu.</summary>
    internal static class SpookProgressTests
    {
        public static void Run()
        {
            var p = new SpookProgress();
            TestAssert.That(!p.Unlocked && !p.BossOpen(SpookBoss.Kaiju), "fresh: locked");
            foreach (SpookPuzzle q in new[] { SpookPuzzle.Echo, SpookPuzzle.Signal, SpookPuzzle.Breaker }) p.Solve(q);
            TestAssert.That(!p.Unlocked && p.SolvedCount == 3, "any order, three of four");
            TestAssert.That(p.Has(SpookPuzzle.Echo) && !p.Has(SpookPuzzle.Cipher), "has reports exactly the solved ones");
            TestAssert.That(p.Solve(SpookPuzzle.Cipher) && !p.Solve(SpookPuzzle.Cipher), "solve reports only the first time");
            TestAssert.That(p.Unlocked && p.BossOpen(SpookBoss.Kaiju) && !p.BossOpen(SpookBoss.FallenAce), "unlock opens kaiju only");
            TestAssert.That(p.RecordDefeat(SpookBoss.FallenAce) == false, "ace defeat before kaiju is not recorded");
            TestAssert.That(p.ChainLevel == 0, "ace defeat before kaiju does not skip the chain");
            TestAssert.That(p.RecordDefeat(SpookBoss.Kaiju) && p.BossOpen(SpookBoss.FallenAce), "kaiju defeat opens the ace");
            TestAssert.That(!p.RecordDefeat(SpookBoss.Kaiju) && p.ChainLevel == 1, "a second kaiju defeat changes nothing");
            p.RecordDefeat(SpookBoss.FallenAce);
            TestAssert.That(p.ChainLevel == 2 && !p.BossOpen(SpookBoss.Redacted), "redacted stays locked");
            TestAssert.That(!p.RecordDefeat(SpookBoss.Redacted) && !p.RecordDefeat(SpookBoss.FallenAce) && p.ChainLevel == 2, "nothing raises the chain past 2");
            p.HeardIntercept = true;
            string saved = p.Save();
            TestAssert.That(saved == "v1;p=15;d=2;h=1", "save format: " + saved);
            var back = SpookProgress.Load(saved);
            TestAssert.That(back.Unlocked && back.ChainLevel == 2 && back.HeardIntercept == p.HeardIntercept, "save round-trips");
            TestAssert.That(new SpookProgress().Save() == "v1;p=0;d=0;h=0", "a fresh save is all zeroes");
            var part = SpookProgress.Load("v1;p=5;d=0;h=0");
            TestAssert.That(part.Has(SpookPuzzle.Signal) && !part.Has(SpookPuzzle.Cipher) && part.Has(SpookPuzzle.Breaker) && !part.Has(SpookPuzzle.Echo) && !part.Unlocked,
                "mask bits are the puzzles in enum order");
            foreach (string bad in new[] { null, "", "v1;p=99", "garbage", "v1;p=15;d=9;h=1", "v0;p=15", "v1;p=15;d=0;h=2", "v1;p=16;d=0;h=0", "v1;p=-1;d=0;h=0", "v1;p=15;d=x;h=0", "v2;p=15;d=0;h=0" })
                TestAssert.That(!SpookProgress.Load(bad).Unlocked, "corrupt progress loads fresh: " + bad);
            TestAssert.That(SpookProgress.Load("v1;p=15;d=9;h=1").Save() == "v1;p=0;d=0;h=0", "corrupt progress loads fully fresh, not half-read");

            TestAssert.That(SpookMenu.SpawnBlocked(false) && !SpookMenu.SpawnBlocked(true), "non-host cannot spawn");
            TestAssert.That(SpookMenu.KaijuCard(false) == "KAIJU MODULE OFF", "kaiju off");
            TestAssert.That(SpookMenu.KaijuCard(true) == "KAIJU", "kaiju on");
            TestAssert.That(SpookMenu.RedactedTitle == "[REDACTED] \u00b7 31.10", "redacted teaser");
        }
    }
}
