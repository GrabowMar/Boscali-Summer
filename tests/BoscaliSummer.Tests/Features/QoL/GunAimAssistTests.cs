using System;
using BoscaliSummer.Features.QoL.Runtime;

namespace BoscaliSummer.Tests.Features.QoL
{
    internal static class GunAimAssistTests
    {
        public static void Run()
        {
            float Help(float error, float angle = 1f, float input = 0f, float rate = 0f, float strength = 0.04f)
                => GunAimAssistPolicy.Correction(error, angle, input, rate, strength);
            TestAssert.That(Help(1f) > 0f && Help(-1f) == -Help(1f), "Correction must follow the signed aiming error");
            TestAssert.That(Help(0f, 0f) == 0f, "Aligned stationary aim must not drift");
            TestAssert.That(Help(1f, 2.5f) == 0f && Help(1f, 5f) == 0f,
                "No acquisition outside the narrow cone");
            TestAssert.That(Math.Abs(Help(1f, 2.499f)) < 0.000001f, "Cone edge must fade continuously");
            TestAssert.That(Help(1f, input: -0.1f) == 0f && Help(-1f, input: 0.1f) == 0f,
                "Opposing player input must immediately release the assist");
            TestAssert.That(Help(1f, input: 0.35f) == 0f && Help(1f, input: 1f) == 0f,
                "Deliberate steering must have full control");
            TestAssert.That(Help(1f, input: 0.2f) < Help(1f), "Assistance must decrease with player input");
            TestAssert.That(Help(1f, rate: 0.5f) < Help(1f) && Help(0f, 0f, rate: 0.5f) < 0f,
                "Angular damping must resist overshoot");
            TestAssert.That(Help(1f, strength: 0f) == 0f && Help(1f, strength: -1f) == 0f,
                "Zero or negative strength must disable assistance");
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                TestAssert.That(Help(bad) == 0f && Help(1f, bad) == 0f && Help(1f, input: bad) == 0f &&
                    Help(1f, rate: bad) == 0f && Help(1f, strength: bad) == 0f, "Invalid math must fail closed");
            for (int i = -250; i <= 250; i++)
            {
                float error = i * 0.01f;
                TestAssert.That(Math.Abs(Help(error, Math.Abs(error), rate: 100f)) <= 0.04f &&
                    Math.Abs(Help(error, Math.Abs(error), rate: -100f, strength: 100f)) <= 0.08f,
                    "Both configured and hard correction limits must hold");
            }
        }
    }
}
