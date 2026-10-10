using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Local pilot's AEGIS feed (channel vg.aegis): a held "AEGIS n READY" line while the pod is the selected station,
    /// and notices for SNAP AWAY / NO SOLUTION (local trigger) and INTERCEPT / SNAP HIT (server cues via AegisNet).
    /// </summary>
    internal sealed class AegisHud : MonoBehaviour, ISceneService
    {
        private const string Owner = "vanguard.aegis";
        private const string Channel = "vg.aegis";
        private static readonly string ReadyDetail = "SNAP " + AegisSnap.Km(AegisSnap.MaxRange) + " km · AUTO DEFENCE";
        private static IHudBoard board;
        private IHudLine line;
        private int readyAmmo = -1; // ammo the cached ready text was built for
        private string readyText;

        public void ResetForScene()
        {
            line?.Release();
            line = null;
        }

        private static bool Board()
        {
            if (board == null && !ModuleServices.TryGet(out board)) return false;
            VanguardHudFeeds.DeclareChannel(Channel, "AEGIS");
            return true;
        }

        private static bool IsLocal(Unit unit) =>
            unit != null && GameManager.GetLocalAircraft(out Aircraft me) && me == unit;

        public static void Notify(Unit aircraft, HudTone tone, string text, string detail)
        {
            if (IsLocal(aircraft) && Board()) board.Notice(Channel, tone, text, detail);
        }

        public static void Cue(Unit launcher, AegisCue cue)
        {
            if (cue == AegisCue.SnapHit) Notify(launcher, HudTone.Info, "SNAP HIT", "HIT-TO-KILL");
            else Notify(launcher, HudTone.Warning, "INTERCEPT", "AUTO DEFENCE DART AWAY");
        }

        private void Update()
        {
            WeaponStation station = null;
            if (GameManager.GetLocalAircraft(out Aircraft me) && me != null && !me.disabled && me.weaponManager != null)
                station = me.weaponManager.currentWeaponStation;
            if (!AegisTrigger.IsAegis(station) || !Board())
            {
                line?.Release();
                line = null;
                return;
            }
            line ??= VanguardHudFeeds.Acquire(board, Owner, Channel, "ready");
            float full = Mathf.Max(station.FullAmmo, 1);
            if (station.Ammo > 0)
            {
                if (readyAmmo != station.Ammo)
                {
                    readyAmmo = station.Ammo;
                    readyText = "AEGIS " + readyAmmo + " READY";
                }
                line?.Set(HudTone.Info, readyText, ReadyDetail, station.Ammo / full);
            }
            else line?.Set(HudTone.Caution, "AEGIS EMPTY", "NO DEFENCE DARTS", 0f);
        }
    }
}
