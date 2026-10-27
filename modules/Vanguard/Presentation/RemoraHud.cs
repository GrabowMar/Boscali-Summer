using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// The launcher's REMORA line on channel vg.remora (4 Hz): "ARCHER 2/2 READY", "ARCHER ENGAGING 1/2",
    /// "REMORA GUIDED BOMB — INBOUND", plus "ARCHER AWAY" notices. The designation picks the role: air hunts, ground bombs. Also drops dead drones from RemoraBoard on every peer.
    /// </summary>
    internal sealed class RemoraHud : MonoBehaviour, ISceneService
    {
        private const string Owner = "vanguard.remora";
        private const string Channel = "vg.remora";
        private const float Interval = 0.25f;

        private readonly Dictionary<uint, RemoraView> seen = new Dictionary<uint, RemoraView>();
        private readonly List<uint> gone = new List<uint>();
        private IHudBoard board;
        private IHudLine line;
        private float next;

        public void ResetForScene()
        {
            seen.Clear();
            Drop();
        }

        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Interval;
            Prune();
            if (board == null && !ModuleServices.TryGet(out board)) return;
            VanguardHudFeeds.DeclareChannel(Channel, "REMORA");
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled)
            {
                Drop();
                return;
            }
            int ready = 0, capacity = 0, flares = 0, mantas = 0, remoras = 0;
            RemoraPhase worst = RemoraPhase.Escort;
            foreach (RemoraView view in RemoraBoard.Views.Values)
            {
                if (view.Launcher != me.persistentID.Id) continue;
                int left = RemoraBoard.Left(view);
                ready += left;
                capacity += RemoraBoard.Capacity(view.Drone);
                if (RemoraBoard.IsManta(view.Drone)) mantas++; else remoras++;
                flares += view.Flares;
                if (view.Phase > worst) worst = view.Phase;
                if (seen.TryGetValue(view.Drone, out RemoraView before))
                {
                    bool big = RemoraBoard.IsManta(view.Drone);
                    if (view.Jams < before.Jams) board.Notice(Channel, HudTone.Caution, "REMORA ECM BURST", view.Jams + " LEFT ON THIS DRONE");
                    if (view.Fired != before.Fired) board.Notice(Channel, HudTone.Info, big ? "MANTA ROUND AWAY" : "ARCHER AWAY", left + " LEFT ON THIS DRONE");
                    if (view.Phase == RemoraPhase.Ram && before.Phase != RemoraPhase.Ram)
                        board.Notice(Channel, HudTone.Warning, big ? "MANTA GUIDED BOMB — INBOUND" : "REMORA RAMMING — INBOUND");
                }
                seen[view.Drone] = view;
            }
            if (capacity == 0)
            {
                Drop();
                return;
            }
            line ??= VanguardHudFeeds.Acquire(board, Owner, Channel, "archer");
            HudTone tone = worst == RemoraPhase.Ram ? HudTone.Warning : worst == RemoraPhase.Engage ? HudTone.Caution : HudTone.Info;
            // REMORA hunts the air, MANTA strikes the ground (2026-10-27).
            string detail = worst == RemoraPhase.Ram ? "WARHEAD ARMED — RAMMING"
                : worst == RemoraPhase.Engage ? (mantas > 0 && remoras == 0 ? "AGM STRIKE" : "AIR HUNT")
                : ready > 0 ? (mantas == 0 ? "HUNTING AIR" : remoras == 0 ? "STRIKING GROUND" : "AIR + GROUND") + " - FLARES " + flares : "DESIGNATE TO RAM";
            // Only MANTAs airborne: their own words; any REMORA among them keeps the shared ARCHER line.
            string status = remoras == 0 ? MantaRules.Status(ready, capacity, worst) : RemoraArcher.Status(ready, capacity, worst);
            line?.Set(tone, status, detail, ready / (float)capacity);
        }

        // Board entries outlive their drone; any peer drops ids that no longer resolve to a live missile.
        private void Prune()
        {
            gone.Clear();
            foreach (uint id in RemoraBoard.Views.Keys)
                if (!UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) || unit == null || unit.disabled) gone.Add(id);
            foreach (uint id in gone)
            {
                RemoraBoard.Views.Remove(id);
                seen.Remove(id);
            }
        }

        private void Drop()
        {
            line?.Release();
            line = null;
        }
    }
}
