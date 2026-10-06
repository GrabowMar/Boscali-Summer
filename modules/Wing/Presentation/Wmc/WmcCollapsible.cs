using NOAvionics;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Wraps a flow part that is sometimes not there (a plan lane nobody flies, the step editor with no step picked, the run
    /// buttons while no plan runs): hidden, it takes no room in the flow line. <see cref="Set"/> says whether the state changed, so the
    /// caller relayouts once.</summary>
    internal sealed class WmcCollapsible : AvPart
    {
        private readonly AvPart inner;

        public WmcCollapsible(AvPart part)
        {
            inner = part;
            Rect = part.Rect;
        }

        // Visibility is the base AvPart.Shown (GameObject active); the flow reads it
        // through the base type, so a second field here could only ever disagree.

        /// <summary>Shows or hides the part; true when that changed it.</summary>
        public bool Set(bool on)
        {
            if (on == Shown) return false;
            Rect.gameObject.SetActive(on);
            return true;
        }

        public override float Measure(float width) => Shown ? inner.Measure(width) : 0f;

        public override void Place(AvSlot slot)
        {
            if (Shown) inner.Place(slot);
        }

        public override void Restyle() => inner.Restyle();
    }
}
