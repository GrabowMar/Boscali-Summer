using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Networking;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// MALD-X notices on channel vg.maldx (every peer, from MaldxNet events). Launch and loss go to the launching
    /// pilot; exposures to every pilot of the bait's side (the map icon comes from the game's tracking database).
    /// </summary>
    internal static class MaldxHud
    {
        private const string Channel = "vg.maldx";
        private static IHudBoard board;

        public static void Show(MaldxEvent e)
        {
            if (board == null && !ModuleServices.TryGet(out board)) return;
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled) return;
            VanguardHudFeeds.DeclareChannel(Channel, "MALD-X");
            bool mine = e.Launcher != 0u && me.persistentID.Id == e.Launcher;
            switch (e.Kind)
            {
                case MaldxNet.Airborne:
                    if (mine) board.Notice(Channel, HudTone.Info, "BAIT AIRBORNE");
                    break;
                case MaldxNet.Down:
                    if (mine) board.Notice(Channel, HudTone.Info, "BAIT DOWN — " + e.Count + " EXPOSED");
                    break;
                case MaldxNet.Chaffed:
                    if (mine || me.persistentID.Id == e.Site) board.Notice(Channel, HudTone.Info, "CHAFF — MISSILE BLINDED", e.Count + " BLINDED");
                    break;
                case MaldxNet.Exposed:
                    if (!UnitRegistry.TryGetUnit(new PersistentID { Id = e.Site }, out Unit site) || site.NetworkHQ == me.NetworkHQ) return;
                    if (UnitRegistry.TryGetUnit(new PersistentID { Id = e.Bait }, out Unit bait) && bait.NetworkHQ != me.NetworkHQ) return;
                    float km = FastMath.Distance(site.GlobalPosition(), me.GlobalPosition()) / 1000f;
                    board.Notice(Channel, HudTone.Caution, "EMITTER EXPOSED " + site.definition.unitName.ToUpperInvariant() + " " + km.ToString("F1") + " KM",
                        e.Count + " EXPOSED");
                    break;
            }
        }
    }
}
