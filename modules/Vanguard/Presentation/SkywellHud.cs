using System.Collections.Generic;
using NOAvionics;
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
    /// SKYWELL on the common HUD element and the radial menu. A receiver pilot is walked into the service box with
    /// director-light cues (UP/DOWN, LEFT/RIGHT, CLOSE/DROP BACK, like a tanker's belly lights), then HOLD BOX while the
    /// glider comes under the belly, the pit stop job by job (skywellHudPit.cs), SERVICE COMPLETE and, if the box is
    /// left early, BOX LOST; a tanker pilot sees deploy state, receiver state and stock.
    /// </summary>
    internal sealed partial class SkywellHud : MonoBehaviour, ISceneService, ISkywellControl
    {
        private const string Owner = "vanguard.skywell";
        private const string Channel = "vg.skywell";
        private const float CueRange = 800f;
        private const float Refresh = 0.2f;

        private IHudBoard board;
        private IHudLine line;
        private float next;
        private readonly Dictionary<uint, SkywellPhase> lastPhase = new Dictionary<uint, SkywellPhase>();
        private uint docking;           // tanker whose glider is serving the local aircraft
        private float engageFrom = 1f;  // glider's distance to its pose when it was sent (bar)

        public void ResetForScene()
        {
            line?.Release();
            line = null;
            pitLine?.Release();
            pitLine = null;
            lastPhase.Clear();
            lastFlags.Clear();
            docking = 0;
            pitAvailable = false;
            slots.Clear();
            slotSignature = 0;
            priority = -1;
            order = 0;
            orderOwner = null;
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

        // The player's own unit system: the cues used to hard-code metres and tonnes.
        private static readonly char[] Scratch = new char[24];

        private static bool Imperial => PlayerSettings.unitSystem != PlayerSettings.UnitSystem.Metric;

        /// <summary>A short distance in the player's units: "12m" or "39ft".</summary>
        private static string Dist(float metres)
        {
            int len = AvUnitTable.AltitudeReading(Scratch, 0, metres, Imperial ? AvUnits.Imperial : AvUnits.Metric);
            return new string(Scratch, 0, len);
        }

        /// <summary>A stock in the player's units: "12.3 t" or "27100 lb".</summary>
        private static string Mass(float kg, int decimals) => Imperial
            ? Mathf.Round(kg * 2.20462f / 100f) * 100f + " lb"
            : (kg / 1000f).ToString(decimals == 1 ? "0.0" : "0.00", System.Globalization.CultureInfo.InvariantCulture) + " t";

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
            bool shown = false;
            pitAvailable = false;
            if (Local(out Aircraft me))
            {
                if (me != orderOwner) NewAircraft(me);
                if (SkywellBoard.KitStation(me) != null) shown = Tanker(me);
                else
                {
                    shown = Receiver(me); // sets pitAvailable: docked, or any deployed friendly kit within cue range
                    RefreshSlots(me);
                }
            }
            if (!shown)
            {
                line?.Release();
                line = null;
                pitLine?.Release();
                pitLine = null;
            }
        }

        private IHudLine Line() => line ??= board.Acquire(Owner, Channel, "status");

        private IHudLine PitLine() => pitLine ??= board.Acquire(Owner, Channel, "pit");

        private bool Tanker(Aircraft me)
        {
            if (!SkywellBoard.Views.TryGetValue(me.persistentID.Id, out SkywellView v) || !v.Active) return false;
            Edges(v, true);
            string stock = $"FUEL {v.FuelKg / 1000f:0.0} t  ·  MUNITIONS {v.StockKg / 1000f:0.00} t";
            if (v.Receiver == 0)
                Line()?.Set(HudTone.Info, (v.Flags & PitFlags.BoxLost) != 0 ? "SKYWELL  BOX LOST" : "SKYWELL  DEPLOYED  ·  AWAITING RECEIVER", stock, 0f);
            else if (v.Phase == SkywellPhase.Transfer)
            {
                Aircraft receiver = Resolve(v.Receiver);
                string who = receiver != null ? receiver.unitName : "RECEIVER";
                Line()?.Set(HudTone.Caution, "SKYWELL  SERVING  " + who,
                    $"JOB {v.JobIndex + 1}/{v.JobCount}  ·  {(v.Job == PitJobKind.Fuel ? "FUEL" : "STN " + (v.JobStation + 1))}  ·  FUEL {v.FuelKg / 1000f:0.0} t  ·  MUN {v.StockKg / 1000f:0.00} t",
                    TankerJobProgress(v));
            }
            else
                Line()?.Set(HudTone.Caution, v.Phase == SkywellPhase.Engage ? "SKYWELL  GLIDER TO RECEIVER" : "SKYWELL  SERVICE COMPLETE", stock, 0f);
            return true;
        }

        private bool Receiver(Aircraft me)
        {
            uint id = me.persistentID.Id;
            foreach (SkywellView v in SkywellBoard.Views.Values)
                if (v.Receiver == id) return pitAvailable = Docked(me, v);
            if (docking != 0)
            {
                byte flags = SkywellBoard.Views.TryGetValue(docking, out SkywellView gone) ? gone.Flags : (byte)0;
                if ((flags & PitFlags.HotBreak) != 0)
                {
                    Notice("SKYWELL  HOT BREAK  MISSILE INBOUND", HudTone.Warning);
                    // The server refuses this jet for a while: show it.
                    SkywellBoard.HotLockUntil = Time.timeSinceLevelLoad + SkywellService.HotBreakCooldown;
                }
                else if ((flags & PitFlags.BoxLost) != 0) Notice("SKYWELL  BOX LOST", HudTone.Warning);
                else if (!lastPhase.TryGetValue(docking, out SkywellPhase p) || p != SkywellPhase.Served) Notice("SKYWELL  CONTACT LOST", HudTone.Info);
                lastFlags.Remove(docking);
                docking = 0;
                priority = -1; // one-shot
                pitLine?.Release();
                pitLine = null;
            }
            return Approach(me);
        }

        private bool Docked(Aircraft me, SkywellView v)
        {
            Aircraft tanker = Resolve(v.Tanker);
            if (tanker == null) return false;
            float gap = SkywellVisuals.GliderGap(v.Tanker);
            bool fresh = !lastPhase.TryGetValue(v.Tanker, out SkywellPhase was) || was != v.Phase || docking != v.Tanker;
            if (docking != v.Tanker)
            {
                docking = v.Tanker;
                engageFrom = Mathf.Max(gap, 1f);
                ResetStop();
                order = (byte)v.Order; // the server's standing order wins when the stop begins
            }
            lastPhase[v.Tanker] = v.Phase;
            Edges(v, false);
            switch (v.Phase)
            {
                case SkywellPhase.Engage:
                    Line()?.Set(HudTone.Caution, "SKYWELL  HOLD BOX", gap < 90f ? "GLIDER " + Mathf.RoundToInt(gap) + " m" : "GLIDER INBOUND",
                        Mathf.Clamp01(1f - gap / engageFrom));
                    break;
                case SkywellPhase.Transfer:
                    PitJobLines(me, v);
                    break;
                default:
                    if (fresh)
                    {
                        FinishJob(me);
                        if ((v.Flags & PitFlags.Nothing) == 0) Notice($"SKYWELL  SERVICE COMPLETE  {v.PitTenths / 10f:0.0} s", HudTone.Info);
                        priority = -1;
                    }
                    pitLine?.Release();
                    pitLine = null;
                    Line()?.Set(HudTone.Info, (v.Flags & PitFlags.Nothing) != 0 ? "SKYWELL  NOTHING TO SERVE" : "SKYWELL  SERVICE COMPLETE",
                        v.PitTenths > 0 ? $"{v.PitTenths / 10f:0.0} s  ·  GLIDER RETURNING" : "GLIDER RETURNING", 1f);
                    break;
            }
            return true;
        }

        /// <summary>
        /// Nearest free deployed friendly kit: director-light cues into the service box. PIT STOP is available from any
        /// deployed friendly kit in cue range, busy or not, so a pilot queued behind another jet can already choose an order.
        /// </summary>
        private bool Approach(Aircraft me)
        {
            Aircraft best = null;
            float bestDist = CueRange;
            SkywellView bestView = default;
            foreach (SkywellView v in SkywellBoard.Views.Values)
            {
                if (!v.Active) continue;
                Aircraft tanker = Resolve(v.Tanker);
                if (tanker == null || tanker.disabled || tanker == me || tanker.NetworkHQ != me.NetworkHQ) continue;
                float d = Vector3.Distance(me.transform.position, SkywellBoard.Anchor(tanker));
                if (d >= CueRange) continue;
                pitAvailable = true;
                if (v.Receiver != 0 || d >= bestDist) continue;
                bestDist = d;
                best = tanker;
                bestView = v;
            }
            if (best == null) return false;
            SkywellBoard.Rel(best, me, out Vector3 rel, out float relSpeed, out float roll);
            Vector3 toCentre = rel - SkywellBox.Centre;
            if (SkywellBox.Inside(rel, relSpeed, roll, false))
            {
                float lockLeft = SkywellBoard.HotLockUntil - Time.timeSinceLevelLoad;
                if ((bestView.Flags & PitFlags.HotBreak) != 0 || lockLeft > 0f)
                    Line()?.Set(HudTone.Warning, "SKYWELL  HOT BREAK  CLEARING", lockLeft > 0f ? "RE-ENTER IN " + Mathf.CeilToInt(lockLeft) + " s" : "STAND BY", 0f);
                else if (SkywellService.NeedsService((PitOrder)order, bestView.FuelKg, bestView.StockKg, me))
                    Line()?.Set(HudTone.Warning, "SKYWELL  HOLD BOX", "STEADY  ·  ORDER " + OrderWord(), 1f);
                else
                    Line()?.Set(HudTone.Info, "SKYWELL  IN BOX", "NOTHING TO SERVE  ·  ORDER " + OrderWord(), 1f);
                return true;
            }
            Line()?.Set(HudTone.Info, "SKYWELL  TANKER  " + Mathf.RoundToInt(bestDist) + " m", Director(toCentre, relSpeed, roll),
                Mathf.Clamp01(1f - (toCentre.magnitude - SkywellBox.BehindMax) / (CueRange - SkywellBox.BehindMax)));
            return true;
        }

        /// <summary>Tanker belly-light style cue to the box centre (tanker axes: +y up, +z forward), worst axis first.</summary>
        private static string Director(Vector3 toCentre, float relSpeed, float roll)
        {
            const float zHalf = (SkywellBox.BehindMax - SkywellBox.BehindMin) * 0.5f, yHalf = (SkywellBox.Below + SkywellBox.Above) * 0.5f;
            if (toCentre.z > zHalf) return "DROP BACK  ·  AFT " + Mathf.RoundToInt(toCentre.z - zHalf) + " m";
            var cue = new List<string>(4);
            if (toCentre.y > yHalf) cue.Add("DOWN " + Mathf.RoundToInt(toCentre.y - yHalf) + " m");
            else if (toCentre.y < -yHalf) cue.Add("UP " + Mathf.RoundToInt(-toCentre.y - yHalf) + " m");
            if (toCentre.x > SkywellBox.HalfWidth) cue.Add("LEFT " + Mathf.RoundToInt(toCentre.x - SkywellBox.HalfWidth) + " m");
            else if (toCentre.x < -SkywellBox.HalfWidth) cue.Add("RIGHT " + Mathf.RoundToInt(-toCentre.x - SkywellBox.HalfWidth) + " m");
            if (toCentre.z < -zHalf) cue.Add("CLOSE " + Mathf.RoundToInt(-toCentre.z - zHalf) + " m");
            if (relSpeed > SkywellBox.MaxRelSpeed) cue.Add("MATCH SPEED");
            if (Mathf.Abs(roll) > SkywellBox.MaxRollDeg) cue.Add("LEVEL WINGS");
            return cue.Count == 0 ? "HOLD STEADY" : string.Join("  ·  ", cue);
        }

        private void Notice(string text, HudTone tone) => board?.Notice(Channel, tone, text);

        private static Aircraft Resolve(uint id) =>
            id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) ? u as Aircraft : null;
    }
}
