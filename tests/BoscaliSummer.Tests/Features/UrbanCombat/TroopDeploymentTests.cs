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
            TestAssert.That(TroopDeploymentMath.ComputeDropSize(0, 8) == 0,
                "empty mounts cannot create troops");

            int aboard = 16;
            for (int insertion = 0; insertion < 2; insertion++)
            {
                int deployed = TroopDeploymentMath.ComputeDropSize(aboard, TroopDeploymentMath.DefaultSquadSize);
                TestAssert.That(deployed == 8, "each of two insertions uses eight troops");
                aboard -= deployed;
            }
            TestAssert.That(aboard == 0 && TroopDeploymentMath.ComputeDropSize(aboard, 8) == 0,
                "a sixteen-man load cannot perform a third insertion");

            // Tier: bigger committed force -> bigger encampment.
            TestAssert.That(TroopDeploymentMath.ComputeTier(0) == 1, "no troops still garrisons a tier-1 outpost");
            TestAssert.That(TroopDeploymentMath.ComputeTier(8) == 1, "one squad is a tier-1 outpost");
            TestAssert.That(TroopDeploymentMath.ComputeTier(16) == 2, "two committed squads reinforce to tier 2");
            TestAssert.That(TroopDeploymentMath.ComputeTier(24) == 3, "three committed squads reinforce to tier 3");
            TestAssert.That(TroopDeploymentMath.ComputeTier(32) == 4, "four committed squads cap at tier 4");
            TestAssert.That(TroopDeploymentMath.ComputeTier(40) == 4, "overflow caps at tier 4");

            // Reinforcement accumulation: sequential squads grow the encampment tier
            int committed = 0;
            committed += TroopDeploymentMath.ComputeDropSize(32, 8);
            TestAssert.That(TroopDeploymentMath.ComputeTier(committed) == 1, "first drop of 8 troops establishes tier 1");
            committed += TroopDeploymentMath.ComputeDropSize(24, 8);
            TestAssert.That(TroopDeploymentMath.ComputeTier(committed) == 2, "second drop reaches 16 troops and tier 2");
            committed += TroopDeploymentMath.ComputeDropSize(16, 8);
            TestAssert.That(TroopDeploymentMath.ComputeTier(committed) == 3, "third drop reaches 24 troops and tier 3");
            committed += TroopDeploymentMath.ComputeDropSize(8, 8);
            TestAssert.That(TroopDeploymentMath.ComputeTier(committed) == 4, "fourth drop reaches 32 troops and tier 4");

            // Descent time: height over rate, never negative, NaN or unbounded.
            float rate = TroopDeploymentMath.ParachuteDescentRate;
            TestAssert.That(Math.Abs(TroopDeploymentMath.DescentSeconds(520f, 5.2f) - 100f) < 0.01f,
                "descent time is height over descent rate");
            TestAssert.That(TroopDeploymentMath.DescentSeconds(-10f, rate) == 0f &&
                TroopDeploymentMath.DescentSeconds(float.NaN, rate) == 0f, "no height lands at once");
            TestAssert.That(TroopDeploymentMath.DescentSeconds(1e7f, rate) == TroopDeploymentMath.MaxParadropSeconds &&
                TroopDeploymentMath.DescentSeconds(500f, 0f) == TroopDeploymentMath.MaxParadropSeconds,
                "descent time stops at the hard ceiling");

            // Paradrop lifetime: the slowest jumper of a full stick lands before the drop is culled.
            float high = TroopDeploymentMath.ParadropOperationSeconds(570f, rate);
            TestAssert.That(high > 570f / rate + 16 * 1.15f,
                "a 570 m drop outlives the slowest jumper's descent and the stick's exit stagger");
            TestAssert.That(TroopDeploymentMath.ParadropOperationSeconds(1500f, rate) > high,
                "higher drops stay alive longer");
            TestAssert.That(TroopDeploymentMath.ParadropOperationSeconds(0f, rate) >= 40f &&
                TroopDeploymentMath.ParadropOperationSeconds(float.NaN, rate) >= 40f,
                "a low drop still holds its landing");
            TestAssert.That(TroopDeploymentMath.ParadropOperationSeconds(1e7f, rate) == TroopDeploymentMath.MaxParadropSeconds &&
                TroopDeploymentMath.ParadropOperationSeconds(500f, -1f) == TroopDeploymentMath.MaxParadropSeconds,
                "paradrop lifetime stops at the hard ceiling");
        }
    }
}
