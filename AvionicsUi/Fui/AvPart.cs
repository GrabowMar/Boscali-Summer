using NOAvionics;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>A kit v2 building block: measurable at a width, placeable in a slot, restylable on theme change.</summary>
    public abstract class AvPart
    {
        public RectTransform Rect { get; protected set; }
        public virtual float Measure(float width) => Rect != null ? Rect.rect.height : 0f;
        public virtual void Place(AvSlot slot) { if (Rect != null) AvLay.Place(Rect, slot); }
        public virtual void Restyle() { }

        /// <summary>The flow this part sits in (set when it is added to one).</summary>
        internal AvFlow Owner;

        /// <summary>A part nested inside another part (a list's rows): changes bubble to it instead of a flow.</summary>
        internal AvPart Parent;

        /// <summary>What the owning flow last gave this part; a later height or visibility difference re-lays the page.</summary>
        internal float PlacedWidth = -1f, PlacedHeight = -1f;
        internal bool PlacedShown = true;

        /// <summary>A part counts as shown while its GameObject is active; hidden parts collapse out of the flow.</summary>
        public bool Shown => Rect == null || Rect.gameObject.activeSelf;

        /// <summary>
        /// Call after changing anything that can change this part's height (text, rows, visibility). The owning
        /// flow re-measures the part on its next fast tick and re-lays the page only if the height really moved.
        /// Kit parts call it from their own setters; custom parts that write text directly should call it too
        /// (the flow's slow sweep catches the ones that do not, within half a second).
        /// </summary>
        public void Changed() { if (Owner != null) Owner.MarkChanged(this); else Parent?.Changed(); }

        /// <summary>Shows or hides the part and tells the flow, so the gap closes (or opens) on the next tick.</summary>
        public void SetShown(bool shown)
        {
            if (Rect == null || Rect.gameObject.activeSelf == shown) return;
            Rect.gameObject.SetActive(shown);
            Changed();
        }
    }
}
