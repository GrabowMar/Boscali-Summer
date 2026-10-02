using System;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Which surface under a relocation point is the pavement: of the heights a ray met, the one nearest the taxi graph's
    /// height within <c>maxOff</c> — never the first from above, which in a hangar is its roof (night-1 m3b-refit: a jet relocated
    /// to its hangar stand landed on the roof, fell and was destroyed). None near: the graph's height.</summary>
    internal static class SurfacePick
    {
        public static float Closest(float graphY, float[] hits, int count, float maxOff)
        {
            float best = graphY, off = maxOff;
            for (int i = 0; i < count && hits != null && i < hits.Length; i++)
            {
                float d = Math.Abs(hits[i] - graphY);
                if (d >= off) continue;
                off = d;
                best = hits[i];
            }
            return best;
        }
    }
}
