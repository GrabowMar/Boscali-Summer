using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class InfoNetworkTests
    {
        public static void Run()
        {
            var network = new InfoNetwork();
            InfoPowers fresh = network.Powers;

            TestAssert.That(fresh.Tier == 0, "a fresh network has no tier");
            for (int i = 0; i < CyberCatalog.All.Length; i++)
                TestAssert.That(!fresh.Has(CyberCatalog.All[i]), "no hack is available before investment");

            TestAssert.That(network.CanUpgrade(FacilityId.Sigint), "SIGINT must be buildable first");
            TestAssert.That(!network.CanUpgrade(FacilityId.Crypto), "CRYPTO must require SIGINT");
            TestAssert.That(!network.CanUpgrade(FacilityId.Disrupt), "C2D must require CRYPTO");
            TestAssert.That(!network.TryUpgrade(FacilityId.Crypto), "a gated build must not apply");

            TestAssert.That(network.TryUpgrade(FacilityId.Sigint), "SIGINT LV1 must apply");
            TestAssert.That(network.Powers.Has(HackKind.Ping), "SIGINT LV1 unlocks PING");
            TestAssert.That(!network.Powers.Has(HackKind.Track), "TRACK needs SIGINT LV2");

            TestAssert.That(network.CanUpgrade(FacilityId.Crypto), "SIGINT LV1 unlocks CRYPTO");
            TestAssert.That(network.TryUpgrade(FacilityId.Sigint), "SIGINT LV2 must apply");
            TestAssert.That(network.Powers.Has(HackKind.Track), "SIGINT LV2 unlocks TRACK");

            TestAssert.That(network.TryUpgrade(FacilityId.Sigint), "SIGINT LV3 must apply");
            TestAssert.That(!network.CanUpgrade(FacilityId.Sigint), "SIGINT must cap at LV3");
            TestAssert.That(!network.TryUpgrade(FacilityId.Sigint), "overspending a maxed facility must fail");

            TestAssert.That(network.TryUpgrade(FacilityId.Crypto), "CRYPTO LV1 must apply");
            TestAssert.That(network.CanUpgrade(FacilityId.Disrupt), "CRYPTO LV1 unlocks C2D");
            TestAssert.That(network.TryUpgrade(FacilityId.Disrupt), "C2D LV1 must apply");
            TestAssert.That(network.Powers.Has(HackKind.Blackout), "C2D LV1 unlocks BLACKOUT");

            TestAssert.That(!network.Powers.Has(HackKind.Ghost), "GHOST needs EW DIVISION");
            TestAssert.That(network.CanUpgrade(FacilityId.Ew), "SIGINT LV1 unlocks EW DIVISION");
            TestAssert.That(network.TryUpgrade(FacilityId.Ew), "EW DIVISION LV1 must apply");
            TestAssert.That(network.Powers.Has(HackKind.Ghost), "EW LV1 unlocks GHOST");
            TestAssert.That(!network.Powers.Has(HackKind.Spoof), "SPOOF needs EW LV2");
            TestAssert.That(network.TryUpgrade(FacilityId.Ew), "EW DIVISION LV2 must apply");
            TestAssert.That(network.Powers.Has(HackKind.Spoof), "EW LV2 unlocks SPOOF");

            // Power scales with the relevant facility; crypto discounts cost only.
            var scaling = new InfoNetwork();
            scaling.TryUpgrade(FacilityId.Sigint);
            float baseRadius = scaling.Powers.RevealRadius;
            scaling.TryUpgrade(FacilityId.Sigint);
            TestAssert.That(scaling.Powers.RevealRadius > baseRadius, "higher SIGINT must widen sweeps");
            TestAssert.That(scaling.Powers.TrackRadius > scaling.Powers.RevealRadius,
                "track uplink must out-reach a ping sweep");

            float baseScale = scaling.Powers.CostScale;
            scaling.TryUpgrade(FacilityId.Crypto);
            TestAssert.That(scaling.Powers.CostScale < baseScale, "CRYPTO must discount operations");
            FacilityInfo crypto = InfoNetwork.Facility(FacilityId.Crypto);
            for (int i = 0; i < crypto.Levels.Length; i++)
                TestAssert.That(!crypto.Levels[i].ToLowerInvariant().Contains("cooldown"),
                    "CRYPTO copy must not claim a cooldown the host does not honour");

            TestAssert.That(InfoNetwork.Facilities.Length == 4, "four facility lines are expected");
            TestAssert.That(InfoNetwork.Facility(FacilityId.Sigint).Costs.Length == InfoNetwork.MaxLevel + 1,
                "every facility must price each level");

            // A mirrored snapshot never exceeds what the host sent.
            var mirrored = new InfoNetwork();
            mirrored.Mirror(9, 9, 9, 9);
            TestAssert.That(mirrored.Level(FacilityId.Sigint) == InfoNetwork.MaxLevel,
                "mirrors must clamp to the maximum level");

            for (int i = 0; i < CyberCatalog.All.Length; i++)
                TestAssert.That(CyberCatalog.BaseCost(CyberCatalog.All[i]) > 0f,
                    "every hack must have a price");
        }
    }
}
