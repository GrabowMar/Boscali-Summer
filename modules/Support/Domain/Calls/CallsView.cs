using System;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    internal enum CallState : byte { Ready, Armed, Pending, Cooldown, Locked, LowCredit, Offline }

    internal readonly struct CallTile
    {
        public readonly SupportActionId Id;
        public readonly string Label, RungWord, CostText, Reason, StateWord;
        public readonly CallState State;
        public readonly bool Enabled;

        public CallTile(SupportActionId id, string label, string rungWord, string costText, string reason,
            CallState state, string stateWord, bool enabled)
        {
            Id = id;
            Label = label;
            RungWord = rungWord;
            CostText = costText;
            Reason = reason;
            State = state;
            StateWord = stateWord;
            Enabled = enabled;
        }
    }

    /// <summary>What one perk tile shows. State priority: offline > pending > armed > locked > cooldown > allocation > ready.</summary>
    internal static class CallsView
    {
        /// <param name="lockedReason">Empty when nothing blocks the perk, else the words of the gate (e.g. <c>NEEDS STRIKE QUALIFICATION</c>).</param>
        public static CallTile Tile(in CallRow row, in CallQuote quote, string lockedReason, float balance,
            float cooldownLeft, bool armed, bool pending, bool offline)
        {
            CallState state;
            string word;
            if (offline) { state = CallState.Offline; word = "OFFLINE"; }
            else if (pending) { state = CallState.Pending; word = "PENDING"; }
            else if (armed) { state = CallState.Armed; word = "ARMED — PRESS AGAIN"; }
            else if (!string.IsNullOrEmpty(lockedReason)) { state = CallState.Locked; word = lockedReason; }
            else if (cooldownLeft > 0.05f) { state = CallState.Cooldown; word = (int)Math.Ceiling(cooldownLeft) + "s"; }
            else if (balance + 0.001f < quote.Cost) { state = CallState.LowCredit; word = "NEED " + quote.Cost + " ALLOC"; }
            else { state = CallState.Ready; word = "READY"; }

            return new CallTile(row.Id, row.Label, row.RungWord, quote.Cost + " ALLOC", quote.Reason ?? "",
                state, word, state == CallState.Ready || state == CallState.Armed);
        }
    }
}
