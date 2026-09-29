using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>
    /// A tiny reveal driver: an alpha fade and a scale punch, each running on a fixed
    /// number of slots (no <c>List</c> growth, no coroutines). Idle costs nothing —
    /// <see cref="Behaviour.enabled"/> is off whenever nothing is running, so Unity never
    /// calls <see cref="Update"/>.
    /// </summary>
    public sealed class AvReveal : MonoBehaviour
    {
        /// <summary>Set by the consumer (reduced motion / low FX / pause): every call applies its end value immediately.</summary>
        public static bool Snap;

        public const int MaxConcurrent = 2;

        private struct Slot
        {
            public bool Active;
            public bool IsPunch;
            public CanvasGroup Group;
            public RectTransform Target;
            public float From;
            public float To;
            public float Elapsed;
            public float Duration;
        }

        private readonly Slot[] slots = new Slot[MaxConcurrent];

        /// <summary>Fades <paramref name="group"/>'s alpha to <paramref name="to"/>, easing out on unscaled time.</summary>
        public void Fade(CanvasGroup group, float to, float seconds)
        {
            if (group == null) return;

            if (Snap || seconds <= 0f)
            {
                CancelFade(group);
                group.alpha = to;
                return;
            }

            int index = FindFadeSlot(group);
            if (index < 0)
            {
                group.alpha = to;
                return;
            }

            slots[index].Active = true;
            slots[index].IsPunch = false;
            slots[index].Group = group;
            slots[index].Target = null;
            slots[index].From = group.alpha;
            slots[index].To = to;
            slots[index].Elapsed = 0f;
            slots[index].Duration = seconds;
            enabled = true;
        }

        /// <summary>Scales <paramref name="target"/> from <paramref name="fromScale"/> to 1, easing out on unscaled time.</summary>
        public void Punch(RectTransform target, float fromScale, float seconds)
        {
            if (target == null) return;

            if (Snap || seconds <= 0f)
            {
                CancelPunch(target);
                target.localScale = Vector3.one;
                return;
            }

            int index = FindPunchSlot(target);
            if (index < 0)
            {
                target.localScale = Vector3.one;
                return;
            }

            slots[index].Active = true;
            slots[index].IsPunch = true;
            slots[index].Group = null;
            slots[index].Target = target;
            slots[index].From = fromScale;
            slots[index].To = 1f;
            slots[index].Elapsed = 0f;
            slots[index].Duration = seconds;
            enabled = true;
        }

        /// <summary>Snaps every running slot to its end value.</summary>
        public void Finish()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Active) continue;

                if (slots[i].IsPunch)
                {
                    if (slots[i].Target != null)
                        slots[i].Target.localScale = new Vector3(slots[i].To, slots[i].To, slots[i].To);
                }
                else if (slots[i].Group != null)
                {
                    slots[i].Group.alpha = slots[i].To;
                }

                slots[i].Active = false;
            }
            enabled = false;
        }

        private void Update()
        {
            bool anyActive = false;

            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Active) continue;

                if (slots[i].IsPunch ? slots[i].Target == null : slots[i].Group == null)
                {
                    // The target was destroyed under us; drop the slot rather than fault.
                    slots[i].Active = false;
                    continue;
                }

                slots[i].Elapsed += Time.unscaledDeltaTime;
                float t = slots[i].Duration <= 0f ? 1f : Mathf.Clamp01(slots[i].Elapsed / slots[i].Duration);
                float eased = 1f - (1f - t) * (1f - t);

                if (slots[i].IsPunch)
                {
                    float scale = Mathf.LerpUnclamped(slots[i].From, slots[i].To, eased);
                    slots[i].Target.localScale = new Vector3(scale, scale, scale);
                }
                else
                {
                    slots[i].Group.alpha = Mathf.LerpUnclamped(slots[i].From, slots[i].To, eased);
                }

                if (t >= 1f) slots[i].Active = false;
                else anyActive = true;
            }

            if (!anyActive) enabled = false;
        }

        private int FindFadeSlot(CanvasGroup group)
        {
            int free = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Active && !slots[i].IsPunch && slots[i].Group == group) return i;
                if (free < 0 && !slots[i].Active) free = i;
            }
            return free;
        }

        private int FindPunchSlot(RectTransform target)
        {
            int free = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Active && slots[i].IsPunch && slots[i].Target == target) return i;
                if (free < 0 && !slots[i].Active) free = i;
            }
            return free;
        }

        private void CancelFade(CanvasGroup group)
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Active && !slots[i].IsPunch && slots[i].Group == group) slots[i].Active = false;
        }

        private void CancelPunch(RectTransform target)
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Active && slots[i].IsPunch && slots[i].Target == target) slots[i].Active = false;
        }
    }
}
