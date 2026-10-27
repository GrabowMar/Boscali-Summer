using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The native objects of the two lower-priority screen corners the board restyles (LAYOUT.md 7): the kill feed's box at the top-left (<c>MessageUI.messageBackground</c> holds it; its dark plate is the child called
    /// <c>Darkener</c>) and the minimised map's plate at the bottom-left (<c>DynamicMap.mapBackground</c>'s rectangle). Reads only, except for two things it hides while the mod's own pieces stand in for them. The feed's plate:
    /// through a <see cref="CanvasGroup"/> it OWNS on <c>Darkener</c> at alpha 0 and through the alpha of the box's own <c>Image</c> renderer. In the game's scene (read from its assets, confirmed by a capture) <c>Darkener</c>'s Image is
    /// switched OFF and the dark rounded plate people see is the Image on the box itself (<c>ChatBackground</c>, which also holds the texts as children, so a group on it would hide them): its <see cref="CanvasRenderer"/> alpha is
    /// the one thing that hides that plate alone. The feed's TEXT (DL EVT LOG draws the lines itself, 2026-10-13): one group per text object (<c>MessageUI.messageText</c> and <c>killFeedText</c>; a group the object already
    /// had is reused and its alpha put back, one we added is destroyed on <see cref="Dispose"/>) at alpha 0. Never <c>SetActive</c> on the plate, the texts or the box (the game's <c>MessageUI</c> activates and deactivates the box itself, and
    /// the feed's texts live in it), never a disabled native script, Image or text, the feed's own text and layout untouched: the game keeps writing its lines into objects that are only not seen, so its own removal timing and
    /// the base game's chat work as ever. <see cref="HidePlate"/> and <see cref="HideText"/> are told every frame whether the mod is standing in (fail open: they come back the same frame it is not);
    /// <see cref="Typing"/> says the chat input is open, when the game's feed is to be left whole; <see cref="Dispose"/> takes our groups off and puts the alphas back, so the game has its plate and its text exactly as they were.
    /// A native object that cannot be found leaves its half off (the board skips that corner).
    /// </summary>
    internal sealed class NativeScreenCorners
    {
        private static readonly AccessTools.FieldRef<MessageUI, GameObject> BackgroundRef = NativeRefs.Field<MessageUI, GameObject>("messageBackground");
        private static readonly AccessTools.FieldRef<MessageUI, TextMeshProUGUI> MessageTextRef = NativeRefs.Field<MessageUI, TextMeshProUGUI>("messageText"), KillTextRef = NativeRefs.Field<MessageUI, TextMeshProUGUI>("killFeedText");
        private static readonly AccessTools.FieldRef<MessageUI, ChatBox> ChatRef = NativeRefs.Field<MessageUI, ChatBox>("chat");

        private Image darkener, boxPlate;
        private RectTransform feedBox, mapPlate;
        private Transform mapImage;
        private CanvasGroup plate;
        private GameObject chat;
        private TMP_Text[] texts = new TMP_Text[0];
        private CanvasGroup[] textGroups = new CanvasGroup[0];
        private bool[] groupOwned = new bool[0];
        private float[] groupAlpha = new float[0];

        /// <summary>Both native objects are held (nothing left to look for).</summary>
        public bool Complete => feedBox != null && mapPlate != null;

        /// <summary>The feed's box exists: its plate has a group of ours.</summary>
        public bool HasFeed => feedBox != null && (plate != null || boxPlate != null);

        /// <summary>The kill feed's box is up now: the game shows it while there is a line or the chat is open.</summary>
        public bool FeedUp => HasFeed && darkener.gameObject.activeInHierarchy;

        /// <summary>The chat input is open (<c>MessageUI.chat</c> is active: the game's own flag, which its box and its Update follow): the pilot is typing, and the feed is left whole for it.</summary>
        public bool Typing => chat != null && chat.activeSelf;

        /// <summary>The game's feed has characters laid out (the parsed length of either text object): lines are on it. A count of two int reads, nothing allocated.</summary>
        public bool FeedHasText
        {
            get
            {
                for (int i = 0; i < texts.Length; i++)
                    if (texts[i] != null && texts[i].textInfo != null && texts[i].textInfo.characterCount > 0) return true;
                return false;
            }
        }

        /// <summary>The map's plate is up now (the map is in the HUD: not hidden, not maximised, which the board's gate checks on its own).</summary>
        public bool MapUp => mapPlate != null && mapPlate.gameObject.activeInHierarchy;

        public RectTransform FeedBox => feedBox;

        /// <summary>The feed box's bottom edge in screen pixels from the top of the screen while it is up, else 0 (its own canvas: it stays up in free camera, where the HUD canvas is off).</summary>
        public float FeedBottomPx()
        {
            if (!FeedUp) return 0f;
            Canvas canvas = feedBox.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            feedBox.GetWorldCorners(worldCorners);
            float low = float.PositiveInfinity;
            for (int i = 0; i < 4; i++) low = Mathf.Min(low, RectTransformUtility.WorldToScreenPoint(cam, worldCorners[i]).y);
            return Mathf.Max(0f, Screen.height - low);
        }

        private readonly Vector3[] worldCorners = new Vector3[4];
        public RectTransform MapPlate => mapPlate;

        /// <summary>
        /// How far the minimised map is turned, degrees counter-clockwise (NaN when the map image is not held): the game's <c>DynamicMap.CenterMinimizedMap</c> sets <c>mapImage.eulerAngles.z</c> to the aircraft's heading every frame,
        /// so this reads the heading and north lies that far counter-clockwise of up on the screen. Reads only (a <see cref="Vector3"/> out of a transform: nothing is allocated).
        /// </summary>
        public float MapTurnDeg => mapImage != null ? mapImage.eulerAngles.z : float.NaN;

        /// <summary>Look the native objects up in the game (the plate by its name under the feed's background; the feed's two texts and the chat input off <c>MessageUI</c>).</summary>
        public void Find()
        {
            MessageUI ui = SceneSingleton<MessageUI>.i;
            GameObject background = NativeRefs.Get(BackgroundRef, ui);
            Image dark = null;
            if (background != null)
                foreach (Image image in background.GetComponentsInChildren<Image>(true))
                    if (image.name == "Darkener") { dark = image; break; }
            if (dark == null && background != null) dark = background.GetComponent<Image>();      // the game's 2026-10-27 update dropped Darkener: the plate is the box's own Image (MessageUI.backgroundImage)
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            ChatBox input = NativeRefs.Get(ChatRef, ui);
            Adopt(dark, map != null && map.mapBackground != null ? map.mapBackground.rectTransform : null, map != null && map.mapImage != null ? map.mapImage.transform : null,
                new TMP_Text[] { NativeRefs.Get(MessageTextRef, ui), NativeRefs.Get(KillTextRef, ui) }, input != null ? input.gameObject : null);
        }

        /// <summary>
        /// Hold these objects (what <see cref="Find"/> found; an offline render stands them in): <paramref name="darkener"/> is the feed's plate, whose parent is its box; <paramref name="mapImage"/> the map picture the game turns by
        /// the heading; <paramref name="feedTexts"/> the feed's text objects and <paramref name="chat"/> the chat input's object. Looking again (a retry of <see cref="Find"/>) keeps what is already held and ours: a group destroyed this
        /// frame cannot be added again in it.
        /// </summary>
        public void Adopt(Image darkener, RectTransform mapPlate, Transform mapImage = null, TMP_Text[] feedTexts = null, GameObject chat = null)
        {
            if (!(darkener != null && darkener == this.darkener && (plate != null || boxPlate == darkener)))
            {
                Dispose();
                this.darkener = darkener;
                if (darkener != null && darkener.name != "Darkener")
                {
                    feedBox = darkener.rectTransform;      // the box's own plate: no group (it would hide the texts, its children), its renderer alpha alone hides it
                    boxPlate = darkener;
                }
                else
                    feedBox = darkener != null ? darkener.rectTransform.parent as RectTransform : null;
                if (feedBox != null && boxPlate == null)
                {
                    plate = darkener.gameObject.AddComponent<CanvasGroup>();      // null if Unity would not take a second group: the feed stays as the game made it
                    if (plate == null) feedBox = null;
                    else
                    {
                        plate.alpha = 1f;
                        boxPlate = feedBox.GetComponent<Image>();
                    }
                }
            }
            this.mapPlate = mapPlate;
            this.mapImage = mapImage;
            this.chat = chat;
            if (feedBox != null && !SameTexts(feedTexts)) HoldTexts(feedTexts);
        }

        private bool SameTexts(TMP_Text[] list)
        {
            int n = 0;
            if (list != null) foreach (TMP_Text t in list) if (t != null) n++;
            if (n != texts.Length) return false;
            int at = 0;
            if (list != null)
                foreach (TMP_Text t in list)
                    if (t != null && texts[at++] != t) return false;
            return true;
        }

        /// <summary>A group on each of the feed's text objects: the one it has, or ours.</summary>
        private void HoldTexts(TMP_Text[] list)
        {
            ReleaseTexts();
            if (list == null) return;
            int n = 0;
            foreach (TMP_Text t in list) if (t != null) n++;
            texts = new TMP_Text[n];
            textGroups = new CanvasGroup[n];
            groupOwned = new bool[n];
            groupAlpha = new float[n];
            int at = 0;
            foreach (TMP_Text t in list)
            {
                if (t == null) continue;
                CanvasGroup group = t.GetComponent<CanvasGroup>();
                bool owned = group == null;
                if (owned) group = t.gameObject.AddComponent<CanvasGroup>();
                texts[at] = t;
                textGroups[at] = group;
                groupOwned[at] = owned;
                groupAlpha[at] = owned || group == null ? 1f : group.alpha;
                at++;
            }
        }

        private void ReleaseTexts()
        {
            for (int i = 0; i < textGroups.Length; i++)
            {
                if (textGroups[i] == null) continue;
                textGroups[i].alpha = groupAlpha[i];
                if (groupOwned[i]) Object.Destroy(textGroups[i]);
            }
            texts = new TMP_Text[0];
            textGroups = new CanvasGroup[0];
            groupOwned = new bool[0];
            groupAlpha = new float[0];
        }

        /// <summary>Hide the feed's plate (the board's shade stands in for it) or show it. Idempotent; meant to run every frame.</summary>
        public void HidePlate(bool hide)
        {
            if (plate == null && boxPlate == null) return;
            float alpha = hide ? 0f : 1f;
            if (plate != null && plate.alpha != alpha) plate.alpha = alpha;
            if (boxPlate != null && boxPlate.canvasRenderer.GetAlpha() != alpha) boxPlate.canvasRenderer.SetAlpha(alpha);      // every frame, not once: the game switches the box off and on as lines come and go
        }

        /// <summary>Hide the feed's text (DL EVT LOG draws the lines) or show it as it was. Idempotent; meant to run every frame.</summary>
        public void HideText(bool hide)
        {
            for (int i = 0; i < textGroups.Length; i++)
            {
                CanvasGroup group = textGroups[i];
                if (group == null) continue;
                float alpha = hide ? 0f : groupAlpha[i];
                if (group.alpha != alpha) group.alpha = alpha;
            }
        }

        /// <summary>Give the plate and the text back and let go of everything.</summary>
        public void Dispose()
        {
            ReleaseTexts();
            if (plate != null) { plate.alpha = 1f; Object.Destroy(plate); }
            if (boxPlate != null) boxPlate.canvasRenderer.SetAlpha(1f);
            plate = null;
            darkener = boxPlate = null;
            feedBox = mapPlate = null;
            mapImage = null;
            chat = null;
        }
    }
}
