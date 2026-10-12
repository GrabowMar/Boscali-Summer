using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.DynamicOperations.Domain;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.DynamicOperations.Runtime
{
    // SEND WING (spec 04 W4): the host's order for a card, and the two hooks the wing's tasking has on the contract tick. The staff never sends the wing; only a press does.
    internal sealed partial class OperationsManager
    {
        private IWingSortieOrders wingOrders;

        private IWingSortieOrders Wing => wingOrders ?? (ModuleServices.TryGet(out IWingSortieOrders found) ? wingOrders = found : null);

        public bool TryGetWingOrder(int sortieId, out WingSortieOrder order)
        {
            order = default;
            if (!GameAccess.IsServer() || settings?.Enabled.Value != true || !MissionManager.IsRunning ||
                !ReferenceEquals(missionIdentity, MissionManager.CurrentMission)) return false;
            if (!GameManager.GetLocalPlayer<Player>(out Player local) || local?.HQ == null || !boards.TryGetValue(local.HQ, out FactionBoard board)) return false;
            foreach (Target target in board.Targets)
            {
                Operation op = target.Mission;
                if (op.Id != sortieId) continue;
                if (!op.IsLive) return false;
                WingSortieIntent intent = OperationWing.IntentFor(op.Kind, out string why);
                if (op.Returning) { intent = WingSortieIntent.None; why = "Landing is yours: fly the return leg yourself."; }
                bool marker = MarkerPosition(local.HQ, target) && Finite(target.Position);
                uint id = intent == WingSortieIntent.Destroy && target.Unit != null && !target.Unit.disabled ? target.Unit.persistentID.Id : 0u;
                float left = Mathf.Clamp(op.Deadline - NetworkSceneSingleton<MissionManager>.i.MissionTime, 0f, 1200f);
                order = new WingSortieOrder(op.Id, intent, op.State == OperationState.Active,
                    marker ? target.Position.x : float.NaN, marker ? target.Position.z : float.NaN, id, left, why);
                return true;
            }
            return false;
        }

        /// <summary>Hold credit (Defend, Patrol only): the pilot's press has the wing on station. Recon kinds still need the pilot.</summary>
        private bool WingOnStation(Operation op) =>
            (op.Kind == OperationKind.Defend || op.Kind == OperationKind.Patrol) && Wing != null &&
            Wing.TryGetStatus(op.Id, out WingSortieStatus s) && s.State == WingSortieState.OnStation;

        /// <summary>The pilot's press had the wing on this card when it finished (tasked, on station, or its target down).</summary>
        private bool WingTasked(int id) => Wing != null && Wing.TryGetStatus(id, out WingSortieStatus s) &&
            (s.State == WingSortieState.Tasked || s.State == WingSortieState.OnStation || s.State == WingSortieState.Done);

        // Never lets the wing break the contract tick.
        private void ReleaseWing(int id)
        {
            try { Wing?.Release(id); }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Operations] Wing release failed: " + e.Message); }
        }
    }
}
