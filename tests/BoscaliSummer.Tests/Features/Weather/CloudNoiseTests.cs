using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class CloudNoiseTests
    {
        public static void Run()
        {
            const int size = 16;
            byte[] first = CloudNoise3D.Generate(size, 47);
            byte[] repeat = CloudNoise3D.Generate(size, 47);
            byte[] other = CloudNoise3D.Generate(size, 48);
            TestAssert.That(first.Length == size * size * size * 4, "cloud volume has one RGBA voxel per cell");
            int changedSeed = 0, variedShape = 0, variedDetail = 0, variedLobes = 0;
            for (int i = 0; i < first.Length; i += 4)
            {
                TestAssert.That(first[i] == repeat[i] && first[i + 1] == repeat[i + 1] &&
                    first[i + 2] == repeat[i + 2] && first[i + 3] == repeat[i + 3],
                    "cloud noise is deterministic for a mission seed");
                TestAssert.That(first[i + 3] == 255, "cloud noise voxels remain opaque data");
                if (first[i] != other[i]) changedSeed++;
                if (first[i] != first[0]) variedShape++;
                if (first[i + 1] != first[1]) variedDetail++;
                if (first[i + 2] != first[2]) variedLobes++;
            }
            TestAssert.That(changedSeed > 0 && variedShape > 0 && variedDetail > 0 && variedLobes > 0,
                "cloud shape, erosion and rounded Worley lobes vary across the volume and with the seed");
        }
    }
}
