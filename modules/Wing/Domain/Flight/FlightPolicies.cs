namespace BoscaliSummer.Modules.Wing.Domain
{

    /// <summary>Restart timeout whenever a decreasing progress value advances.</summary>
    internal sealed class CargoProgressTracker
    {
        public int LastAmount { get; private set; }
        public float LastProgressAt { get; private set; }
        public bool MadeProgress { get; private set; }

        public void Reset(int amount, float now)
        {
            LastAmount = amount;
            LastProgressAt = now;
            MadeProgress = false;
        }

        public bool Observe(int amount, float now)
        {
            if (amount >= LastAmount) return false;
            LastAmount = amount;
            LastProgressAt = now;
            MadeProgress = true;
            return true;
        }
    }
}
