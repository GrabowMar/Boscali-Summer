using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The game's own grey message boxes the CUE stack stands in for, one per <see cref="CueSource"/>: the action report (<c>AircraftActionsReport</c>: its background Image and its text), the reserve notice (<c>ReserveReport</c>'s object),
    /// the funds line (<c>AllocationDisplay</c>'s object) and the score line (<c>KillDisplay.killText</c>). Reads only, except for the one thing it does while the mod's own cues are drawn: a <see cref="CanvasGroup"/> it OWNS on each
    /// object (a group the object already had is reused and its alpha put back) at alpha 0. Never <c>SetActive</c> on a native object (the game switches its own boxes on and off), never a disabled script, Image or text, the game's own
    /// text and timers untouched: the game keeps writing into objects that are only not seen. <see cref="Hide"/> is told every frame whether the mod is standing in (fail open: the box comes back the very frame it is not);
    /// <see cref="Dispose"/> takes our groups off and puts the alphas back. A box that is not found is not held (<see cref="Held"/> false) and its cues are not drawn; one the game destroyed (a new aircraft has a new action report) is
    /// let go and found again.
    /// </summary>
    internal sealed class NativeCues
    {
        private static readonly AccessTools.FieldRef<AircraftActionsReport, TextMeshProUGUI> ActionTextRef = NativeRefs.Field<AircraftActionsReport, TextMeshProUGUI>("actionsText");
        private static readonly AccessTools.FieldRef<AircraftActionsReport, Image> ActionBackgroundRef = NativeRefs.Field<AircraftActionsReport, Image>("background");
        private static readonly AccessTools.FieldRef<KillDisplay, TextMeshProUGUI> ScoreTextRef = NativeRefs.Field<KillDisplay, TextMeshProUGUI>("killText");

        private sealed class Box
        {
            /// <summary>The box shows while this text has characters (null: while <see cref="Root"/> is active).</summary>
            public TMP_Text Text;
            public GameObject Root;
            public CanvasGroup[] Groups = new CanvasGroup[0];
            public bool[] Owned = new bool[0];
            public float[] Alpha = new float[0];
        }

        private readonly Box[] boxes = new Box[4];

        /// <summary>Every box is held (nothing left to look for).</summary>
        public bool Complete
        {
            get
            {
                for (int i = 0; i < boxes.Length; i++) if (!Held((CueSource)i)) return false;
                return true;
            }
        }

        /// <summary>The box exists and every object of it is still there: its cues may be drawn, its native look hidden.</summary>
        public bool Held(CueSource source)
        {
            Box box = boxes[(int)source];
            if (box == null || box.Groups.Length == 0 || (box.Text == null && box.Root == null)) return false;
            for (int i = 0; i < box.Groups.Length; i++) if (box.Groups[i] == null) return false;
            return true;
        }

        /// <summary>The game's box is showing something now: its text has characters, or its object is active. A count of one field read, nothing allocated.</summary>
        public bool Showing(CueSource source)
        {
            Box box = boxes[(int)source];
            if (box == null) return false;
            if (box.Text != null) return box.Text.isActiveAndEnabled && box.Text.text.Length > 0;
            return box.Root != null && box.Root.activeInHierarchy;
        }

        /// <summary>Look the native boxes up in the game, the ones not held now (production).</summary>
        public void Find()
        {
            if (!Held(CueSource.Action))
            {
                AircraftActionsReport report = SceneSingleton<AircraftActionsReport>.i;
                TextMeshProUGUI text = NativeRefs.Get(ActionTextRef, report);
                Image background = NativeRefs.Get(ActionBackgroundRef, report);
                if (text != null) Adopt(CueSource.Action, text, null, background != null ? background.gameObject : null, text.gameObject);
            }
            if (!Held(CueSource.Reserve))
            {
                ReserveReport reserve = SceneSingleton<ReserveReport>.i;
                if (reserve != null) Adopt(CueSource.Reserve, null, reserve.gameObject, reserve.gameObject);
            }
            if (!Held(CueSource.Allocation))
            {
                AllocationDisplay allocation = SceneSingleton<AllocationDisplay>.i;
                if (allocation != null) Adopt(CueSource.Allocation, null, allocation.gameObject, allocation.gameObject);
            }
            if (!Held(CueSource.Score))
            {
                TextMeshProUGUI score = NativeRefs.Get(ScoreTextRef, SceneSingleton<KillDisplay>.i);
                if (score != null) Adopt(CueSource.Score, score, null, score.gameObject);
            }
        }

        /// <summary>
        /// Hold these objects as the box of <paramref name="source"/> (what <see cref="Find"/> found; an offline render stands them in): <paramref name="text"/> is the text whose characters say the box is showing (or null, then
        /// <paramref name="root"/> being active says it) and <paramref name="hide"/> the objects whose look is hidden (null entries and repeats are left out). What was held for that source before is given back first.
        /// </summary>
        public void Adopt(CueSource source, TMP_Text text, GameObject root, params GameObject[] hide)
        {
            Release(source);
            var box = new Box { Text = text, Root = root };
            int n = 0;
            for (int i = 0; i < hide.Length; i++) if (hide[i] != null && System.Array.IndexOf(hide, hide[i]) == i) n++;
            box.Groups = new CanvasGroup[n];
            box.Owned = new bool[n];
            box.Alpha = new float[n];
            int at = 0;
            for (int i = 0; i < hide.Length; i++)
            {
                if (hide[i] == null || System.Array.IndexOf(hide, hide[i]) != i) continue;
                CanvasGroup group = hide[i].GetComponent<CanvasGroup>();
                bool owned = group == null;
                if (owned) group = hide[i].AddComponent<CanvasGroup>();
                box.Groups[at] = group;
                box.Owned[at] = owned;
                box.Alpha[at] = owned || group == null ? 1f : group.alpha;
                at++;
            }
            boxes[(int)source] = box;
        }

        /// <summary>Hide the game's box (the cues stand in for it) or show it as it was. Idempotent; meant to run every frame.</summary>
        public void Hide(CueSource source, bool hide)
        {
            Box box = boxes[(int)source];
            if (box == null) return;
            for (int i = 0; i < box.Groups.Length; i++)
            {
                CanvasGroup group = box.Groups[i];
                if (group == null) continue;
                float alpha = hide ? 0f : box.Alpha[i];
                if (group.alpha != alpha) group.alpha = alpha;
            }
        }

        private void Release(CueSource source)
        {
            Box box = boxes[(int)source];
            boxes[(int)source] = null;
            if (box == null) return;
            for (int i = 0; i < box.Groups.Length; i++)
            {
                if (box.Groups[i] == null) continue;
                box.Groups[i].alpha = box.Alpha[i];
                if (box.Owned[i]) Object.Destroy(box.Groups[i]);
            }
        }

        /// <summary>Give every box back and let go of everything.</summary>
        public void Dispose()
        {
            for (int i = 0; i < boxes.Length; i++) Release((CueSource)i);
        }
    }
}
