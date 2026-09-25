using BoscaliSummer.Runtime;

namespace BoscaliSummer.Features.Command.Runtime
{
    /// <summary>
    /// Mission-scoped faction resources. Call on Unity's main thread. Morale starts at
    /// 50 on a 0–100 scale. New contract offers
    /// use it to scale money and XP.
    /// Host writes are authoritative; remote reads use a bounded host snapshot.
    /// Future sibling-module consumers must use a narrow Framework contract.
    /// </summary>
    public static class FactionResources
    {
        public static bool TryGetMorale(FactionHQ faction, out float morale)
        {
            morale = 0f;
            if (faction == null || CommandManager.Active == null) return false;
            return GameAccess.IsServer()
                ? CommandManager.Active.Morale.TryGet(faction.GetInstanceID(), out morale)
                : CommandManager.Active.TryGetRemoteMorale(faction.faction?.factionName, out morale);
        }
    }
}
