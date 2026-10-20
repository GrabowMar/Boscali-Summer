namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>What the player's TASKED claim is doing. Only a physical launch receipt is Launched (SHOT).</summary>
    internal enum TaskedReceiptState : byte { Pending, Launching, Launched, Refused }

    internal static class TaskedReceipts
    {
        /// <param name="postLaunching">The mirrored post is held by a launching claim (this pilot's, as far as the card can tell).</param>
        public static TaskedReceiptState State(TaskedOutcome outcome, bool postLaunching)
        {
            switch (outcome)
            {
                case TaskedOutcome.Queued: return postLaunching ? TaskedReceiptState.Launching : TaskedReceiptState.Pending;
                case TaskedOutcome.Fired: return TaskedReceiptState.Launched;
                default: return TaskedReceiptState.Refused;
            }
        }

        /// <summary>The words for a receipt: PENDING and LAUNCHING are not SHOT, a refusal carries the host's own words.</summary>
        public static string Words(TaskedReceiptState state, TaskedOutcome outcome, int detail)
        {
            switch (state)
            {
                case TaskedReceiptState.Pending: return "PENDING · " + TaskedWords.Of(TaskedOutcome.Queued);
                case TaskedReceiptState.Launching: return "LAUNCHING · STAND BY";
                case TaskedReceiptState.Launched: return "SHOT · " + TaskedWords.Of(TaskedOutcome.Fired);
                default: return TaskedWords.Of(outcome, detail);
            }
        }
    }
}
