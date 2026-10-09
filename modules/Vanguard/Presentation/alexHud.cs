using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// ALE-X KITE on the common HUD element (channel vg.alex): a held line "KITE  SUP DEF SED / LINE 150 m" whose pips
    /// light as each layer works, the count of missiles riding the kite and the fire-to-cut cue, a countdown after
    /// the cut, and one notice for deploy, cut ("KITE CUT - 2 MISSILES DRAGGED"), snap or loss.
    /// </summary>
    internal sealed class AlexHud : MonoBehaviour, ISceneService
    {
        private const string Owner = "vanguard.alex";
        private const string Channel = "vg.alex";
        private const float Refresh = 0.2f;

        private IHudBoard board;
        private IHudLine line;
        private float next;
        private uint lastHost, lastDecoy;
        private KiteState lastState;

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
            if (me.persistentID.Id != lastHost)
            {
                lastHost = me.persistentID.Id; // a new aircraft starts with a clean slate
                lastDecoy = 0;
            }
            if (!AlexBoard.ForHost(me.persistentID.Id, out AlexView view))
            {
                if (lastDecoy != 0 && lastState != KiteState.Free) board.Notice(Channel, HudTone.Caution, "KITE LOST");
                lastDecoy = 0;
                Drop();
                return;
            }
            if (view.Decoy != lastDecoy)
            {
                lastDecoy = view.Decoy;
                lastState = view.State;
                board.Notice(Channel, HudTone.Info, "KITE AWAY");
            }
            if (view.State == KiteState.Free && lastState != KiteState.Free)
                board.Notice(Channel, view.Reason == KiteCut.Snap || view.Reason == KiteCut.Low ? HudTone.Warning : HudTone.Info,
                    AlexKite.CutText(view.Reason, view.Dragged));
            lastState = view.State;
            line ??= board.Acquire(Owner, Channel, "status");
            if (view.State == KiteState.Free)
            {
                float left = Mathf.Max(0f, view.FreeLeft - (Time.unscaledTime - view.Stamp));
                line?.Set(HudTone.Info, "KITE ADRIFT", "SELF-DESTRUCT " + Mathf.CeilToInt(left) + " s", left / AlexKite.FreeSeconds);
                return;
            }
            int severity = AlexKite.Severity(view.Pips, view.Captured);
            line?.Set(severity == 2 ? HudTone.Warning : severity == 1 ? HudTone.Caution : HudTone.Info,
                AlexKite.StatusText(view.Pips), AlexKite.LineDetail(view.Line, view.Captured, true),
                view.Line / TowedTrail.NearLine);
        }

        private void Drop()
        {
            line?.Release();
            line = null;
        }
    }
}
