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
    }
}
