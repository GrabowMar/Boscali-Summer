using System.Collections.Generic;
using System.Globalization;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Scenario.Presentation
{
    /// <summary>
    /// What a mission script puts on the screen: the transmission plate (who is speaking, their words typed on), title cards, hints, and a cutscene's dressing:
    /// letterbox bars, a vignette, captions, a typed place-and-date stamp, dips to black, and pictures that land like polaroids and then line up along the top
    /// bar as a timeline. A cutscene is framed as a surveillance feed: corner brackets, scan lines, a recording lamp with a running clock, the camera's own
    /// height, heading and grid read off live, and a burst of interference on every cut. One overlay canvas above the game's, switched off whenever nothing shows. It takes no input: nothing here can be clicked.
    /// </summary>
    internal sealed class ScenarioStageView : MonoBehaviour
    {
        private const int SortingOrder = 30004, MaxPhotos = 7;
        private const float BarShare = 0.11f, TypeCharsPerSecond = 60f, StampCharsPerSecond = 38f, PlateWidth = 1040f, PlatePad = 24f, HintWidth = 780f;
        // A polaroid: the picture, a white border all round and a deeper one below for the label.
        private const float PhotoWidth = 400f, PhotoHeight = 225f, Border = 16f, LabelBand = 62f;
        private const float LandSeconds = 0.32f, DockSeconds = 0.7f, DockScale = 0.33f, SlotStep = 255f;
        private static readonly Vector2 HeldAt = new Vector2(372f, -455f), FirstSlot = new Vector2(118f, -60f);
        private static readonly Color Paper = new Color(0.95f, 0.94f, 0.9f, 1f), Ink = new Color(0.13f, 0.13f, 0.15f, 1f);

        /// <summary>A picture on the screen: landing, held large beside the action, or docked small in the timeline.</summary>
        private sealed class Polaroid
        {
            public RectTransform Root;
            public Image Shadow, Frame;
            public RawImage Photo;
            public TextMeshProUGUI Label, Day;
            public float LandedAt, DockAt = -1f, Angle;
            public int Slot;
        }

        private const int StripeCount = 5;
        private static readonly Color Lamp = new Color(0.95f, 0.16f, 0.12f, 1f);

        private readonly List<Polaroid> photos = new List<Polaroid>();
        private readonly List<Image> brackets = new List<Image>();
        private readonly Image[] stripes = new Image[StripeCount];
        private Image lamp, crossAcross, crossUp;
        private RawImage scan;
        private Texture2D scanTexture;
        private TextMeshProUGUI feed, telemetry;
        private float nextReadout;
        private Canvas canvas;
        private RectTransform root;
        private Image top, bottom, fade, shutter, band, plate, plateAccent, hintPlate, hintAccent, rail, stampMark;
        private RawImage vignette;
        private Texture2D vignetteTexture;
        private TextMeshProUGUI caption, skip, cardTitle, cardText, callsign, role, line, hint, stamp;
        private RectTransform dialogue, hintBox;
        private int shown;

        // Cutscene state runs on real time; everything else on game time, so a paused game holds the words with the story.
        private bool cut;
        private float cutChanged = -100f, captionShown = -100f, flash = -100f, snap = -100f;
        private float cardFrom, cardUntil = -1f, lineFrom, lineUntil = -1f, hintFrom, hintUntil = -1f;
        private bool cardInCut;

        internal static ScenarioStageView Create(Transform owner)
        {
            var go = new GameObject("BoscaliSummer.ScenarioStage", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(owner, false);
            var view = go.AddComponent<ScenarioStageView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root = (RectTransform)transform;

            vignette = new GameObject("Vignette", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<RawImage>();
            vignette.transform.SetParent(root, false);
            vignette.raycastTarget = false;
            vignette.texture = vignetteTexture = Vignette();
            AvLay.Fill(vignette.rectTransform);

            scan = new GameObject("ScanLines", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<RawImage>();
            scan.transform.SetParent(root, false);
            scan.raycastTarget = false;
            scan.texture = scanTexture = ScanLines();
            scan.uvRect = new Rect(0f, 0f, 1f, 270f);
            AvLay.Fill(scan.rectTransform);
            for (int i = 0; i < StripeCount; i++)
            {
                stripes[i] = AvLay.Solid(root, "Interference" + i, Color.clear);
                Anchor(stripes[i].rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 8f));
            }

            top = AvLay.Solid(root, "BarTop", Color.black);
            bottom = AvLay.Solid(root, "BarBottom", Color.black);

            // The feed's frame sits inside the picture, clear of the bars.
            float bar = 1080f * BarShare;
            for (int corner = 0; corner < 4; corner++)
            {
                float x = corner % 2, y = corner / 2;
                var at = new Vector2(x, y);
                var inset = new Vector2(x == 0f ? 46f : -46f, y == 0f ? bar + 24f : -bar - 24f);
                Image across = AvLay.Solid(root, "BracketAcross" + corner, Color.clear), up = AvLay.Solid(root, "BracketUp" + corner, Color.clear);
                Anchor(across.rectTransform, at, at, at, inset, new Vector2(54f, 2f));
                Anchor(up.rectTransform, at, at, at, inset, new Vector2(2f, 54f));
                brackets.Add(across);
                brackets.Add(up);
            }
            var middle = new Vector2(0.5f, 0.5f);
            crossAcross = AvLay.Solid(root, "CrossAcross", Color.clear);
            Anchor(crossAcross.rectTransform, middle, middle, middle, Vector2.zero, new Vector2(46f, 1f));
            crossUp = AvLay.Solid(root, "CrossUp", Color.clear);
            Anchor(crossUp.rectTransform, middle, middle, middle, Vector2.zero, new Vector2(1f, 46f));
            lamp = AvLay.Solid(root, "RecordingLamp", Color.clear);
            Anchor(lamp.rectTransform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-64f, -bar - 46f), new Vector2(12f, 12f));
            feed = Text(root, "Feed", AvTextRole.DataStrong, 16f, TextAlignmentOptions.TopRight, false);
            feed.characterSpacing = 6f;
            Anchor(feed.rectTransform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-88f, -bar - 41f), new Vector2(760f, 24f));
            telemetry = Text(root, "Telemetry", AvTextRole.DataStrong, 16f, TextAlignmentOptions.BottomLeft, false);
            telemetry.characterSpacing = 6f;
            Anchor(telemetry.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(64f, bar + 38f), new Vector2(1200f, 24f));
            rail = AvLay.Solid(root, "TimelineRail", Color.clear);
            Anchor(rail.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(FirstSlot.x, FirstSlot.y), new Vector2(0f, 2f));

            band = AvLay.Solid(root, "CardBand", Color.clear);
            Anchor(band.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 250f));
            cardTitle = Text(root, "CardTitle", AvTextRole.Headline, 76f, TextAlignmentOptions.Center, false);
            cardTitle.characterSpacing = 14f;
            Anchor(cardTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 34f), new Vector2(-240f, 100f));
            cardText = Text(root, "CardText", AvTextRole.Prose, 28f, TextAlignmentOptions.Center, true);
            Anchor(cardText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -52f), new Vector2(-420f, 80f));

            caption = Text(root, "Caption", AvTextRole.Prose, 30f, TextAlignmentOptions.Center, true);
            Anchor(caption.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-360f, 92f));
            skip = Text(root, "Skip", AvTextRole.Label, 14f, TextAlignmentOptions.Right, false);
            skip.text = "SPACE  SKIP";
            Anchor(skip.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 14f), new Vector2(320f, 24f));

            stampMark = AvLay.Solid(root, "StampMark", Color.clear);
            Anchor(stampMark.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(64f, -1080f * BarShare - 46f), new Vector2(10f, 22f));
            stamp = Text(root, "Stamp", AvTextRole.DataStrong, 22f, TextAlignmentOptions.TopLeft, false);
            stamp.characterSpacing = 8f;
            Anchor(stamp.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(86f, -1080f * BarShare - 44f), new Vector2(1200f, 30f));

            dialogue = AvLay.Child(root, "Transmission");
            Anchor(dialogue, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(PlateWidth, 120f));
            plate = AvLay.Solid(dialogue, "Plate", Color.clear);
            AvLay.Fill(plate.rectTransform);
            plateAccent = AvLay.Solid(dialogue, "Accent", Color.clear);
            Anchor(plateAccent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(5f, 0f));
            callsign = Text(dialogue, "Callsign", AvTextRole.Head, 22f, TextAlignmentOptions.TopLeft, false);
            Anchor(callsign.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(PlatePad, -12f), new Vector2(-2f * PlatePad, 28f));
            role = Text(dialogue, "Role", AvTextRole.Label, 15f, TextAlignmentOptions.TopRight, false);
            Anchor(role.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-PlatePad, -16f), new Vector2(-2f * PlatePad, 22f));
            line = Text(dialogue, "Line", AvTextRole.Prose, 25f, TextAlignmentOptions.TopLeft, true);
            Anchor(line.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(PlatePad, -46f), new Vector2(-2f * PlatePad, -62f));

            hintBox = AvLay.Child(root, "Hint");
            Anchor(hintBox, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -176f), new Vector2(HintWidth, 44f));
            hintPlate = AvLay.Solid(hintBox, "Plate", Color.clear);
            AvLay.Fill(hintPlate.rectTransform);
            hintAccent = AvLay.Solid(hintBox, "Accent", Color.clear);
            Anchor(hintAccent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(4f, 0f));
            hint = Text(hintBox, "Text", AvTextRole.Prose, 20f, TextAlignmentOptions.TopLeft, true);
            Anchor(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(-36f, -20f));

            shutter = AvLay.Solid(root, "Shutter", Color.clear);
            AvLay.Fill(shutter.rectTransform);
            fade = AvLay.Solid(root, "Fade", Color.clear);
            AvLay.Fill(fade.rectTransform);
            Paint();
        }

        /// <summary>One dark line in every four, repeated down the screen.</summary>
        private static Texture2D ScanLines()
        {
            var texture = new Texture2D(1, 4, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels32(new[] { new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 0) });
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Clear in the middle, dark toward the corners.</summary>
        private static Texture2D Vignette()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    // 0 inside 0.6 of the half-width, rising smoothly to the corners (Mathf.SmoothStep is an interpolation, not this).
                    float t = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.6f) / 0.8f);
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(t * t * (3f - 2f * t) * 170f));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static TextMeshProUGUI Text(RectTransform parent, string name, AvTextRole textRole, float size, TextAlignmentOptions align, bool wrap)
        {
            TextMeshProUGUI text = AvText.Make(parent, name, textRole, "", align, wrap);
            AvText.Size(text, size);
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>A speaker's line, typed on and held for <paramref name="seconds"/> of game time.</summary>
        internal void Say(string who, string whoRole, Color tint, string words, float seconds)
        {
            callsign.text = who ?? "";
            callsign.color = tint;
            role.text = whoRole ?? "";
            line.text = words ?? "";
            line.maxVisibleCharacters = 0;
            plateAccent.color = tint;
            float height = 46f + Mathf.Ceil(line.GetPreferredValues(line.text, PlateWidth - 2f * PlatePad, 0f).y) + 18f;
            dialogue.sizeDelta = new Vector2(PlateWidth, Mathf.Max(96f, height));
            lineFrom = Time.time;
            lineUntil = lineFrom + seconds;
        }

        /// <summary>A title card: <paramref name="title"/> large (it may be null) with <paramref name="words"/> under it.</summary>
        internal void Card(string title, string words, float seconds)
        {
            SetCard(title, words);
            cardInCut = false;
            cardFrom = Time.time;
            cardUntil = cardFrom + seconds;
        }

        internal void Hint(string words, float seconds)
        {
            hint.text = words ?? "";
            float height = Mathf.Ceil(hint.GetPreferredValues(hint.text, HintWidth - 36f, 0f).y) + 20f;
            hintBox.sizeDelta = new Vector2(HintWidth, Mathf.Max(44f, height));
            hintFrom = Time.time;
            hintUntil = hintFrom + seconds;
        }

        /// <summary>A quick fade through black, starting black now.</summary>
        internal void Dip() => flash = Time.unscaledTime;

        internal void BeginCut()
        {
            cut = true;
            cutChanged = Time.unscaledTime;
            flash = Time.unscaledTime;
            lineUntil = cardUntil = -1f;
            caption.text = "";
            stamp.text = "";
            shown = 0;
            foreach (Polaroid photo in photos) photo.Root.gameObject.SetActive(false);
        }

        /// <summary>
        /// The words of the shot that just started: a caption in the lower bar or a title card over the picture, a stamp typed in the corner, and a picture
        /// that lands beside the action. The picture of the shot before moves up into the timeline. True when a picture landed.
        /// </summary>
        internal bool Caption(string title, string words, string stampText, Texture picture)
        {
            float now = Time.unscaledTime;
            captionShown = now;
            cardInCut = !string.IsNullOrEmpty(title);
            if (cardInCut)
            {
                SetCard(title, words);
                caption.text = "";
            }
            else caption.text = words ?? "";
            stamp.text = stampText ?? "";
            stamp.maxVisibleCharacters = 0;
            for (int i = 0; i < shown; i++)
                if (photos[i].DockAt < 0f) photos[i].DockAt = now;
            if (picture == null || shown >= MaxPhotos) return false;

            Polaroid landing = shown < photos.Count ? photos[shown] : Make();
            landing.Slot = shown++;
            landing.Photo.texture = picture;
            // The stamp reads "PLACE / DAY 9": the day goes on the picture and under its place in the timeline.
            string day = stampText ?? "";
            int slash = day.LastIndexOf('/');
            if (slash >= 0) day = day.Substring(slash + 1).Trim();
            landing.Label.text = day;
            landing.Day.text = day;
            landing.LandedAt = now;
            landing.DockAt = -1f;
            // Each picture lies a little crooked, the same way every time the cutscene plays.
            landing.Angle = (landing.Slot * 37 % 11 - 5) * 1.3f;
            landing.Root.gameObject.SetActive(true);
            landing.Day.transform.SetAsLastSibling();
            landing.Root.SetAsLastSibling();
            shutter.transform.SetAsLastSibling();
            fade.transform.SetAsLastSibling();
            snap = now;
            return true;
        }

        internal void EndCut()
        {
            if (!cut) return;
            cut = false;
            cardInCut = false;
            cutChanged = Time.unscaledTime;
            flash = Time.unscaledTime;   // a dip to black covers the camera going back
            caption.text = "";
            stamp.text = "";
        }

        internal void Clear()
        {
            cut = cardInCut = false;
            cutChanged = captionShown = flash = snap = -100f;
            cardUntil = lineUntil = hintUntil = -1f;
            Paint();
        }

        private Polaroid Make()
        {
            var photo = new Polaroid();
            float width = PhotoWidth + 2f * Border, height = PhotoHeight + Border + LabelBand;
            photo.Root = AvLay.Child(root, "Polaroid" + photos.Count);
            Anchor(photo.Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), HeldAt, new Vector2(width, height));
            photo.Shadow = AvLay.Solid(photo.Root, "Shadow", Color.black);
            Anchor(photo.Shadow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, -12f), Vector2.zero);
            photo.Frame = AvLay.Solid(photo.Root, "Frame", Paper);
            AvLay.Fill(photo.Frame.rectTransform);
            photo.Photo = new GameObject("Photo", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<RawImage>();
            photo.Photo.transform.SetParent(photo.Root, false);
            photo.Photo.raycastTarget = false;
            Anchor(photo.Photo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -Border), new Vector2(PhotoWidth, PhotoHeight));
            photo.Label = Text(photo.Root, "Label", AvTextRole.DataStrong, 26f, TextAlignmentOptions.Center, false);
            photo.Label.characterSpacing = 6f;
            Anchor(photo.Label.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(-2f * Border, 38f));
            // The day under the picture's place in the timeline, where the picture's own label is too small to read.
            photo.Day = Text(root, "Day" + photos.Count, AvTextRole.DataStrong, 18f, TextAlignmentOptions.Left, false);
            Anchor(photo.Day.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(150f, 22f));
            photos.Add(photo);
            return photo;
        }

        private void SetCard(string title, string words)
        {
            cardTitle.text = title ?? "";
            cardText.text = words ?? "";
            // Words alone sit in the middle of the band; under a title they sit below it.
            cardText.rectTransform.anchoredPosition = new Vector2(0f, string.IsNullOrEmpty(title) ? 0f : -52f);
        }

        private void Update() => Paint();

        private void Paint()
        {
            float real = Time.unscaledTime, game = Time.time;
            float bars = Mathf.Clamp01((real - cutChanged) / 0.5f);
            if (!cut) bars = 1f - bars;
            float dip = 1f - Mathf.Clamp01((real - flash) / 0.9f);
            float cardAlpha = cardInCut ? Mathf.Clamp01((real - captionShown) / 0.6f) : Window(game, cardFrom, cardUntil, 0.5f);
            float lineAlpha = Window(game, lineFrom, lineUntil, 0.25f);
            float hintAlpha = Window(game, hintFrom, hintUntil, 0.4f);

            bool any = bars > 0.001f || dip > 0.001f || cardAlpha > 0.001f || lineAlpha > 0.001f || hintAlpha > 0.001f;
            if (canvas.enabled != any) canvas.enabled = any;
            if (!any) return;

            float share = BarShare * Mathf.SmoothStep(0f, 1f, bars);
            top.rectTransform.anchorMin = new Vector2(0f, 1f - share);
            top.rectTransform.anchorMax = Vector2.one;
            top.rectTransform.offsetMin = top.rectTransform.offsetMax = Vector2.zero;
            bottom.rectTransform.anchorMin = Vector2.zero;
            bottom.rectTransform.anchorMax = new Vector2(1f, share);
            bottom.rectTransform.offsetMin = bottom.rectTransform.offsetMax = Vector2.zero;
            fade.color = new Color(0f, 0f, 0f, dip);
            // The picture breathes a little, the way a relayed feed does.
            vignette.color = Color.white.WithAlpha(bars * (0.9f + 0.1f * Mathf.PerlinNoise(real * 7f, 0.3f)));
            PaintFeed(real, bars);
            // The camera's shutter: a short white blink as a picture lands.
            shutter.color = Color.white.WithAlpha(cut ? 0.28f * (1f - Mathf.Clamp01((real - snap) / 0.22f)) : 0f);

            float shownAlpha = cut ? Mathf.Clamp01((real - captionShown) / 0.4f) : 0f;
            caption.color = Color.white.WithAlpha(shownAlpha);
            skip.color = Color.white.WithAlpha(cut ? 0.5f * bars : 0f);
            bool stamped = cut && stamp.text.Length > 0;
            stamp.color = AvTheme.Warning.WithAlpha(stamped ? 1f : 0f);
            stampMark.color = AvTheme.Warning.WithAlpha(stamped ? 1f : 0f);
            if (stamped) stamp.maxVisibleCharacters = (int)((real - captionShown) * StampCharsPerSecond);

            PaintPhotos(real, bars);

            band.color = Color.black.WithAlpha(0.62f * cardAlpha);
            cardTitle.color = Color.white.WithAlpha(cardAlpha);
            cardText.color = AvTheme.TextPrimary.WithAlpha(cardAlpha);

            plate.color = AvTheme.Ground.WithAlpha(0.86f * lineAlpha);
            plateAccent.color = plateAccent.color.WithAlpha(lineAlpha);
            callsign.color = callsign.color.WithAlpha(lineAlpha);
            role.color = AvTheme.Dim.WithAlpha(lineAlpha);
            line.color = AvTheme.TextPrimary.WithAlpha(lineAlpha);
            if (lineAlpha > 0f) line.maxVisibleCharacters = (int)((game - lineFrom) * TypeCharsPerSecond);

            hintPlate.color = AvTheme.Ground.WithAlpha(0.8f * hintAlpha);
            hintAccent.color = AvTheme.Warning.WithAlpha(hintAlpha);
            hint.color = AvTheme.TextPrimary.WithAlpha(hintAlpha);
        }

        /// <summary>The surveillance frame: brackets, scan lines, the recording lamp and clock, live camera readouts, interference on a cut.</summary>
        private void PaintFeed(float real, float bars)
        {
            float on = cut ? bars : 0f;
            Color ink = Paper.WithAlpha(0.72f * on);
            foreach (Image bracket in brackets) bracket.color = ink;
            scan.color = Color.white.WithAlpha(0.09f * bars);
            lamp.color = Lamp.WithAlpha(on * ((int)(real * 2f) % 2 == 0 ? 1f : 0.18f));
            feed.color = ink;
            telemetry.color = ink;
            Color cross = Paper.WithAlpha(cardInCut ? 0f : 0.3f * on);
            crossAcross.color = cross;
            crossUp.color = cross;

            float burst = cut ? 1f - Mathf.Clamp01((real - captionShown) / 0.24f) : 0f;
            float bar = 1080f * BarShare;
            foreach (Image stripe in stripes)
            {
                if (burst <= 0f) { stripe.color = Color.clear; continue; }
                stripe.rectTransform.anchoredPosition = new Vector2(Random.Range(-30f, 30f), -Random.Range(bar, 1080f - bar - 50f));
                stripe.rectTransform.sizeDelta = new Vector2(0f, Random.Range(3f, 46f));
                stripe.color = (Random.value < 0.5f ? Color.white : Color.black).WithAlpha(burst * Random.Range(0.2f, 0.55f));
            }

            if (on <= 0f || real < nextReadout) return;
            nextReadout = real + 0.1f;
            float running = real - cutChanged;
            feed.text = "ISR-4 // RELAY SAT-2 // REC " + ((int)(running / 60f)).ToString("00", CultureInfo.InvariantCulture) + ":" +
                (running % 60f).ToString("00.0", CultureInfo.InvariantCulture);
            Camera eye = Camera.main;
            if (eye == null) return;
            GlobalPosition at = eye.transform.position.ToGlobalPosition();
            telemetry.text = "ALT " + Mathf.Max(0f, at.y).ToString("0000", CultureInfo.InvariantCulture) + " M   HDG " +
                eye.transform.eulerAngles.y.ToString("000", CultureInfo.InvariantCulture) + "   GRID " +
                (at.x / 1000f).ToString("+00.0;-00.0", CultureInfo.InvariantCulture) + " E " +
                (at.z / 1000f).ToString("+00.0;-00.0", CultureInfo.InvariantCulture) + " N   FOV " +
                eye.fieldOfView.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Each picture lands, is held, then travels up to its place in the timeline; a rail joins the places taken.</summary>
        private void PaintPhotos(float real, float bars)
        {
            int docked = 0;
            for (int i = 0; i < shown && i < photos.Count; i++)
            {
                Polaroid photo = photos[i];
                float land = Mathf.Clamp01((real - photo.LandedAt) / LandSeconds);
                float dock = photo.DockAt < 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((real - photo.DockAt) / DockSeconds));
                if (dock >= 1f) docked = i + 1;
                Vector2 slot = FirstSlot + new Vector2(photo.Slot * SlotStep, 0f);
                photo.Root.anchoredPosition = Vector2.Lerp(HeldAt, slot, dock);
                float scale = Mathf.Lerp(Mathf.Lerp(1.35f, 1f, land), DockScale, dock);
                photo.Root.localScale = new Vector3(scale, scale, 1f);
                photo.Root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(photo.Angle + 9f * (1f - land), photo.Angle * 0.4f, dock));
                float alpha = bars * land;
                photo.Frame.color = Paper.WithAlpha(alpha);
                photo.Photo.color = Color.white.WithAlpha(alpha);
                photo.Shadow.color = Color.black.WithAlpha(0.45f * alpha);
                photo.Label.color = Ink.WithAlpha(alpha * (1f - dock));
                photo.Day.rectTransform.anchoredPosition = slot + new Vector2(84f, 0f);
                photo.Day.color = Paper.WithAlpha(bars * dock);
            }
            float length = docked > 1 ? (docked - 1) * SlotStep : 0f;
            rail.rectTransform.sizeDelta = new Vector2(length, 2f);
            rail.color = Paper.WithAlpha(docked > 1 ? 0.35f * bars : 0f);
            if (bars > 0.001f || cut) return;
            foreach (Polaroid photo in photos)
                if (photo.Root.gameObject.activeSelf) photo.Root.gameObject.SetActive(false);
            shown = 0;
        }

        /// <summary>1 inside [from, until], easing over <paramref name="edge"/> seconds at both ends, 0 outside.</summary>
        private static float Window(float now, float from, float until, float edge)
        {
            if (now < from || now > until) return 0f;
            return Mathf.Clamp01(Mathf.Min((now - from) / edge, (until - now) / edge));
        }

        private void OnDestroy()
        {
            if (vignetteTexture != null) Destroy(vignetteTexture);
            if (scanTexture != null) Destroy(scanTexture);
        }
    }
}
