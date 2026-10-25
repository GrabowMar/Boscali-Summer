using System.Collections.Generic;
using BoscaliSummer.Modules.Spooktober.Domain;

namespace BoscaliSummer.Tests.Features.Spooktober
{
    /// <summary>Pins the four SET puzzles' rules and answers, and the transmission text that carries the first clue.</summary>
    internal static class SpookPuzzleTests
    {
        /// <summary>Fewest presses that turn <paramref name="start"/> into all-on, by BFS over the 64 breaker states.</summary>
        private static int ShortestBreaker(int start)
        {
            int goal = (1 << SpookPuzzles.BreakerCount) - 1;
            var dist = new Dictionary<int, int> { [start] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int state = queue.Dequeue();
                if (state == goal) return dist[state];
                for (int i = 0; i < SpookPuzzles.BreakerCount; i++)
                {
                    int next = SpookPuzzles.BreakerPress(state, i);
                    if (dist.ContainsKey(next)) continue;
                    dist[next] = dist[state] + 1;
                    queue.Enqueue(next);
                }
            }
            return -1;
        }

        /// <summary>Every set of banks (a press is its own undo, so order is irrelevant) that solves <paramref name="start"/>.</summary>
        private static List<int> SolvingPressSets(int start)
        {
            var sets = new List<int>();
            for (int presses = 0; presses < 1 << SpookPuzzles.BreakerCount; presses++)
            {
                int state = start;
                for (int i = 0; i < SpookPuzzles.BreakerCount; i++)
                    if ((presses & (1 << i)) != 0) state = SpookPuzzles.BreakerPress(state, i);
                if (SpookPuzzles.BreakerSolved(state)) sets.Add(presses);
            }
            return sets;
        }

        public static void Run()
        {
            TestAssert.That(SpookPuzzles.SignalSolved(1310, 2.1f) && !SpookPuzzles.SignalSolved(1310, 1.5f) && !SpookPuzzles.SignalSolved(1311, 9f), "signal: 131.0 held 2 s");
            TestAssert.That(SpookPuzzles.SignalTenths >= SpookPuzzles.SignalMin && SpookPuzzles.SignalTenths <= SpookPuzzles.SignalMax, "signal: the answer is inside the stepper's range");
            TestAssert.That(SpookPuzzles.CipherLabel == "OVSSVD", "cipher label is ROT7 of HOLLOW");
            TestAssert.That(SpookPuzzles.Rot(SpookPuzzles.CipherLabel, 26 - SpookPuzzles.CipherShift) == "HOLLOW", "rot round-trips");
            TestAssert.That(SpookPuzzles.Rot("ab-C 9", 1) == "ab-D 9", "rot moves A-Z only and keeps everything else");
            TestAssert.That(SpookPuzzles.Rot("A", -1) == "Z", "rot wraps backwards");
            TestAssert.That(SpookPuzzles.CipherSolved("  hollow ") && !SpookPuzzles.CipherSolved("HOLOW"), "cipher answer: trim+case only");
            TestAssert.That(!SpookPuzzles.CipherSolved(null) && !SpookPuzzles.CipherSolved("OVSSVD"), "cipher: no answer, and the label itself, do not solve it");
            TestAssert.That(!SpookPuzzles.BreakerSolved(SpookPuzzles.BreakerStart), "breaker starts unsolved");
            TestAssert.That(SpookPuzzles.BreakerPress(0, 0) == 0b000011 && SpookPuzzles.BreakerPress(0, 5) == 0b110000 && SpookPuzzles.BreakerPress(0, 2) == 0b001110,
                "breaker: a press flips itself and its neighbours, and stops at the ends");
            TestAssert.That(SpookPuzzles.BreakerPress(SpookPuzzles.BreakerPress(5, 3), 3) == 5, "breaker: pressing twice undoes it");
            TestAssert.That(SpookPuzzles.BreakerPress(5, -1) == 5 && SpookPuzzles.BreakerPress(5, 6) == 5, "breaker: a press off the panel does nothing");
            TestAssert.That(ShortestBreaker(SpookPuzzles.BreakerStart) == 3, "breaker: 3 presses (0, 2, 5), no shorter path"); // BFS over 64 states in the test
            List<int> solutions = SolvingPressSets(SpookPuzzles.BreakerStart);
            TestAssert.That(solutions.Count == 1 && solutions[0] == 0b100101, "breaker: banks 0, 2 and 5 are the only way");
            TestAssert.That(SpookPuzzles.Morse("GHOST") == "--. .... --- ... -", "morse");
            TestAssert.That(SpookPuzzles.Morse("sos") == "... --- ...", "morse: lower case reads the same");
            // G H O S T, in Morse units: verify every lit/off interval, including the seven-unit repeat gap.
            var pulses = new[] { (0, 3), (4, 7), (8, 9), (12, 13), (14, 15), (16, 17), (18, 19),
                (22, 25), (26, 29), (30, 33), (36, 37), (38, 39), (40, 41), (44, 47) };
            for (int unit = 0; unit < 108; unit++)
            {
                bool lit = false;
                foreach (var pulse in pulses) lit |= unit % 54 >= pulse.Item1 && unit % 54 < pulse.Item2;
                TestAssert.That(SpookPuzzles.EchoLampLit((unit + 0.5f) * SpookPuzzles.EchoUnitSeconds) == lit,
                    "echo lamp: standard pulse/gap timing at unit " + unit);
            }
            TestAssert.That(!SpookPuzzles.EchoLampLit(-0.1f) && !SpookPuzzles.EchoLampLit(float.NaN) &&
                !SpookPuzzles.EchoLampLit(float.PositiveInfinity), "echo lamp: no light before start or for invalid time");
            TestAssert.That(SpookPuzzles.EchoSolved("ghost\n") && !SpookPuzzles.EchoSolved("GHOSTS"), "echo answer");
            TestAssert.That(!SpookPuzzles.EchoSolved("") && !SpookPuzzles.EchoSolved(null) && !SpookPuzzles.EchoSolved("G H O S T"), "echo: blanks and spaced-out letters do not solve it");

            TestAssert.That(SpookText.Transmission.Contains("zero") && SpookText.Transmission.Contains("never, ever"), "transmission carries the signal clue");
            TestAssert.That(SpookText.Transmission.Contains("zero... one... three... one..."), "transmission reads 131.0 backwards");
            TestAssert.That(SpookText.Transmission.Contains("never, ever solve the puzzles in SET"), "transmission names the SET panel");
            TestAssert.That(SpookText.Glitch("131.0 never", 4, 1f).StartsWith("131.0 "), "glitch keeps digits and spaces");
            TestAssert.That(SpookText.Glitch("abc", 4, .5f) == SpookText.Glitch("abc", 4, .5f), "glitch is deterministic");
            TestAssert.That(SpookText.Glitch("abc", 4, 1f) != "abc" && SpookText.Glitch("abc", 4, 1f).Length == 3, "full glitch changes every letter and keeps the length");
            TestAssert.That(SpookText.Glitch("never ever", 4, 0f) == "never ever", "no glitch leaves the text alone");
            TestAssert.That(SpookText.Glitch(null, 4, 1f) == "", "glitch of nothing is nothing");
        }
    }
}
