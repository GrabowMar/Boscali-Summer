using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// ALE-X KITE on the common HUD element (channel vg.alex): a held line "KITE  SUP DEF SED / LINE 150 m" for the
    /// decoy on the fiber whose pips light as each layer works, the count of missiles riding the kite and (with the
    /// pod selected) the fire-to-cut cue; a second line counts down a cut decoy that is still adrift, also while the
    /// next one is towed; and one notice for deploy, cut ("KITE CUT - 2 MISSILES DRAGGED"), snap or loss.
    /// </summary>
    internal sealed class AlexHud : MonoBehaviour, ISceneService
    {
        private const string Owner = "vanguard.alex";
        private const string Channel = "vg.alex";
        private const float Refresh = 0.2f;

        private IHudBoard board;
        private IHudLine line, adrift;
        private float next;
        private uint lastHost, lastDecoy; // lastDecoy: the decoy on the fiber when the HUD last looked

        public void ResetForScene()
        {
            Drop();
            lastHost = lastDecoy = 0;
        }

        private void OnDestroy() => Drop();

        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Refresh;
            if (board == null && !ModuleServices.TryGet(out board)) return;
            board.DeclareChannel(Channel, "KITE");
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled)
            {
                Drop();
                return;
            }
            uint host = me.persistentID.Id;
            if (host != lastHost)
            {
                lastHost = host; // a new aircraft starts with a clean slate
                lastDecoy = 0;
            }
            bool towing = AlexBoard.TowedBy(host, out AlexView on);
            bool loose = AlexBoard.FreeOf(host, out AlexView free);
            if (lastDecoy != 0 && (!towing || on.Decoy != lastDecoy))
            {
                Ended(lastDecoy);
                lastDecoy = 0;
            }
            if (towing && on.Decoy != lastDecoy)
            {
                lastDecoy = on.Decoy;
                board.Notice(Channel, HudTone.Info, "KITE AWAY");
            }
            if (towing) ShowTowed(on, me);
            else Release(ref line);
            if (loose) ShowAdrift(free);
            else Release(ref adrift);
        }

        // The decoy that was on the fiber is no longer: cut loose (its view stays, Free) or gone (shot down, host hit).
        private void Ended(uint decoy)
        {
            if (AlexBoard.Views.TryGetValue(decoy, out AlexView cut) && cut.State == KiteState.Free)
                board.Notice(Channel, cut.Reason == KiteCut.Snap || cut.Reason == KiteCut.Low ? HudTone.Warning : HudTone.Info,
                    AlexKite.CutText(cut.Reason, cut.Dragged));
            else board.Notice(Channel, HudTone.Caution, "KITE LOST");
        }

        private void ShowTowed(AlexView view, Aircraft me)
        {
            line ??= board.Acquire(Owner, Channel, "status");
            // The fire press only cuts with the pod selected, so only then is it offered.
            bool podSelected = AlexFire.IsPod(me.weaponManager != null ? me.weaponManager.currentWeaponStation : null);
            int severity = AlexKite.Severity(view.Pips, view.Captured);
            line?.Set(severity == 2 ? HudTone.Warning : severity == 1 ? HudTone.Caution : HudTone.Info,
                AlexKite.StatusText(view.Pips), AlexKite.LineDetail(view.Line, view.Captured, podSelected),
                view.Line / TowedTrail.NearLine);
        }

        private void ShowAdrift(AlexView view)
        {
            adrift ??= board.Acquire(Owner, Channel, "adrift");
            float left = Mathf.Max(0f, view.FreeLeft - (Time.unscaledTime - view.Stamp));
            adrift?.Set(HudTone.Info, "KITE ADRIFT", "SELF-DESTRUCT " + Mathf.CeilToInt(left) + " s", left / AlexKite.FreeSeconds);
        }

        private static void Release(ref IHudLine held)
        {
            held?.Release();
            held = null;
        }

        private void Drop()
        {
            Release(ref line);
            Release(ref adrift);
        }
    }
}
