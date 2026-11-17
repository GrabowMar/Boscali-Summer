using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class CloudClearanceTests
    {
        public static void Main()
        {
            int assertions = 0;
            Run((ok, message) => { if (!ok) throw new InvalidOperationException(message); assertions++; });
            Sa74FlashTests.Run((ok, message) => { if (!ok) throw new InvalidOperationException(message); assertions++; });
            Sa74BlastShapeTests.Run((ok, message) => { if (!ok) throw new InvalidOperationException(message); assertions++; });
            Console.WriteLine("PASS: cloud clearance and flash evolution, " + assertions + " assertions.");
        }

        public static void Run(Action<bool, string> check)
        {
            var blast = new CloudClearanceSphere(1000f, 200f, -3000f, 5000f, 1f, 500f);
            check(blast.Keep(1000f, 200f, -3000f) == 0f, "Ground zero must clear.");
            check(blast.Keep(1000f, 4200f, -3000f) == 0f, "Clouds inside the vertical shock sphere must clear.");
            check(blast.Keep(1000f, 6200f, -3000f) == 1f, "The shock must reach a high layer before clearing it.");
            check(Math.Abs(blast.Keep(6000f, 200f, -3000f) - .5f) < .0001f, "The advancing edge must fade smoothly.");
            var recovering = new CloudClearanceSphere(1000f, 200f, -3000f, 5000f, .4f, 500f);
            check(Math.Abs(recovering.Keep(1000f, 200f, -3000f) - .6f) < .0001f, "Cloud density must recover as displacement fades.");
            CloudClearance.Publish(new[] { recovering }, 1);
            CloudClearanceSphere[] snapshot = CloudClearance.Current;
            CloudClearance.Publish(Array.Empty<CloudClearanceSphere>(), 2);
            check(CloudClearance.Keep(snapshot, 1000f, 200f, -3000f) < 1f, "A worker keeps its immutable snapshot.");
            check(CloudClearance.Keep(CloudClearance.Current, 1000f, 200f, -3000f) == 1f, "A mission reset restores undisturbed cloud density.");
            check(CloudClearance.Revision == 2, "Shadow invalidation follows displacement revisions.");
        }
    }
}
