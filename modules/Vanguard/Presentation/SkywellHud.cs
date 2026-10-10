using System.Collections.Generic;
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
    /// SKYWELL on the radial menu (carried, deployed, stock, toggle) and the ticker (the end of a hook-up). The receiver's
    /// display is the DOCK page of the TAC BOARD (<see cref="SkywellScopeFeed"/>: director cues, HOLD BRAKE, transfer, served)
    /// and the tanker pilot's is its stock gauge.
    /// </summary>
    internal sealed class SkywellHud : MonoBehaviour, ISceneService, ISkywellControl
    {
        private const string Channel = "skywell";
        private const float Refresh = 0.2f;

        private IHudBoard board;
        private float next;
        private readonly Dictionary<uint, SkywellPhase> lastPhase = new Dictionary<uint, SkywellPhase>();
        private uint docking;           // tanker the local aircraft is docked to

        public void ResetForScene()
        {
            lastPhase.Clear();
            docking = 0;
        }

        private void OnDestroy() => ResetForScene();

        // ---- radial

        public bool Carried => Local(out Aircraft a) && SkywellBoard.KitStation(a) != null;

        public bool Deployed => Local(out Aircraft a) && SkywellBoard.Views.TryGetValue(a.persistentID.Id, out SkywellView v) && v.Active;

        public string Stock
        {
            get
            {
                if (!Local(out Aircraft a)) return "";
                return SkywellBoard.Views.TryGetValue(a.persistentID.Id, out SkywellView v)
                    ? $"FUEL {Mass(v.FuelKg, 1)}  AMMO {Mass(v.StockKg, 2)}"
                    : $"FUEL {Mass(SkywellService.FuelStockKg, 1)}  AMMO {Mass(SkywellService.MunitionStockKg, 2)}";
            }
        }

        private static bool Imperial => PlayerSettings.unitSystem != PlayerSettings.UnitSystem.Metric;

        /// <summary>A stock in the player's units: "12.3 t" or "27100 lb".</summary>
        private static string Mass(float kg, int decimals) => Imperial
            ? Mathf.Round(kg * 2.20462f / 100f) * 100f + " lb"
            : (kg / 1000f).ToString(decimals == 1 ? "0.0" : "0.00", System.Globalization.CultureInfo.InvariantCulture) + " t";

        public void Toggle()
        {
            if (Local(out Aircraft a) && SkywellBoard.KitStation(a) != null) SkywellNet.RequestToggle(a);
        }

        private static bool Local(out Aircraft a) => GameManager.GetLocalAircraft(out a) && a != null && !a.disabled;

        // ---- notices (the DOCK page, SkywellScopeFeed, is the receiver's display now: only the hook-up's end is announced here)

        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Refresh;
            if (board == null && !ModuleServices.TryGet(out board)) return;
            if (!Local(out Aircraft me) || SkywellBoard.KitStation(me) != null)
            {
                docking = 0;
                return;
            }
            uint id = me.persistentID.Id;
            foreach (SkywellView v in SkywellBoard.Views.Values)
            {
                if (v.Receiver != id) continue;
                bool fresh = !lastPhase.TryGetValue(v.Tanker, out SkywellPhase was) || was != v.Phase || docking != v.Tanker;
                docking = v.Tanker;
                lastPhase[v.Tanker] = v.Phase;
                if (fresh && v.Phase == SkywellPhase.Served) Notice("SKYWELL  SERVICE COMPLETE", HudTone.Info);
                return;
            }
            if (docking != 0)
            {
                Notice(lastPhase.TryGetValue(docking, out SkywellPhase p) && p == SkywellPhase.Served
                    ? "SKYWELL  DISCONNECTED" : "SKYWELL  CONTACT LOST", HudTone.Info);
                docking = 0;
            }
        }

        private void Notice(string text, HudTone tone) => board?.Notice(Channel, tone, text);
    }
}
