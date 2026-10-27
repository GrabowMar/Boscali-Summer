using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>REMORA wing slots: alternating starboard/port echelon behind the launcher.</summary>
    internal static class FormationSlot
    {
        public static Vector3 Local(int slot)
        {
            int rank = slot / 2;
            float side = slot % 2 == 0 ? 1f : -1f;
            return new Vector3(side * (45f + 30f * rank), 5f * rank, -55f - 35f * rank);
        }

        /// <summary>MANTA slots: the same echelon, wider, higher and further back, so the big drone never sits where a REMORA does.</summary>
        public static Vector3 BigLocal(int slot)
        {
            int rank = slot / 2;
            float side = slot % 2 == 0 ? 1f : -1f;
            return new Vector3(side * (120f + 60f * rank), 12f + 6f * rank, -80f - 50f * rank);
        }

        /// <summary>Lowest slot no live drone holds, so a lost wingman's slot is refilled first.</summary>
        public static int FirstFree(System.Collections.Generic.ICollection<int> used)
        {
            int slot = 0;
            while (used.Contains(slot)) slot++;
            return slot;
        }

        public static Vector3 World(Vector3 position, Vector3 forward, Vector3 right, Vector3 up, int slot)
        {
            Vector3 local = Local(slot);
            return position + right * local.x + up * local.y + forward * local.z;
        }

        public static Vector3 World(Vector3 position, Vector3 forward, Vector3 right, Vector3 up, int slot, bool big)
        {
            Vector3 local = big ? BigLocal(slot) : Local(slot);
            return position + right * local.x + up * local.y + forward * local.z;
        }
    }
}
