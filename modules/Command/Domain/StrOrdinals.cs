using System.Collections.Generic;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// First-seen numbering of platforms (P1..) and sorties (S1..): a key keeps its number for the mission on this client.
    /// Numbers are labels that link the panel to the map, not ranks, and another client may number differently.
    /// </summary>
    internal sealed class StrOrdinals
    {
        public const int Max = 99;

        private readonly string[] tags = new string[Max + 1];
        private readonly Dictionary<string, int> byText = new Dictionary<string, int>();
        private readonly Dictionary<int, int> byNumber = new Dictionary<int, int>();
        private int next = 1;

        public StrOrdinals(string prefix)
        {
            tags[0] = "";
            for (int i = 1; i <= Max; i++) tags[i] = (prefix ?? "") + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The number of a key, assigning the next free one on first sight; 0 when the table is full or the key is null.</summary>
        public int Of(string key)
        {
            if (key == null) return 0;
            if (byText.TryGetValue(key, out int n)) return n;
            if (next > Max) return 0;
            byText[key] = next;
            return next++;
        }

        public int Of(int key)
        {
            if (byNumber.TryGetValue(key, out int n)) return n;
            if (next > Max) return 0;
            byNumber[key] = next;
            return next++;
        }

        /// <summary>"P2" for number 2; "" for 0 or a number outside the table.</summary>
        public string Tag(int number) => number >= 0 && number <= Max ? tags[number] : "";

        /// <summary>Forget everything (scene reset, session or faction change).</summary>
        public void Reset()
        {
            byText.Clear();
            byNumber.Clear();
            next = 1;
        }
    }
}
