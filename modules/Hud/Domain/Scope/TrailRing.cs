using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// A fixed ring of samples (<c>width</c> floats each) that keeps the newest <c>capacity</c> and hands them back oldest first: a missile's
    /// trail. Samples are taken at most every <c>step</c> seconds however often <see cref="Add"/> is called, so a 10 Hz reader and a 60 Hz
    /// reader leave the same trail. Pure; allocates once.
    /// </summary>
    internal sealed class TrailRing
    {
        private readonly float[] data;
        private readonly int width, capacity;
        private readonly float step;
        private int head, count;
        private float last = float.NegativeInfinity;

        public TrailRing(int capacity, int width, float step)
        {
            this.capacity = capacity;
            this.width = width;
            this.step = step;
            data = new float[capacity * width];
        }

        public int Count => count;

        /// <summary>Take a sample if <paramref name="step"/> has passed since the last one (the first is always taken).</summary>
        public bool Add(float now, float a, float b = 0f, float c = 0f)
        {
            if (now - last < step) return false;
            last = now;
            int at = (head + count) % capacity;
            if (count == capacity) head = (head + 1) % capacity; else count++;
            int o = at * width;
            data[o] = a;
            if (width > 1) data[o + 1] = b;
            if (width > 2) data[o + 2] = c;
            return true;
        }

        /// <summary>Component <paramref name="k"/> of sample <paramref name="i"/> (0 = oldest).</summary>
        public float Get(int i, int k) => data[((head + i) % capacity) * width + k];

        /// <summary>Copy the samples oldest first into <paramref name="into"/> (<c>width</c> floats each); returns the count.</summary>
        public int CopyTo(float[] into)
        {
            int n = Math.Min(count, into.Length / width);
            for (int i = 0; i < n; i++)
                for (int k = 0; k < width; k++) into[i * width + k] = Get(i, k);
            return n;
        }

        public void Clear()
        {
            head = count = 0;
            last = float.NegativeInfinity;
        }
    }

    /// <summary>
    /// Where one missile has been, in GLOBAL positions (the floating origin shifts local ones): a short fine trail for the plan view and a long coarse
    /// path from the launch for the side profile. The long path thins itself (every other sample goes, the sampling step doubles) whenever it fills,
    /// so a cruise missile that flies for minutes still keeps its launch and its whole arc in a fixed buffer. Pure.
    /// </summary>
    internal sealed class MissilePath
    {
        public const int RecentSamples = 36, LongSamples = 96;
        public const float RecentStep = 0.3f, LongStep = 0.5f;

        private readonly TrailRing recent = new TrailRing(RecentSamples, 3, RecentStep);
        private readonly float[] lx = new float[LongSamples], ly = new float[LongSamples], lz = new float[LongSamples];
        private int longCount;
        private float longStep = LongStep, longLast = float.NegativeInfinity;

        public int RecentCount => recent.Count;
        public int LongCount => longCount;

        /// <summary>Record the missile's global position at <paramref name="now"/> (cheap to call every tick).</summary>
        public void Add(float now, float x, float y, float z)
        {
            recent.Add(now, x, y, z);
            if (now - longLast < longStep) return;
            longLast = now;
            if (longCount == LongSamples)
            {
                int keep = 0;
                for (int i = 0; i < longCount; i += 2) { lx[keep] = lx[i]; ly[keep] = ly[i]; lz[keep] = lz[i]; keep++; }
                longCount = keep;
                longStep *= 2f;
            }
            lx[longCount] = x; ly[longCount] = y; lz[longCount] = z;
            longCount++;
        }

        /// <summary>Sample <paramref name="i"/> of the fine trail (0 = oldest).</summary>
        public void Recent(int i, out float x, out float y, out float z)
        {
            x = recent.Get(i, 0); y = recent.Get(i, 1); z = recent.Get(i, 2);
        }

        /// <summary>Sample <paramref name="i"/> of the whole path (0 = the launch).</summary>
        public void Long(int i, out float x, out float y, out float z)
        {
            x = lx[i]; y = ly[i]; z = lz[i];
        }

        public void Clear()
        {
            recent.Clear();
            longCount = 0;
            longStep = LongStep;
            longLast = float.NegativeInfinity;
        }
    }
}
