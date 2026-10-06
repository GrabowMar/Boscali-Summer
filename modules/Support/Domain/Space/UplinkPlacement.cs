using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    internal readonly struct UplinkCandidate
    {
        public readonly int Id;
        public readonly float X, Z, RearScore;

        public UplinkCandidate(int id, float x, float z, float rearScore)
        {
            Id = id;
            X = x;
            Z = z;
            RearScore = rearScore;
        }
    }

    internal static class UplinkPlacement
    {
        public const int MaxCandidates = 128;
        public const float SeparationFraction = 0.25f;

        public static bool TryPair(IReadOnlyList<UplinkCandidate> legal, float diagonal, out int firstId, out int secondId)
        {
            firstId = secondId = -1;
            if (legal == null || legal.Count < 2 || legal.Count > MaxCandidates ||
                !float.IsFinite(diagonal) || diagonal <= 0f) return false;
            double minimum = diagonal * (double)SeparationFraction;
            double minimumSquared = minimum * minimum;
            double best = double.NegativeInfinity;
            // ponytail: at most128 legal candidates; bounded pair enumeration needs no spatial index.
            for (int i = 0; i < legal.Count; i++)
            {
                UplinkCandidate first = legal[i];
                if (!Valid(first)) continue;
                for (int j = i + 1; j < legal.Count; j++)
                {
                    UplinkCandidate second = legal[j];
                    if (!Valid(second)) continue;
                    if (first.Id == second.Id)
                    {
                        firstId = secondId = -1;
                        return false;
                    }
                    double dx = (double)first.X - second.X, dz = (double)first.Z - second.Z;
                    if (dx * dx + dz * dz < minimumSquared) continue;
                    int a = first.Id < second.Id ? first.Id : second.Id;
                    int b = first.Id < second.Id ? second.Id : first.Id;
                    double score = (double)first.RearScore + second.RearScore;
                    if (score < best || (score == best && firstId >= 0 && (a > firstId || (a == firstId && b >= secondId)))) continue;
                    best = score;
                    firstId = a;
                    secondId = b;
                }
            }
            return firstId >= 0;
        }

        private static bool Valid(in UplinkCandidate candidate) => candidate.Id >= 0 &&
            float.IsFinite(candidate.X) && float.IsFinite(candidate.Z) && float.IsFinite(candidate.RearScore);
    }
}
