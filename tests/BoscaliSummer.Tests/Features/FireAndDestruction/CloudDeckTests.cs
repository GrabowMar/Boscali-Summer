using BoscaliSummer.Fire;

namespace BoscaliSummer.Tests.Features.FireAndDestruction
{
    internal static class CloudDeckTests
    {
        public static void Run()
        {
            const float deck = 2000f;
            TestAssert.That(
                CloudDeckPolicy.IsBehindDeck(4500f, 120f, deck),
                "a ground plume seen from above the deck must draw behind it");
            TestAssert.That(
                !CloudDeckPolicy.IsBehindDeck(600f, 120f, deck),
                "a plume under the deck seen from under the deck keeps its vanilla order");
            TestAssert.That(
                !CloudDeckPolicy.IsBehindDeck(4500f, 2300f, deck),
                "a mountain-top plume above the deck seen from above is not hidden by it");
            TestAssert.That(
                CloudDeckPolicy.IsBehindDeck(600f, 2300f, deck),
                "a mountain-top plume above the deck seen from under it must draw behind it");

            // Vanilla queues measured from the game assets: water 2502, glass/lights/sun 2950,
            // cloud layer plane 2958, cloud puffs 2996-2997, smoke 2998-3001.
            TestAssert.That(
                CloudDeckPolicy.BehindDeckQueue > 2950 && CloudDeckPolicy.BehindDeckQueue < 2958,
                "behind-deck plumes must draw after water and glass but before the cloud layer plane");
        }
    }
}
