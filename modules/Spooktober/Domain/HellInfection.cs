using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Spooktober.Domain
{
    /// <summary>A seeded ant-colony trail plan, generated once. Arrival and coverage can be baked into mesh UVs.</summary>
    internal sealed class HellInfection
    {
        internal const int Side = 65, FieldSamples = Side * Side, MaxAnts = 24, MaxSteps = 32;
        internal const float Duration = 40f;
        private const float FinalCost = 1.05f, EdgeCost = .9f;
        private static readonly int[] Dx = { 1, 1, 0, -1, -1, -1, 0, 1 }, Dz = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private readonly float[] arrivals = new float[FieldSamples], coverage = new float[FieldSamples];

        internal int TrailCells { get; private set; }
        internal int SharedCells { get; private set; }
        internal int BranchCells { get; private set; }
        internal int AntSteps { get; private set; }
        internal int QueuePops { get; private set; }
        internal int VisitedCells { get; private set; }
        internal int PeakQueue { get; private set; }

        internal readonly struct SampleValue
        {
            internal readonly float Arrival, Coverage;
            internal SampleValue(float arrival, float weight) { Arrival = arrival; Coverage = weight; }
        }

        internal HellInfection(int seed)
        {
            var random = new Random(seed);
            var trails = new float[FieldSamples];
            var pheromone = new float[FieldSamples];
            var boundary = new float[FieldSamples];
            var visits = new int[FieldSamples];
            var lastAnt = new int[FieldSamples];
            var links = new byte[FieldSamples];
            var weights = new float[8];
            float phase = (float)random.NextDouble() * (float)Math.PI * 2f;
            for (int i = 0; i < FieldSamples; i++)
            {
                trails[i] = float.PositiveInfinity;
                float x = (i % Side - Side / 2) / (float)(Side / 2), z = (i / Side - Side / 2) / (float)(Side / 2);
                float angle = (float)Math.Atan2(z, x);
                // Leave space before the mesh boundary; the seeded, lobed limit never exposes a hard circular cut.
                float limit = .785f + .045f * (float)Math.Sin(angle * 3f + phase) + .02f * (float)Math.Sin(angle * 7f - phase * .5f);
                boundary[i] = Math.Max(0f, ((float)Math.Sqrt(x * x + z * z) - limit + .14f) / .14f) * FinalCost;
            }
            int startDirection = random.Next(8);
            for (int ant = 0; ant < MaxAnts; ant++)
            {
                int x = Side / 2, z = Side / 2, direction = (startDirection + ant % 4 * 2) % 8;
                int length = ant < 4 ? random.Next(28, MaxSteps + 1) : random.Next(15, MaxSteps + 1);
                int forkAt = random.Next(6, 14);
                float time = (float)random.NextDouble() * .05f;
                Mark(z * Side + x, 0f, ant);
                for (int step = 0; step < length; step++)
                {
                    bool exploring = ant >= 4 && step >= forkAt && step < forkAt + 8;
                    if (ant >= 4 && (step == forkAt || step == forkAt + 8))
                        direction = (direction + (random.Next(2) == 0 ? 1 : 7)) % 8;
                    float sum = 0f;
                    for (int d = 0; d < 8; d++)
                    {
                        weights[d] = 0f;
                        int nx = x + Dx[d], nz = z + Dz[d];
                        if (nx < 1 || nz < 1 || nx >= Side - 1 || nz >= Side - 1) continue;
                        int next = nz * Side + nx;
                        float px = nx - Side / 2, pz = nz - Side / 2;
                        if (px * px + pz * pz > .85f * .85f * 32f * 32f || boundary[next] > .78f) continue;
                        float dot = (Dx[d] * Dx[direction] + Dz[d] * Dz[direction]) /
                            (float)Math.Sqrt((Dx[d] * Dx[d] + Dz[d] * Dz[d]) * (Dx[direction] * Dx[direction] + Dz[direction] * Dz[direction]));
                        float forward = Math.Max(0f, dot);
                        float outward = (Dx[d] * (x - Side / 2) + Dz[d] * (z - Side / 2)) /
                            Math.Max(1f, (float)Math.Sqrt(((x - Side / 2) * (x - Side / 2) + (z - Side / 2) * (z - Side / 2)) *
                                (Dx[d] * Dx[d] + Dz[d] * Dz[d])));
                        float follow = 1f + Math.Min(8f, pheromone[next]) * (exploring ? .08f : .9f);
                        float avoidLoop = lastAnt[next] == ant + 1 ? .025f : 1f;
                        weights[d] = (.2f + 2f * forward * forward + .5f + .45f * outward) * follow * avoidLoop;
                        if (dot < -.95f) weights[d] *= .02f;
                        sum += weights[d];
                    }
                    if (sum <= 0f) break;
                    float pick = (float)random.NextDouble() * sum;
                    int chosen = direction;
                    for (int d = 0; d < 8; d++)
                    {
                        if (weights[d] <= 0f) continue;
                        chosen = d;
                        pick -= weights[d];
                        if (pick <= 0f) break;
                    }
                    int previous = z * Side + x;
                    x += Dx[chosen]; z += Dz[chosen];
                    int index = z * Side + x;
                    links[previous] |= (byte)(1 << chosen);
                    links[index] |= (byte)(1 << ((chosen + 4) % 8));
                    direction = chosen;
                    time += .017f + (float)random.NextDouble() * .003f;
                    Mark(index, Math.Min(.7f, time), ant);
                    AntSteps++;
                }
                for (int i = 0; i < FieldSamples; i++) pheromone[i] *= .96f;
            }
            for (int i = 0; i < FieldSamples; i++)
            {
                if (visits[i] > 1) SharedCells++;
                int degree = 0;
                for (int bits = links[i]; bits != 0; bits &= bits - 1) degree++;
                if (degree >= 3) BranchCells++;
            }
            Spread(seed, trails, boundary);

            void Mark(int index, float time, int ant)
            {
                if (float.IsPositiveInfinity(trails[index])) TrailCells++;
                trails[index] = Math.Min(trails[index], time);
                pheromone[index] = Math.Min(12f, pheromone[index] + 1.1f);
                if (lastAnt[index] != ant + 1) visits[index]++;
                lastAnt[index] = ant + 1;
            }
        }

        private void Spread(int seed, float[] trails, float[] boundary)
        {
            var costs = new float[FieldSamples];
            var queue = new SortedSet<(float cost, int index)>();
            for (int i = 0; i < FieldSamples; i++)
            {
                costs[i] = Math.Max(trails[i], boundary[i]);
                if (costs[i] <= FinalCost) queue.Add((costs[i], i));
            }
            PeakQueue = queue.Count;
            while (queue.Count > 0)
            {
                (float cost, int index) current = queue.Min;
                queue.Remove(current);
                QueuePops++;
                int x = current.index % Side, z = current.index / Side;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + Dx[d], nz = z + Dz[d];
                    if (nx < 0 || nz < 0 || nx >= Side || nz >= Side) continue;
                    int index = nz * Side + nx;
                    float distance = Dx[d] != 0 && Dz[d] != 0 ? 1.41421356f : 1f;
                    float candidate = Math.Max(boundary[index], current.cost + distance * (.07f + .03f * CellNoise(seed, index)));
                    if (candidate > FinalCost || candidate >= costs[index]) continue;
                    if (!float.IsPositiveInfinity(costs[index])) queue.Remove((costs[index], index));
                    costs[index] = candidate;
                    queue.Add((candidate, index));
                }
                PeakQueue = Math.Max(PeakQueue, queue.Count);
            }
            for (int i = 0; i < FieldSamples; i++)
            {
                float cost = costs[i];
                if (float.IsPositiveInfinity(cost)) { arrivals[i] = 1f; continue; }
                VisitedCells++;
                arrivals[i] = Clamp01(cost / FinalCost);
                coverage[i] = 1f - Smooth(Clamp01((cost - EdgeCost) / (FinalCost - EdgeCost)));
            }
        }

        internal SampleValue Sample(float x, float z)
        {
            if (!float.IsFinite(x) || !float.IsFinite(z) || x < -1f || x > 1f || z < -1f || z > 1f) return new SampleValue(1f, 0f);
            float fx = (x + 1f) * (Side - 1) * .5f, fz = (z + 1f) * (Side - 1) * .5f;
            int ix = Math.Min(Side - 2, (int)fx), iz = Math.Min(Side - 2, (int)fz);
            float tx = fx - ix, tz = fz - iz, weight = 0f, arrival = 0f;
            Add(iz * Side + ix, (1f - tx) * (1f - tz));
            Add(iz * Side + ix + 1, tx * (1f - tz));
            Add((iz + 1) * Side + ix, (1f - tx) * tz);
            Add((iz + 1) * Side + ix + 1, tx * tz);
            return new SampleValue(weight > .00001f ? arrival / weight : 1f, weight);

            void Add(int index, float blend)
            {
                float amount = coverage[index] * blend;
                arrival += arrivals[index] * amount;
                weight += amount;
            }
        }

        internal static float Phase(float now, float born) =>
            float.IsFinite(now) && float.IsFinite(born) ? Clamp01((now - born) / Duration) : 0f;
        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
        private static float Smooth(float value) => value * value * (3f - 2f * value);
        private static float CellNoise(int seed, int index)
        {
            unchecked
            {
                uint value = (uint)(seed ^ index * 374761393);
                value = (value ^ (value >> 13)) * 1274126177u;
                return (value & 0x00ffffff) / 16777215f;
            }
        }
    }
}
