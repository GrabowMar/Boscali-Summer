using System;
using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class TroopDeploymentTests
    {
        public static void Run()
        {
            // Drop size: a full squad is committed each trigger, bounded by remaining ammo.
            TestAssert.That(TroopDeploymentMath.ComputeDropSize(16, 8) == 8,
                "full ammo drops a full squad");
            TestAssert.That(TroopDeploymentMath.ComputeDropSize(16, 12) == 12,
                "drop size honors a larger desired squad");
            TestAssert.That(TroopDeploymentMath.ComputeDropSize(5, 8) == 5,
                "short ammo caps the squad to what remains");
            TestAssert.That(TroopDeploymentMath.ComputeDropSize(1, 8) == 1,
                "one remaining infantry drops a single soldier");
            TestAssert.That(TroopDeploymentMath.ComputeDropSize(0, 8) == 1,
                "zero ammo never yields a negative/zero drop");

            // Tier: bigger committed force -> bigger encampment.
            TestAssert.That(TroopDeploymentMath.ComputeTier(0) == 1, "no troops still garrisons a tier-1 outpost");
            TestAssert.That(TroopDeploymentMath.ComputeTier(8) == 1, "one squad is a tier-1 outpost");
            TestAssert.That(TroopDeploymentMath.ComputeTier(16) == 2, "two committed squads reinforce to tier 2");
            TestAssert.That(TroopDeploymentMath.ComputeTier(24) == 3, "three committed squads reinforce to tier 3");
            TestAssert.That(TroopDeploymentMath.ComputeTier(32) == 4, "four committed squads cap at tier 4");
            TestAssert.That(TroopDeploymentMath.ComputeTier(40) == 4, "overflow caps at tier 4");

        }
    }
}
