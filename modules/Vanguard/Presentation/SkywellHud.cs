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
    /// SKYWELL on the common HUD element and the radial menu. A receiver pilot is walked into the service zone with director cues
    /// (UP / DOWN, LEFT / RIGHT, CLOSE / DROP BACK, SLOW / FAST, like a tanker's belly lights), then HOLD STEADY while the tender comes
    /// under the belly, the pit stop job by job with its service bars (skywellHudPit.cs), SERVICE COMPLETE and, if the zone is left
    /// early, ZONE LOST; a tanker pilot sees deploy state, the tether-speed warning, receiver state and stock.
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
        private uint serving;           // tanker whose tender serves the local aircraft
        private float engageFrom = 1f;  // tender's distance to its pose when it was sent (bar)

        public void ResetForScene()
        {
            line?.Release();
            line = null;
            pitLine?.Release();
            pitLine = null;
            lastFlags.Clear();
            serving = 0;
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
                    ? $"FUEL {v.FuelKg / 1000f:0.0}T  {PitCopy.Rounds(v.Rounds)}"
                    : $"FUEL {SkywellService.FuelStockKg / 1000f:0.0}T  {PitCopy.Rounds(SkywellMagazine.Rounds)}";
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
            VanguardHudFeeds.DeclareChannel(Channel, "SKYWELL");
            bool shown = false;
            pitAvailable = false;
            if (Local(out Aircraft me))
            {
                if (me != orderOwner) NewAircraft(me);
                if (SkywellBoard.KitStation(me) != null) shown = Tanker(me);
                else
                {
                    shown = Receiver(me); // sets pitAvailable: served, or any deployed friendly kit within cue range
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

        private IHudLine Line() => line ??= VanguardHudFeeds.Acquire(board, Owner, Channel, "status");

        private IHudLine PitLine() => pitLine ??= VanguardHudFeeds.Acquire(board, Owner, Channel, "pit");

        private bool Tanker(Aircraft me)
        {
            if (!SkywellBoard.Views.TryGetValue(me.persistentID.Id, out SkywellView v)) return false;
            Edges(v, true, me);
            if (!v.Active) return false;
            string stock = $"FUEL {v.FuelKg / 1000f:0.0} t  ·  {PitCopy.Rounds(v.Rounds)}";
            float speed = me.rb != null ? me.rb.velocity.magnitude : 0f;
            if ((v.Flags & PitFlags.AutoReel) != 0)
                Line()?.Set(HudTone.Warning, "SKYWELL  TETHER REELING IN", stock, 0f);
            else if (v.State == TenderState.Deploying || v.State == TenderState.Stowing)
                Line()?.Set(HudTone.Caution, v.State == TenderState.Deploying ? "SKYWELL  DEPLOYING" : "SKYWELL  STOWING", stock,
                    Mathf.Clamp01(v.StateAge / SkywellTender.DeploySeconds));
            else if ((v.Flags & PitFlags.OverSpeed) != 0)
                Line()?.Set(HudTone.Warning, "SKYWELL  TETHER LIMIT  " + Mathf.RoundToInt(speed) + " M/S",
                    "SLOW TO " + Mathf.RoundToInt(SkywellZone.MaxTankerSpeed) + "  ·  REELS IN ABOVE " + Mathf.RoundToInt(SkywellZone.AutoReelSpeed), 0f);
            else if (v.Receiver == 0)
                Line()?.Set(HudTone.Info, (v.Flags & PitFlags.ZoneLost) != 0 ? "SKYWELL  ZONE LOST" : v.State == TenderState.Return ? "SKYWELL  TENDER RETURNING" : "SKYWELL  DEPLOYED  ·  AWAITING RECEIVER", stock, 0f);
            else if (v.State == TenderState.Service)
            {
                Aircraft receiver = Resolve(v.Receiver);
                string who = receiver != null ? receiver.unitName : "RECEIVER";
                Line()?.Set(HudTone.Caution, "SKYWELL  SERVING  " + who,
                    $"JOB {v.JobIndex + 1}/{v.JobCount}  ·  {(v.Job == PitJobKind.Fuel ? "FUEL" : "STN " + (v.JobStation + 1))}  ·  {stock}",
                    TankerJobProgress(v));
            }
            else
                Line()?.Set(HudTone.Caution, v.State == TenderState.Approach ? "SKYWELL  TENDER TO RECEIVER" : "SKYWELL  TENDER ON STATION", stock, 0f);
            return true;
        }

        private bool Receiver(Aircraft me)
        {
            uint id = me.persistentID.Id;
            foreach (SkywellView v in SkywellBoard.Views.Values)
                if (v.Receiver == id && SkywellTender.Serving(v.State)) return pitAvailable = Served(me, v);
            if (serving != 0)
            {
                // The kit publishes who it let go and why, so a next receiver started within the same second cannot take this notice away.
                SkywellBoard.Views.TryGetValue(serving, out SkywellView gone);
                byte why = gone.Released == id ? gone.ReleaseKind : PitRelease.None;
                if (why == PitRelease.HotBreak)
                {
                    Notice("SKYWELL  HOT BREAK  MISSILE INBOUND", HudTone.Warning);
                    // The server refuses this jet for a while: show it.
                    SkywellBoard.HotLockUntil = Time.timeSinceLevelLoad + SkywellService.HotBreakCooldown;
                }
                else if (why == PitRelease.ZoneLost) Notice("SKYWELL  ZONE LOST", HudTone.Warning);
                else if (why == PitRelease.Served)
                {
                    FinishJob(me);
                    if ((gone.Flags & PitFlags.Nothing) == 0) Notice($"SKYWELL  SERVICE COMPLETE  {gone.PitTenths / 10f:0.0} s", HudTone.Info);
                }
                else Notice("SKYWELL  CONTACT LOST", HudTone.Info);
                lastFlags.Remove(serving);
                serving = 0;
                priority = -1; // one-shot
                pitLine?.Release();
                pitLine = null;
            }
            return Approach(me);
        }

        private bool Served(Aircraft me, SkywellView v)
        {
            Aircraft tanker = Resolve(v.Tanker);
            if (tanker == null) return false;
            float gap = SkywellVisuals.TenderGap(v.Tanker);
            if (serving != v.Tanker)
            {
                serving = v.Tanker;
                engageFrom = Mathf.Max(gap, 1f);
                ResetStop();
                order = (byte)v.Order; // the server's standing order wins when the stop begins
            }
            Edges(v, false, me);
            SkywellBoard.Rel(tanker, me, out Vector3 rel, out float relSpeed, out float relForward, out float roll);
            if (!SkywellZone.Contains(rel, relSpeed, roll))
            {
                // The tender lets go after 2 s outside: tell the pilot the way back in.
                Line()?.Set(HudTone.Warning, "SKYWELL  LEAVING ZONE", SkywellZone.Director(rel, relSpeed, relForward, roll), 0f);
                if (v.State != TenderState.Service) pitLine?.Release();
                return true;
            }
            switch (v.State)
            {
                case TenderState.Approach:
                    Line()?.Set(HudTone.Caution, "SKYWELL  HOLD STEADY", gap < 90f ? "TENDER " + Mathf.RoundToInt(gap) + " m" : "TENDER INBOUND",
                        Mathf.Clamp01(1f - gap / engageFrom));
                    break;
                case TenderState.Station:
                    Line()?.Set(HudTone.Caution, "SKYWELL  TENDER ON STATION", "HOLD STEADY", 1f);
                    break;
                default:
                    PitJobLines(me, v);
                    break;
            }
            return true;
        }

        /// <summary>
        /// Nearest free deployed friendly kit: director cues into the service zone. PIT STOP is available from any deployed
        /// friendly kit in cue range, busy or not, so a pilot queued behind another jet can already choose an order.
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
                if (!SkywellTender.Flying(v.State) || v.Receiver != 0 || d >= bestDist) continue;
                bestDist = d;
                best = tanker;
                bestView = v;
            }
            if (best == null) return false;
            SkywellBoard.Rel(best, me, out Vector3 rel, out float relSpeed, out float relForward, out float roll);
            if (SkywellZone.Contains(rel, relSpeed, roll))
            {
                float lockLeft = SkywellBoard.HotLockUntil - Time.timeSinceLevelLoad;
                float waitLeft = bestView.CooldownLeft(me.persistentID.Id); // the server's own cooldown on this jet (served, zone lost, timeout)
                bool needs = SkywellService.NeedsService((PitOrder)order, bestView.FuelKg, bestView.Rounds, me);
                if ((bestView.Flags & PitFlags.HotBreak) != 0 || lockLeft > 0f)
                    Line()?.Set(HudTone.Warning, "SKYWELL  HOT BREAK  CLEARING", lockLeft > 0f ? "RE-ENTER IN " + Mathf.CeilToInt(lockLeft) + " s" : "STAND BY", 0f);
                else if (needs && waitLeft > 0.5f)
                    Line()?.Set(HudTone.Caution, "SKYWELL  SERVICE IN " + Mathf.CeilToInt(waitLeft) + " s", "STAND BY  ·  ORDER " + OrderWord(), 0f);
                else if (needs)
                    Line()?.Set(HudTone.Warning, "SKYWELL  HOLD STEADY", "IN ZONE  ·  ORDER " + OrderWord(), 1f);
                else
                    Line()?.Set(HudTone.Info, "SKYWELL  IN ZONE", "NOTHING TO SERVE  ·  ORDER " + OrderWord(), 1f);
                return true;
            }
            Line()?.Set(HudTone.Info, "SKYWELL  TANKER  " + Mathf.RoundToInt(bestDist) + " m", SkywellZone.Director(rel, relSpeed, relForward, roll),
                Mathf.Clamp01(1f - ((rel - SkywellZone.Tow).magnitude - SkywellZone.Radius) / (CueRange - SkywellZone.Radius)));
            return true;
        }

        private void Notice(string text, HudTone tone) => board?.Notice(Channel, tone, text);

        private static Aircraft Resolve(uint id) =>
            id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) ? u as Aircraft : null;
    }
}
