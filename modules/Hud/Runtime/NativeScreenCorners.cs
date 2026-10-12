using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The native objects of the two lower-priority screen corners the board restyles (LAYOUT.md 7): the kill feed's box at the top-left (<c>MessageUI.messageBackground</c> holds it; its dark plate is the child called
    /// <c>Darkener</c>) and the minimised map's plate at the bottom-left (<c>DynamicMap.mapBackground</c>'s rectangle). Reads only. The one thing it changes is the feed's plate: through a <see cref="CanvasGroup"/> it
    /// OWNS on <c>Darkener</c> at alpha 0 and through the alpha of the box's own <c>Image</c> renderer. In the game's scene (read from its assets, confirmed by a capture) <c>Darkener</c>'s Image is switched OFF and the dark rounded
    /// plate people see is the Image on the box itself (<c>ChatBackground</c>, which also holds the texts as children, so a group on it would hide them): its <see cref="CanvasRenderer"/> alpha is the one thing that hides that plate alone.
    /// Never <c>SetActive</c> on the plate or on the box (the game's <c>MessageUI</c> activates and deactivates the box itself, and the feed's texts live in it), never a disabled native script or Image, the feed's own text and
    /// layout untouched. <see cref="HidePlate"/> is told every frame whether the board's own shade is standing in for it (fail open: the plate comes back the same frame it is not);
    /// <see cref="Dispose"/> takes our group off and puts the renderer's alpha back, so the game has its plate exactly as it was. A native object that cannot be found leaves its half off (the board skips that corner).
    /// </summary>
    internal sealed class NativeScreenCorners
    {
        private static readonly AccessTools.FieldRef<MessageUI, GameObject> BackgroundRef = NativeRefs.Field<MessageUI, GameObject>("messageBackground");

        private Image darkener, boxPlate;
        private RectTransform feedBox, mapPlate;
        private CanvasGroup plate;

        /// <summary>Both native objects are held (nothing left to look for).</summary>
        public bool Complete => feedBox != null && mapPlate != null;

        /// <summary>The feed's box exists: its plate has a group of ours.</summary>
        public bool HasFeed => feedBox != null && plate != null;

        /// <summary>The kill feed's box is up now: the game shows it while there is a line or the chat is open.</summary>
        public bool FeedUp => HasFeed && darkener.gameObject.activeInHierarchy;

        /// <summary>The map's plate is up now (the map is in the HUD: not hidden, not maximised, which the board's gate checks on its own).</summary>
        public bool MapUp => mapPlate != null && mapPlate.gameObject.activeInHierarchy;

        public RectTransform FeedBox => feedBox;
        public RectTransform MapPlate => mapPlate;

        /// <summary>Look the two objects up in the game (the plate by its name under the feed's background).</summary>
        public void Find()
        {
            MessageUI ui = SceneSingleton<MessageUI>.i;
            GameObject background = NativeRefs.Get(BackgroundRef, ui);
            Image dark = null;
            if (background != null)
                foreach (Image image in background.GetComponentsInChildren<Image>(true))
                    if (image.name == "Darkener") { dark = image; break; }
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            Adopt(dark, map != null && map.mapBackground != null ? map.mapBackground.rectTransform : null);
        }

        /// <summary>Hold these objects (what <see cref="Find"/> found; an offline render stands them in): <paramref name="darkener"/> is the feed's plate, whose parent is its box.</summary>
        public void Adopt(Image darkener, RectTransform mapPlate)
        {
            Dispose();
            this.darkener = darkener;
            feedBox = darkener != null ? darkener.rectTransform.parent as RectTransform : null;
            this.mapPlate = mapPlate;
            if (feedBox == null) return;
            plate = darkener.gameObject.AddComponent<CanvasGroup>();      // null if Unity would not take a second group: the feed stays as the game made it
            if (plate == null) { feedBox = null; return; }
            plate.alpha = 1f;
            boxPlate = feedBox.GetComponent<Image>();
        }

        /// <summary>Hide the feed's plate (the board's shade stands in for it) or show it. Idempotent; meant to run every frame.</summary>
        public void HidePlate(bool hide)
        {
            if (plate == null) return;
            float alpha = hide ? 0f : 1f;
            if (plate.alpha != alpha) plate.alpha = alpha;
            if (boxPlate != null && boxPlate.canvasRenderer.GetAlpha() != alpha) boxPlate.canvasRenderer.SetAlpha(alpha);      // every frame, not once: the game switches the box off and on as lines come and go
        }

        /// <summary>Give the plate back and let go of everything.</summary>
        public void Dispose()
        {
            if (plate != null) { plate.alpha = 1f; Object.Destroy(plate); }
            if (boxPlate != null) boxPlate.canvasRenderer.SetAlpha(1f);
            plate = null;
            darkener = boxPlate = null;
            feedBox = mapPlate = null;
        }
    }
}
