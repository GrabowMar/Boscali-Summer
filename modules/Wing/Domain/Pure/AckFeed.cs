namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>One order result as the HUD chip and SORTIE's event list show it.</summary>
    internal struct AckLine
    {
        public float Time;
        public string Who, What, Reason;
        public bool Accepted;

        public string Chip() => Accepted
            ? (Who + " WILCO · " + (What ?? "")).Trim()
            : (Who + " UNABLE · " + (string.IsNullOrEmpty(Reason) ? "REFUSED" : Reason.ToUpperInvariant())).Trim();
    }

    /// <summary>Order results for the HUD ack chip and BEHAVIOUR › SORTIE's events (user ruling 2026-10-04). A bounded ring:
    /// the newest <see cref="Capacity"/> are kept; a chip shows for <see cref="ChipSeconds"/>, at most <see cref="MaxChips"/>.</summary>
    internal sealed class AckFeed
    {
        public const int Capacity = 16, MaxChips = 2;
        public const float ChipSeconds = 3f;

        private readonly AckLine[] ring = new AckLine[Capacity];
        private int head, count;

        public int Count => count;

        public void Push(float time, string who, string what, bool accepted, string reason)
        {
            ring[head] = new AckLine { Time = time, Who = who ?? "", What = what, Accepted = accepted, Reason = reason };
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
        }

        /// <summary>The i-th newest result (0 = latest); default when out of range.</summary>
        public AckLine Newest(int i) => i < 0 || i >= count ? default : ring[(head - 1 - i + Capacity * 2) % Capacity];

        public int Chips(float now, AckLine[] into)
        {
            if (into == null) return 0;
            int n = 0;
            for (int i = 0; i < count && n < into.Length && n < MaxChips; i++)
            {
                AckLine l = Newest(i);
                if (now - l.Time > ChipSeconds) break;
                into[n++] = l;
            }
            return n;
        }
    }

    /// <summary>The "who" of an ack chip: WING, an element letter, or #n (+k for more members). Kind is ScopeKind as a byte
    /// (0 wing, 1 element, 2 members); firstNumber is the first member's #n, 0 when unknown.</summary>
    internal static class AckWords
    {
        public static string Who(byte kind, int element, int firstNumber, int memberCount)
        {
            if (kind == 1 && element >= 0 && element < 4) return ((char)('A' + element)).ToString();
            if (kind != 2) return "WING";
            if (firstNumber <= 0) return memberCount + " AC";
            return "#" + firstNumber + (memberCount > 1 ? " +" + (memberCount - 1) : "");
        }
    }
}
