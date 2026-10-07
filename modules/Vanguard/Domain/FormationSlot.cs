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

        public static Vector3 World(Vector3 position, Vector3 forward, Vector3 right, Vector3 up, int slot)
        {
            Vector3 local = Local(slot);
            return position + right * local.x + up * local.y + forward * local.z;
        }
    }
}
