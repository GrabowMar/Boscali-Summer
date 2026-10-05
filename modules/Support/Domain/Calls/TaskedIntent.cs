namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    /// <summary>
    /// The armed intent of a TASKED claim: the exact post id plus the mirror and call generation it was armed under. A second
    /// press fires only on the identical triple inside the arm window, so switching posts, a refreshed mirror, a scene reset or
    /// a disarm can never fire a stale post. Mission time only.
    /// </summary>
    internal sealed class TaskedIntent
    {
        private int postId, mirrorGeneration, callGeneration;
        private float armedAt;

        /// <summary>The armed post, 0 when nothing is armed.</summary>
        public int PostId => postId;

        public ArmStep Press(int post, int mirrorGen, int callGen, float now)
        {
            if (post <= 0) { Clear(); return ArmStep.Armed; }
            if (postId == post && mirrorGeneration == mirrorGen && callGeneration == callGen && now - armedAt <= ArmState.ArmSeconds)
            {
                Clear();
                return ArmStep.Fire;
            }
            postId = post; mirrorGeneration = mirrorGen; callGeneration = callGen; armedAt = now;
            return ArmStep.Armed;
        }

        /// <summary>True when the arm window passed and the intent was cleared.</summary>
        public bool Tick(float now)
        {
            if (postId == 0 || now - armedAt <= ArmState.ArmSeconds) return false;
            Clear();
            return true;
        }

        /// <summary>True when a generation moved under the armed post and the intent was cleared.</summary>
        public bool Revalidate(int mirrorGen, int callGen)
        {
            if (postId == 0 || (mirrorGeneration == mirrorGen && callGeneration == callGen)) return false;
            Clear();
            return true;
        }

        public void Clear() { postId = 0; mirrorGeneration = callGeneration = 0; }
    }
}
