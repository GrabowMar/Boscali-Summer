using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// A <see cref="ScrollRect"/> whose wheel steps glide instead of jump. The base class still does the clamping: each notch
    /// is applied from the pending target, the clamped result becomes the new target, and LateUpdate eases the content there.
    /// A drag, a scrollbar pull or any code that moves the content cancels the glide.
    /// </summary>
    public sealed class AvSmoothScroll : ScrollRect
    {
        /// <summary>Pixels per wheel notch; one notch is roughly three rows of kit text.</summary>
        public const float Step = 56f;

        // Exponential approach rate: ~90% of a step lands in 0.05 s.
        private const float Sharpness = 22f * AvFxPacking.AnimationSpeed;

        private Vector2 target, written;
        private bool gliding;

        public override void OnScroll(PointerEventData data)
        {
            if (!IsActive() || content == null) return;
            Vector2 from = content.anchoredPosition;
            if (gliding) { content.anchoredPosition = target; UpdateBounds(); }   // fast flicks stack
            base.OnScroll(data);
            target = content.anchoredPosition;
            content.anchoredPosition = from; UpdateBounds();
            StopMovement();
            written = from;
            gliding = target != from;
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            gliding = false;
            base.OnBeginDrag(eventData);
        }

        protected override void LateUpdate()
        {
            if (gliding && content != null)
            {
                if (content.anchoredPosition != written) gliding = false;   // a scrollbar, a jump-to-top or a relayout moved it
                else
                {
                    Vector2 p = Vector2.Lerp(written, target, 1f - Mathf.Exp(-Sharpness * Time.unscaledDeltaTime));
                    if ((p - target).sqrMagnitude < 0.25f) { p = target; gliding = false; }
                    SetContentAnchoredPosition(p);
                    written = content.anchoredPosition;
                }
            }
            base.LateUpdate();
        }

        protected override void OnDisable()
        {
            gliding = false;
            base.OnDisable();
        }
    }
}
