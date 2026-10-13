using BoscaliSummer.Modules.Wing.Networking;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>The SQUADRON tab's element blocks (spec WMC program §4): rows in element order A–D, seats ascending inside each,
    /// and each block's header words.</summary>
    internal static class ElementGroups
    {
        /// <summary>Row indices sorted by element, then seat; returns how many.</summary>
        public static int Order(SnapshotMember[] rows, int count, int[] into)
        {
            for (int i = 0; i < count; i++) into[i] = i;
            // Insertion sort: stable, allocation-free, eight rows at most.
            for (int i = 1; i < count; i++)
            {
                int k = into[i], j = i - 1;
                while (j >= 0 && Before(rows[k], rows[into[j]]))
                {
                    into[j + 1] = into[j];
                    j--;
                }
                into[j + 1] = k;
            }
            return count;
        }

        private static bool Before(in SnapshotMember a, in SnapshotMember b) =>
            a.Element != b.Element ? a.Element < b.Element : a.Slot < b.Slot;
    }
}
