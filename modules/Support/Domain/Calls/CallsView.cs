using System;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    internal enum CallState : byte { Ready, Armed, Pending, Cooldown, Locked, LowCredit, Offline }

    internal readonly struct CallTile
    {
        public readonly SupportActionId Id;
        public readonly string Label, TierWord, CostText, Reason, StateWord;
        public readonly CallState State;
        public readonly bool Enabled;

        public CallTile(SupportActionId id, string label, string tierWord, string costText, string reason,
            CallState state, string stateWord, bool enabled)
        {
            Id = id;
            Label = label;
            TierWord = tierWord;
            CostText = costText;
            Reason = reason;
            State = state;
            StateWord = stateWord;
            Enabled = enabled;
        }
    }

    /// <summary>What one CALL tile shows. State priority: offline > pending > armed > locked > cooldown > credit > ready.</summary>
    internal static class CallsView
    {
        public static CallTile Tile(in CallRow row, in CallQuote quote, bool unlocked, string unlockText, float balance,
            float cooldownLeft, bool armed, bool pending, bool offline)
        {
            CallState state;
            string word;
            if (offline) { state = CallState.Offline; word = "OFFLINE"; }
            else if (pending) { state = CallState.Pending; word = "PENDING"; }
            else if (armed) { state = CallState.Armed; word = "ARMED — PRESS AGAIN"; }
            else if (!unlocked) { state = CallState.Locked; word = string.IsNullOrEmpty(unlockText) ? "LOCKED" : unlockText; }
            else if (cooldownLeft > 0.05f) { state = CallState.Cooldown; word = (int)Math.Ceiling(cooldownLeft) + "s"; }
            else if (balance + 0.001f < quote.Cost) { state = CallState.LowCredit; word = "NEED " + quote.Cost + " CR"; }
            else { state = CallState.Ready; word = "READY"; }

            return new CallTile(row.Id, row.Label, CallWords.TierWord(row.Tier), quote.Cost + " CR", quote.Reason ?? "",
                state, word, state == CallState.Ready || state == CallState.Armed);
        }
    }
}
