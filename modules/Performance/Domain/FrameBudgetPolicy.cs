namespace BoscaliSummer.Features.Performance.Domain
{
    /// <summary>Pure, bounded frame-pacing hysteresis. Inputs are local rendered-frame intervals.</summary>
    internal sealed class FrameBudgetPolicy
    {
        private const float WarmupSeconds = 4f;
        private const float WindowSeconds = 4f;
        private const float MaxSampleSeconds = 0.25f;
        private const float SlowMs = 40f;
        private const float FastMs = 25f;

        private float warmup;
        private float elapsed;
        private int frames;
        private int slowWindows;
        private int fastWindows;

        public bool Reduced { get; private set; }
        public float LastAverageMs { get; private set; }

        public FrameBudgetPolicy() => Reset();

        public void Reset()
        {
            Reduced = false;
            LastAverageMs = 0f;
            warmup = WarmupSeconds;
            elapsed = 0f;
            frames = 0;
            slowWindows = 0;
            fastWindows = 0;
        }

        public void Observe(float frameSeconds, bool active)
        {
            if (!active)
            {
                Reset();
                return;
            }
            if (frameSeconds <= 0f || frameSeconds > MaxSampleSeconds) return;
            if (warmup > 0f)
            {
                warmup -= frameSeconds;
                return;
            }

            elapsed += frameSeconds;
            frames++;
            if (elapsed < WindowSeconds) return;

            LastAverageMs = elapsed * 1000f / frames;
            elapsed = 0f;
            frames = 0;
            if (LastAverageMs > SlowMs)
            {
                slowWindows++;
                fastWindows = 0;
                if (slowWindows >= 2) Reduced = true;
            }
            else if (LastAverageMs < FastMs)
            {
                fastWindows++;
                slowWindows = 0;
                if (fastWindows >= 3) Reduced = false;
            }
            else
            {
                slowWindows = 0;
                fastWindows = 0;
            }
        }
    }
}
