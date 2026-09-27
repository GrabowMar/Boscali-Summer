using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.TheaterOps.Domain
{
    internal readonly struct WarFrontRead
    {
        internal readonly string Key, Label;
        internal readonly float X, Z, Pressure;
        internal readonly int Friendly, Hostile;
        internal readonly bool Objective, Held, Observed;

        internal WarFrontRead(string key, string label, float x, float z, float pressure,
            int friendly, int hostile, bool objective, bool held, bool observed)
        {
            Key = key; Label = label; X = x; Z = z; Pressure = pressure;
            Friendly = friendly; Hostile = hostile; Objective = objective;
            Held = held; Observed = observed;
        }
    }

    internal readonly struct WarOffer
    {
        internal readonly string Kind;
        internal readonly WarFrontRead Front;
        internal readonly float Score;

        internal WarOffer(string kind, WarFrontRead front, float score)
        {
            Kind = kind; Front = front; Score = score;
        }
    }

    /// <summary>Pure, bounded staff choices. A proposal needs physical forces and a map fix.</summary>
    internal static class LivingWarRules
    {
        internal const int MaximumFronts = 8;
        internal const int MaximumOffers = 3;
        internal const float OfferSeconds = 60f;
        internal const float MinimumOfferGap = 120f;
        internal const float OperationLimitSeconds = 600f;

        internal static float Score(WarFrontRead front, TheaterWarPosture posture, out string kind)
        {
            kind = null;
            if (string.IsNullOrEmpty(front.Key) || float.IsNaN(front.X) || float.IsNaN(front.Z) ||
                float.IsInfinity(front.X) || float.IsInfinity(front.Z)) return float.NegativeInfinity;
            int ours = Math.Max(0, front.Friendly);
            int theirs = Math.Max(0, front.Hostile);
            if (front.Held && theirs > 0)
            {
                kind = "DEFEND";
                return 6f + Math.Min(8, theirs) + front.Pressure * 4f + (front.Objective ? 2f : 0f);
            }
            if (ours == 0) return float.NegativeInfinity;
            if (!front.Observed && theirs == 0)
            {
                kind = "RECON";
                return 1f + (front.Objective ? 1f : 0f) + front.Pressure;
            }
            kind = "ASSAULT";
            float appetite = posture == TheaterWarPosture.Bold ? 2f
                : posture == TheaterWarPosture.Cautious ? -2f : 0f;
            return 3f + appetite + Math.Min(8, ours) - Math.Min(8, theirs) * .8f +
                front.Pressure * 2f + (front.Objective ? 2f : 0f);
        }

        internal static void Choose(IReadOnlyList<WarFrontRead> fronts, TheaterWarPosture posture,
            List<WarOffer> into)
        {
            into.Clear();
            if (fronts == null) return;
            for (int i = 0; i < fronts.Count && i < MaximumFronts; i++)
            {
                WarFrontRead front = fronts[i];
                float score = Score(front, posture, out string kind);
                if (score <= 0f || kind == null) continue;
                int insert = 0;
                while (insert < into.Count && into[insert].Score >= score) insert++;
                if (insert >= MaximumOffers) continue;
                into.Insert(insert, new WarOffer(kind, front, score));
                if (into.Count > MaximumOffers) into.RemoveAt(MaximumOffers);
            }
        }

        internal static bool Opening(float previousPressure, float currentPressure,
            bool contactChanged, bool operationFinished, float now, float nextAllowed)
        {
            return now >= nextAllowed && (operationFinished || contactChanged ||
                Math.Abs(currentPressure - previousPressure) >= .2f);
        }
    }
}
