using BoscaliSummer.Runtime;

namespace BoscaliSummer.Features.Command.Runtime
{
    /// <summary>
    /// Mission-scoped faction resources. Call on Unity's main thread. Morale starts at
    /// 100 on a 0–100 scale, has no gameplay effects, and is currently host-only.
    /// Reads/writes return false on remote clients or when Command is unavailable.
    /// Future sibling-module consumers must use a narrow Framework contract.
    /// </summary>
    public static class FactionResources
    {
        public static bool TryGetMorale(FactionHQ faction, out float morale)
        {
            morale = 0f;
            return faction != null && GameAccess.IsServer() && CommandManager.Active != null &&
                CommandManager.Active.Morale.TryGet(faction.GetInstanceID(), out morale);
        }

        /// <summary>Rejects non-finite/out-of-range values; never changes native resources.</summary>
        public static bool TrySetMorale(FactionHQ faction, float morale) =>
            faction != null && GameAccess.IsServer() && CommandManager.Active != null &&
            CommandManager.Active.Morale.TrySet(faction.GetInstanceID(), morale);
    }
}
