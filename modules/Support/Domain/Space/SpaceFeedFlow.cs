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

    /// <summary>
    /// Who owns the mouse while the full-screen feed is open, as a pure state machine the runtime applies to Rewired. The mouse is
    /// disabled only when it was found enabled and is handed back to exactly that state, and only once its buttons are up, so the
    /// closing click cannot fire a weapon. The keyboard and joysticks are never touched: flight and weapon bindings stay live in the air.
    /// </summary>
    internal sealed class FeedInputLease
    {
        private bool mouseTouched, mouseWas, pending;

        public bool Held { get; private set; }
        /// <summary>The enabled state to give the mouse back; valid after Release/Tick/ForceRelease returned true.</summary>
        public bool RestoreMouseTo => mouseWas;

        /// <summary>True when the caller must now disable the mouse.</summary>
        public bool Acquire(bool mouseAvailable, bool mouseEnabled)
        {
            Held = true;
            if (pending) { pending = false; return false; } // reopened before the buttons were up: still ours, still disabled
            if (mouseTouched) return false;
            if (!mouseAvailable || !mouseEnabled) return false;
            mouseTouched = true;
            mouseWas = true;
            return true;
        }

        /// <summary>True when the caller must now re-enable the mouse; false either means nothing to give back or "wait".</summary>
        public bool Release(bool buttonsDown)
        {
            Held = false;
            if (!mouseTouched) return false;
            if (buttonsDown) { pending = true; return false; }
            return Finish();
        }

        public bool Tick(bool buttonsDown) => pending && !buttonsDown && Finish();

        /// <summary>Teardown, reset or exception: hand the mouse back now, buttons or not.</summary>
        public bool ForceRelease()
        {
            Held = false;
            return mouseTouched && Finish();
        }

        private bool Finish()
        {
            mouseTouched = false;
            pending = false;
            return true;
        }
    }
}
