namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>The last host actions, newest first: four lines for the log card.</summary>
    internal sealed class MetOfficeLog
    {
        public const int Capacity = 4;
        private readonly string[] lines = new string[Capacity];
        private int count;

        /// <summary>0 is the newest line.</summary>
        public string this[int newestFirst] => newestFirst >= 0 && newestFirst < count ? lines[newestFirst] : "";

        public void Add(string line)
        {
            for (int i = Capacity - 1; i > 0; i--) lines[i] = lines[i - 1];
            lines[0] = line ?? "";
            if (count < Capacity) count++;
        }

        public string Joined() => string.Join("\n", lines, 0, count);
    }
}
