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
    /// SKYWELL on the common HUD element and the radial menu. A receiver pilot is walked through the CATOBAR-style
    /// hook-up with director-light cues (UP/DOWN, FWD/AFT, like a tanker's belly lights), then HOLD BRAKE, docking,
    /// transfer and served; a tanker pilot sees deploy state, receiver state and stock.
    /// </summary>
    internal sealed class SkywellHud : MonoBehaviour, ISceneService, ISkywellControl
    {
        private const string Owner = "vanguard.skywell";
        private const string Channel = "skywell";
        private const float CueRange = 800f;
        private const float Refresh = 0.2f;

        private IHudBoard board;
        private IHudLine line;
        private float next;
        private readonly Dictionary<uint, SkywellPhase> lastPhase = new Dictionary<uint, SkywellPhase>();
        private uint docking;           // tanker the local aircraft is docked to
        private float engageFrom = 1f;  // probe distance when docking began (bar)
        private float transferStart, transferSeconds;

        public void ResetForScene()
        {
            line?.Release();
            line = null;
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
                    ? $"FUEL {v.FuelKg / 1000f:0.0}T  AMMO {v.StockKg / 1000f:0.00}T"
                    : $"FUEL {SkywellService.FuelStockKg / 1000f:0.0}T  AMMO {SkywellService.MunitionStockKg / 1000f:0.00}T";
            }
        }

        public void Toggle()
        {
            if (Local(out Aircraft a) && SkywellBoard.KitStation(a) != null) SkywellNet.RequestToggle(a);
        }

        private static bool Local(out Aircraft a) => GameManager.GetLocalAircraft(out a) && a != null && !a.disabled;

        // ---- HUD

        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Refresh;
            if (board == null && !ModuleServices.TryGet(out board)) return;
            board.DeclareChannel(Channel, "SKYWELL");
            if (!Local(out Aircraft me) || !(SkywellBoard.KitStation(me) != null ? Tanker(me) : Receiver(me)))
            {
                line?.Release();
                line = null;
            }
        }

        private IHudLine Line() => line ??= board.Acquire(Owner, Channel, "status");

        private bool Tanker(Aircraft me)
        {
            if (!SkywellBoard.Views.TryGetValue(me.persistentID.Id, out SkywellView v) || !v.Active) return false;
            string stock = $"FUEL {v.FuelKg / 1000f:0.0} t  ·  MUNITIONS {v.StockKg / 1000f:0.00} t";
            string state = v.Receiver == 0 ? "DEPLOYED  ·  AWAITING RECEIVER"
                : v.Phase == SkywellPhase.Engage ? "RECEIVER DOCKING"
                : v.Phase == SkywellPhase.Transfer ? "TRANSFERRING" : "RECEIVER SERVED";
            Line()?.Set(v.Receiver == 0 ? HudTone.Info : HudTone.Caution, "SKYWELL  " + state, stock, 0f);
            return true;
        }

        private bool Receiver(Aircraft me)
        {
            uint id = me.persistentID.Id;
            foreach (SkywellView v in SkywellBoard.Views.Values)
                if (v.Receiver == id) return Docked(me, v);
            if (docking != 0)
            {
                Notice(lastPhase.TryGetValue(docking, out SkywellPhase p) && p == SkywellPhase.Served
                    ? "SKYWELL  DISCONNECTED" : "SKYWELL  CONTACT LOST", HudTone.Info);
                docking = 0;
            }
            return Approach(me);
        }

        private bool Docked(Aircraft me, SkywellView v)
        {
            Aircraft tanker = Resolve(v.Tanker);
            if (tanker == null) return false;
            float dist = Vector3.Distance(SkywellBoard.ProbePoint(me), SkywellBoard.ContactPoint(tanker));
            bool fresh = !lastPhase.TryGetValue(v.Tanker, out SkywellPhase was) || was != v.Phase || docking != v.Tanker;
            if (docking != v.Tanker)
            {
                docking = v.Tanker;
                engageFrom = Mathf.Max(dist, 1f);
            }
            lastPhase[v.Tanker] = v.Phase;
            switch (v.Phase)
            {
                case SkywellPhase.Engage:
                    Line()?.Set(HudTone.Caution, "SKYWELL  DOCKING", "HOLD BRAKE  ·  " + Mathf.RoundToInt(dist) + " m",
                        Mathf.Clamp01(1f - dist / engageFrom));
                    break;
                case SkywellPhase.Transfer:
                    if (fresh)
                    {
                        transferStart = Time.time;
                        transferSeconds = SkywellContact.TransferSeconds(SkywellService.FuelNeedKg(me), SkywellService.MissingRounds(me));
                    }
                    Line()?.Set(HudTone.Caution, "SKYWELL  TRANSFER", "FUEL + MUNITIONS  ·  HOLD BRAKE",
                        Mathf.Clamp01((Time.time - transferStart) / Mathf.Max(transferSeconds, 0.1f)));
                    break;
                default:
                    if (fresh) Notice("SKYWELL  SERVICE COMPLETE", HudTone.Info);
                    Line()?.Set(HudTone.Info, "SKYWELL  SERVED", "RELEASE BRAKE TO DISCONNECT", 1f);
                    break;
            }
            return true;
        }

        /// <summary>Nearest deployed friendly kit: director-light cues into the hook-up window.</summary>
        private bool Approach(Aircraft me)
        {
            Aircraft best = null;
            float bestDist = CueRange;
            foreach (SkywellView v in SkywellBoard.Views.Values)
            {
                if (!v.Active || v.Receiver != 0) continue;
                Aircraft tanker = Resolve(v.Tanker);
                if (tanker == null || tanker.disabled || tanker == me || tanker.NetworkHQ != me.NetworkHQ) continue;
                float d = Vector3.Distance(SkywellBoard.ProbePoint(me), SkywellBoard.ContactPoint(tanker));
                if (d < bestDist)
                {
                    bestDist = d;
                    best = tanker;
                }
            }
            if (best == null) return false;
            // Receiver probe relative to the contact point, tanker axes (+y up, +z forward).
            Vector3 rel = best.transform.InverseTransformPoint(SkywellBoard.ProbePoint(me)) -
                          best.transform.InverseTransformPoint(SkywellBoard.ContactPoint(best));
            if (bestDist <= SkywellService.EngageRadius && rel.z < 5f)
            {
                Line()?.Set(HudTone.Warning, "SKYWELL  READY", "HOLD BRAKE TO DOCK", 1f);
                return true;
            }
            Line()?.Set(HudTone.Info, "SKYWELL  TANKER  " + Mathf.RoundToInt(bestDist) + " m", Director(rel),
                Mathf.Clamp01(1f - (bestDist - SkywellService.EngageRadius) / (CueRange - SkywellService.EngageRadius)));
            return true;
        }

        /// <summary>Tanker belly-light style cue: which way to move the jet, worst axis first.</summary>
        private static string Director(Vector3 rel)
        {
            if (rel.z > 5f) return "DROP BACK  ·  AFT " + Mathf.RoundToInt(rel.z) + " m";
            var cue = new List<string>(3);
            if (rel.y > 10f) cue.Add("DOWN " + Mathf.RoundToInt(rel.y));
            else if (rel.y < -10f) cue.Add("UP " + Mathf.RoundToInt(-rel.y));
            if (rel.x > 10f) cue.Add("LEFT " + Mathf.RoundToInt(rel.x));
            else if (rel.x < -10f) cue.Add("RIGHT " + Mathf.RoundToInt(-rel.x));
            cue.Add("FWD " + Mathf.RoundToInt(-rel.z));
            return string.Join("  ·  ", cue) + " m";
        }

        private void Notice(string text, HudTone tone) => board?.Notice(Channel, tone, text);

        private static Aircraft Resolve(uint id) =>
            id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) ? u as Aircraft : null;
    }
}
