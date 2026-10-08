using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>The C-key menu's view of the OPS perks (<see cref="IOpsPerks"/>): the ready ones, pressed through <see cref="CallsController.Press"/>.</summary>
    internal sealed class OpsPerkFan : IOpsPerks
    {
        private readonly SupportManager manager;
        private readonly CallsController calls;
        private readonly List<CallTile> ready = new List<CallTile>(IOpsPerks.MaxPerks);
        private int frame = -1;

        public OpsPerkFan(SupportManager manager, CallsController calls) { this.manager = manager; this.calls = calls; }

        private List<CallTile> Ready()
        {
            if (frame == Time.frameCount) return ready;
            frame = Time.frameCount;
            ready.Clear();
            if (manager == null || calls == null) return ready;
            foreach (CallRow row in CallSheet.Rows)
            {
                if (ready.Count >= IOpsPerks.MaxPerks) break;
                CallTile t = CallsView.Tile(row, manager.Quote(row.Id), manager.LockReason(row.Id), manager.LocalAllocation,
                    manager.LocalCooldownRemaining(row.Id), calls.Armed == row.Id, false, !manager.Online);
                if (t.State == CallState.Ready || t.State == CallState.Armed) ready.Add(t);
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

        public void PerkPress(int index)
        {
            if (index >= 0 && index < Ready().Count) calls.Press(Ready()[index].Id);
        }
    }
}
