using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>The C-key menu's view of the OPS perks (<see cref="IOpsPerks"/>): the ready, armed and cooling ones, pressed through <see cref="CallsController.Press"/>.</summary>
    internal sealed class OpsPerkFan : IOpsPerks
    {
        private readonly SupportManager manager;
        private readonly CallsController calls;
        private readonly List<CallTile> ready = new List<CallTile>(IOpsPerks.MaxPerks + 1);
        private readonly List<string> fronts = new List<string>(IOpsPerks.MaxPerks + 1);
        private readonly List<int> cooldowns = new List<int>(IOpsPerks.MaxPerks + 1);
        private readonly List<float> totals = new List<float>(IOpsPerks.MaxPerks + 1);
        private int frame = -1;

        public OpsPerkFan(SupportManager manager, CallsController calls) { this.manager = manager; this.calls = calls; }

        private List<CallTile> Ready()
        {
            if (frame == Time.frameCount) return ready;
            frame = Time.frameCount;
            ready.Clear(); fronts.Clear(); cooldowns.Clear(); totals.Clear();
            if (manager == null || calls == null) return ready;
            foreach (CallRow row in CallSheet.Rows)
            {
                float cooldown = manager.LocalCooldownRemaining(row.Id);
                CallTile t = CallsView.Tile(row, manager.Quote(row.Id), manager.LockReason(row.Id), manager.LocalAllocation,
                    cooldown, calls.Armed == row.Id, false, !manager.Online);
                if (t.State != CallState.Ready && t.State != CallState.Armed && t.State != CallState.Cooldown) continue;
                ready.Add(t); fronts.Add(row.FrontWord); cooldowns.Add(t.State == CallState.Cooldown ? (int)Math.Ceiling(cooldown) : 0); totals.Add(PerkPower.Cooldown(CallSheet.Cooldown(row.Rung), manager.FrontMirror.Power(row.Front))); // the length the host applies: shortened while the front is ahead
            }
            while (ready.Count > IOpsPerks.MaxPerks)
            {
                int drop = cooldowns.FindLastIndex(c => c > 0);
                if (drop < 0) drop = ready.Count - 1;
                ready.RemoveAt(drop); fronts.RemoveAt(drop); cooldowns.RemoveAt(drop); totals.RemoveAt(drop);
            }
            return ready;
        }

        public int PerkCount => Ready().Count;
        public string PerkLabel(int index) => index >= 0 && index < Ready().Count ? Ready()[index].Label : "";
        public bool PerkArmed(int index) => index >= 0 && index < Ready().Count && Ready()[index].State == CallState.Armed;

        public string PerkNote(int index)
        {
            if (index < 0 || index >= Ready().Count) return "";
            CallTile t = Ready()[index];
            return t.State == CallState.Armed ? "ARMED · PRESS AGAIN" : t.RungWord + " · " + t.CostText;
        }

        public string PerkFront(int index) => index >= 0 && index < Ready().Count ? fronts[index] : "";

        public int PerkCooldown(int index) => index >= 0 && index < Ready().Count ? cooldowns[index] : 0;

        /// <summary>The whole cooldown of the perk in seconds (its rung's, divided by the front's power while it is ahead: the perk ring's denominator on the TAC BOARD).</summary>
        public float PerkTotal(int index) => index >= 0 && index < Ready().Count ? totals[index] : 0f;

        public void PerkPress(int index)
        {
            if (index >= 0 && index < Ready().Count) calls.Press(Ready()[index].Id);
        }
    }
}
