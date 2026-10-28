using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Core.Scenario;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Scenario.Presentation
{
    /// <summary>
    /// A mission script's fallback transmission plate (normal dialogue uses COMMS), title cards, hints, and a cutscene's dressing:
    /// letterbox bars, a vignette, film grain, captions, a typed place-and-date stamp, dips to black, and pictures that land like polaroids (taped, developing
    /// in) and then line up along the top bar as a timeline, pinned to a red string. A cutscene is framed as a surveillance feed: corner brackets, scan lines,
    /// a recording lamp with a running clock, the camera's own height, heading and grid read off live, and a burst of interference on every cut. A cut's
    /// <see cref="CutFx"/> layer more on for that shot: a blast, a tracking lock, a lost signal, a red alert, a glitching feed, a colour grade.
    /// A cut that names a board (ScenarioStageView.Boards.cs) is dressed as that board instead of the feed.
    /// One overlay canvas above the game's, switched off whenever nothing shows. It takes no input: nothing here can be clicked.
    /// </summary>
    internal sealed partial class ScenarioStageView : MonoBehaviour
    {
        private const int SortingOrder = 30004, MaxPhotos = 7;
        private const float BarShare = 0.11f, TypeCharsPerSecond = 60f, StampCharsPerSecond = 38f, PlateWidth = 1040f, PlatePad = 24f, HintWidth = 780f;
        // A polaroid: the picture, a white border all round and a deeper one below for the label.
        private const float PhotoWidth = 400f, PhotoHeight = 225f, Border = 16f, LabelBand = 62f;
        private const float LandSeconds = 0.42f, DevelopSeconds = 1.8f, DockSeconds = 0.7f, DockScale = 0.33f, SlotStep = 255f, PinRise = 42f;
        private static readonly Vector2 HeldAt = new Vector2(372f, -455f), FirstSlot = new Vector2(118f, -60f);
        private static readonly Color Paper = new Color(0.95f, 0.94f, 0.9f, 1f), Ink = new Color(0.13f, 0.13f, 0.15f, 1f);
        private static readonly Color Marker = new Color(0.78f, 0.1f, 0.08f, 1f), Tape = new Color(0.96f, 0.93f, 0.8f, 1f);
        // A fresh print is a murky grey-green; a docked one has gone a little sepia.
        private static readonly Color Undeveloped = new Color(0.42f, 0.46f, 0.4f, 1f), Sepia = new Color(1f, 0.9f, 0.76f, 1f), Haze = new Color(0.8f, 0.82f, 0.76f, 1f);
        private const string Glyphs = "#%&@$/01X7=+*";

        /// <summary>A picture on the screen: landing, held large beside the action, or docked small in the timeline.</summary>
        private sealed class Polaroid
        {
            public RectTransform Root;
            public RawImage Shadow, Frame, Photo, Pin;
            public Image Edge, Mist, TapeStrip, Underline;
            public TextMeshProUGUI Label, Note, Day;
            public float LandedAt, DockAt = -1f, Angle, LabelWidth;
            public int Slot;
        }

        private const int StripeCount = 5;
        private static readonly Color Lamp = new Color(0.95f, 0.16f, 0.12f, 1f);

        private readonly List<Polaroid> photos = new List<Polaroid>();
        private readonly List<Image> brackets = new List<Image>();
        private readonly List<Image> lockLines = new List<Image>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private readonly Image[] stripes = new Image[StripeCount];
        private Image lamp, crossAcross, crossUp, blast;
        private RawImage scan, grain, grade, alertEdge;
        private TextMeshProUGUI feed, telemetry, banner, lockLabel, lostText;
        private float nextReadout, nextGlitch, glitchUntil;
        private Canvas canvas;
        private RectTransform root;
        private Image top, bottom, fade, shutter, band, plate, plateAccent, hintPlate, hintAccent, rail, stampMark;
        private RawImage vignette;
        private Texture2D vignetteTexture, dotTexture, shadowTexture, paperTexture;
        private TextMeshProUGUI caption, skip, cardTitle, cardText, callsign, role, line, hint, stamp;
        private RectTransform dialogue, hintBox;
        private int shown;

        // Cutscene state runs on real time; everything else on game time, so a paused game holds the words with the story.
        private bool cut;
        private float cutChanged = -100f, captionShown = -100f, captionFrom = -100f, flash = -100f, snap = -100f;
        private float cardFrom, cardUntil = -1f, lineFrom, lineUntil = -1f, hintFrom, hintUntil = -1f;
        private bool cardInCut;
        private CutFx fx;
        private float shotSeconds = 8f;
        private string titleFull = "";

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

            vignetteTexture = Texture(64, 64, TextureWrapMode.Clamp, FilterMode.Bilinear, (x, y) =>
            {
                float dx = (x + 0.5f) / 64f * 2f - 1f, dy = (y + 0.5f) / 64f * 2f - 1f;
                // 0 inside 0.6 of the half-width, rising smoothly to the corners (Mathf.SmoothStep is an interpolation, not this).
                float t = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.6f) / 0.8f);
                return new Color32(255, 255, 255, (byte)(t * t * (3f - 2f * t) * 170f));
            });
            // A pin's head: a round dot, lit from the upper left.
            dotTexture = Texture(32, 32, TextureWrapMode.Clamp, FilterMode.Bilinear, (x, y) =>
            {
                float u = (x + 0.5f) / 32f, v = (y + 0.5f) / 32f;
                float edge = Mathf.Clamp01((0.46f - Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f))) * 24f);
                byte light = (byte)Mathf.Lerp(255f, 150f, Mathf.Clamp01(2.2f * Mathf.Sqrt((u - 0.42f) * (u - 0.42f) + (v - 0.58f) * (v - 0.58f))));
                return new Color32(light, light, light, (byte)(edge * 255f));
            });
            // A soft shadow: solid in the middle, fading out over the outer fifth.
            shadowTexture = Texture(64, 64, TextureWrapMode.Clamp, FilterMode.Bilinear, (x, y) =>
            {
                float ox = Mathf.Max(Mathf.Abs((x + 0.5f) / 32f - 1f) - 0.7f, 0f), oy = Mathf.Max(Mathf.Abs((y + 0.5f) / 32f - 1f) - 0.7f, 0f);
                float t = 1f - Mathf.Clamp01(Mathf.Sqrt(ox * ox + oy * oy) / 0.3f);
                return new Color32(0, 0, 0, (byte)(t * t * (3f - 2f * t) * 255f));
            });
            var noise = new System.Random(4171);
            paperTexture = Texture(64, 64, TextureWrapMode.Repeat, FilterMode.Bilinear, (x, y) =>
            {
                byte v = (byte)(232 + noise.Next(24));
                return new Color32(v, v, v, 255);
            });

            vignette = Raw(root, "Vignette", vignetteTexture);
            AvLay.Fill(vignette.rectTransform);
            alertEdge = Raw(root, "AlertEdge", vignetteTexture);
            AvLay.Fill(alertEdge.rectTransform);
            // Light leaks at the top and bottom of the frame: alpha 1 at the edges, half in the middle.
            grade = Raw(root, "Grade", Texture(1, 32, TextureWrapMode.Clamp, FilterMode.Bilinear,
                (x, y) => new Color32(255, 255, 255, (byte)(255f * (0.5f + 0.5f * Mathf.Abs((y + 0.5f) / 16f - 1f))))));
            AvLay.Fill(grade.rectTransform);
            scan = Raw(root, "ScanLines", Texture(1, 4, TextureWrapMode.Repeat, FilterMode.Point, (x, y) => new Color32(0, 0, 0, (byte)(y == 2 ? 255 : 0))));
            scan.uvRect = new Rect(0f, 0f, 1f, 270f);
            AvLay.Fill(scan.rectTransform);
            // Film grain, and the static a lost signal turns into.
            grain = Raw(root, "Grain", Texture(128, 128, TextureWrapMode.Repeat, FilterMode.Point, (x, y) =>
            {
                byte v = (byte)noise.Next(256);
                return new Color32(v, v, v, 255);
            }));
            AvLay.Fill(grain.rectTransform);
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
            // The tracking box: an L at each corner, pivoted on its corner so the box closes by moving them.
            for (int corner = 0; corner < 4; corner++)
            {
                var pivot = new Vector2(corner % 2, corner / 2);
                Image across = AvLay.Solid(root, "LockAcross" + corner, Color.clear), up = AvLay.Solid(root, "LockUp" + corner, Color.clear);
                Anchor(across.rectTransform, middle, middle, pivot, Vector2.zero, new Vector2(34f, 3f));
                Anchor(up.rectTransform, middle, middle, pivot, Vector2.zero, new Vector2(3f, 34f));
                lockLines.Add(across);
                lockLines.Add(up);
            }
            lockLabel = Text(root, "LockLabel", AvTextRole.DataStrong, 16f, TextAlignmentOptions.Center, false);
            lockLabel.characterSpacing = 8f;
            Anchor(lockLabel.rectTransform, middle, middle, middle, Vector2.zero, new Vector2(400f, 24f));
            lostText = Text(root, "SignalLost", AvTextRole.Headline, 54f, TextAlignmentOptions.Center, false);
            lostText.characterSpacing = 18f;
            lostText.text = "NO SIGNAL";
            Anchor(lostText.rectTransform, middle, middle, middle, Vector2.zero, new Vector2(900f, 80f));
            banner = Text(root, "AlertBanner", AvTextRole.DataStrong, 20f, TextAlignmentOptions.Center, false);
            banner.characterSpacing = 12f;
            banner.text = "// FLASH  TRAFFIC  //  FLASH  TRAFFIC //";
            Anchor(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -bar - 84f), new Vector2(1000f, 28f));

            lamp = AvLay.Solid(root, "RecordingLamp", Color.clear);
            Anchor(lamp.rectTransform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-64f, -bar - 46f), new Vector2(12f, 12f));
            feed = Text(root, "Feed", AvTextRole.DataStrong, 16f, TextAlignmentOptions.TopRight, false);
            feed.characterSpacing = 6f;
            Anchor(feed.rectTransform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-88f, -bar - 41f), new Vector2(760f, 24f));
            telemetry = Text(root, "Telemetry", AvTextRole.DataStrong, 16f, TextAlignmentOptions.BottomLeft, false);
            telemetry.characterSpacing = 6f;
            Anchor(telemetry.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(64f, bar + 38f), new Vector2(1200f, 24f));
            // The red string the docked pictures are pinned along.
            rail = AvLay.Solid(root, "TimelineRail", Color.clear);
            Anchor(rail.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), FirstSlot + new Vector2(0f, PinRise), new Vector2(0f, 2f));

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

            blast = AvLay.Solid(root, "Blast", Color.clear);
            AvLay.Fill(blast.rectTransform);
            shutter = AvLay.Solid(root, "Shutter", Color.clear);
            AvLay.Fill(shutter.rectTransform);
            fade = AvLay.Solid(root, "Fade", Color.clear);
            AvLay.Fill(fade.rectTransform);
            Paint();
        }

        /// <summary>A small generated texture, freed with the view.</summary>
        private Texture2D Texture(int width, int height, TextureWrapMode wrap, FilterMode filter, System.Func<int, int, Color32> pixel)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = wrap, filterMode = filter, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = pixel(x, y);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            textures.Add(texture);
            return texture;
        }

        private static RawImage Raw(RectTransform parent, string name, Texture texture)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<RawImage>();
            image.transform.SetParent(parent, false);
            image.raycastTarget = false;
            image.texture = texture;
            image.color = Color.clear;
            return image;
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

        internal void HideDialogue()
        {
            lineUntil = -1f;
            Paint();
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
            titleFull = "";
            fx = CutFx.None;
            feedOn = true;
            HideBoards();
            shown = 0;
            foreach (Polaroid photo in photos)
            {
                photo.Root.gameObject.SetActive(false);
                photo.Pin.color = Color.clear;
                photo.Day.color = Color.clear;
            }
        }

        /// <summary>
        /// The words of the shot that just started: a caption in the lower bar or a title card over the picture, a stamp typed in the corner, and a picture
        /// that lands beside the action. The picture of the shot before moves up into the timeline. The cut's effects dress this shot alone. A cut that
        /// names a board puts that board up instead, and the feed with its pictures stays hidden for the shot. True when a picture or a page landed.
        /// </summary>
        internal bool Caption(ScenarioCut cutWords, Texture picture, string video = null)
        {
            string title = cutWords.Title, words = cutWords.Caption, stampText = cutWords.Stamp, before = caption.text;
            float now = Time.unscaledTime;
            captionShown = now;
            fx = cutWords.Fx;
            shotSeconds = cutWords.Shot != null ? cutWords.Shot.Seconds : 8f;
            nextGlitch = now + 0.4f;
            feedOn = cutWords.Board == null || cutWords.Board == "feed";
            for (int i = 0; i < shown; i++)
                if (photos[i].DockAt < 0f) photos[i].DockAt = now;
            if (!feedOn)
            {
                cardInCut = false;
                titleFull = "";
                stamp.text = "";
                bool landed = ShowBoard(cutWords, picture, video);
                // A line of narration carried over a cut stays up instead of fading in again.
                if (caption.text != before) captionFrom = now;
                return landed;
            }
            HideBoards();
            cardInCut = !string.IsNullOrEmpty(title);
            titleFull = cardInCut ? title : "";
            if (cardInCut)
            {
                SetCard(title, words);
                caption.text = "";
            }
            else caption.text = words ?? "";
            if (caption.text != before) captionFrom = now;
            stamp.text = stampText ?? "";
            stamp.maxVisibleCharacters = 0;
            blast.transform.SetAsLastSibling();
            shutter.transform.SetAsLastSibling();
            fade.transform.SetAsLastSibling();
            if (picture == null || shown >= MaxPhotos) return false;

            Polaroid landing = shown < photos.Count ? photos[shown] : Make();
            landing.Slot = shown++;
            landing.Photo.texture = picture;
            // The stamp reads "PLACE - DATE / ... / DAY 9": the day goes on the picture and under its place in the timeline, the place and date in small hand beside it.
            string full = stampText ?? "", day = full, note = "";
            int slash = full.LastIndexOf('/');
            if (slash >= 0)
            {
                day = full.Substring(slash + 1).Trim();
                note = full.Substring(0, full.IndexOf('/')).Trim();
                int dash = note.LastIndexOf(" - ", System.StringComparison.Ordinal);
                if (dash > 0) note = note.Substring(0, dash).Trim() + "\n" + note.Substring(dash + 3).Trim();
            }
            landing.Label.text = day;
            landing.Note.text = note;
            landing.Day.text = day;
            landing.LabelWidth = Mathf.Min(200f, landing.Label.GetPreferredValues(day).x + 6f);
            landing.LandedAt = now;
            landing.DockAt = -1f;
            // Each picture lies a little crooked, the same way every time the cutscene plays.
            landing.Angle = (landing.Slot * 37 % 11 - 5) * 1.3f;
            landing.TapeStrip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (landing.Slot * 53 % 7 - 3) * 2.2f);
            landing.Root.gameObject.SetActive(true);
            landing.Day.transform.SetAsLastSibling();
            landing.Root.SetAsLastSibling();
            landing.Pin.transform.SetAsLastSibling();
            blast.transform.SetAsLastSibling();
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
            fx = CutFx.None;
        }

        private Polaroid Make()
        {
            var photo = new Polaroid();
            float width = PhotoWidth + 2f * Border, height = PhotoHeight + Border + LabelBand;
            var centre = new Vector2(0.5f, 0.5f);
            var topCentre = new Vector2(0.5f, 1f);
            photo.Root = AvLay.Child(root, "Polaroid" + photos.Count);
            Anchor(photo.Root, new Vector2(0f, 1f), new Vector2(0f, 1f), centre, HeldAt, new Vector2(width, height));
            photo.Shadow = Raw(photo.Root, "Shadow", shadowTexture);
            Anchor(photo.Shadow.rectTransform, Vector2.zero, Vector2.one, centre, new Vector2(12f, -16f), new Vector2(46f, 46f));
            photo.Frame = Raw(photo.Root, "Frame", paperTexture);
            photo.Frame.uvRect = new Rect(0f, 0f, 3f, 3f);
            AvLay.Fill(photo.Frame.rectTransform);
            // A thin dark line round the print, where the picture meets the card.
            photo.Edge = AvLay.Solid(photo.Root, "Edge", Color.clear);
            Anchor(photo.Edge.rectTransform, topCentre, topCentre, topCentre, new Vector2(0f, -Border + 2f), new Vector2(PhotoWidth + 4f, PhotoHeight + 4f));
            photo.Photo = Raw(photo.Root, "Photo", null);
            Anchor(photo.Photo.rectTransform, topCentre, topCentre, topCentre, new Vector2(0f, -Border), new Vector2(PhotoWidth, PhotoHeight));
            // The milky film a fresh print clears from as it develops.
            photo.Mist = AvLay.Solid(photo.Root, "Mist", Color.clear);
            Anchor(photo.Mist.rectTransform, topCentre, topCentre, topCentre, new Vector2(0f, -Border), new Vector2(PhotoWidth, PhotoHeight));
            photo.TapeStrip = AvLay.Solid(photo.Root, "Tape", Color.clear);
            Anchor(photo.TapeStrip.rectTransform, topCentre, topCentre, centre, new Vector2(0f, -2f), new Vector2(130f, 34f));

            photo.Label = Text(photo.Root, "Label", AvTextRole.DataStrong, 30f, TextAlignmentOptions.BottomLeft, false);
            photo.Label.characterSpacing = 6f;
            Anchor(photo.Label.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Border + 4f, 18f), new Vector2(210f, 38f));
            photo.Label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 2f);
            // A marker stroke under the day, drawn on as the print develops.
            photo.Underline = AvLay.Solid(photo.Root, "Underline", Color.clear);
            Anchor(photo.Underline.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Border + 2f, 13f), new Vector2(0f, 3f));
            photo.Underline.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 1.5f);
            photo.Note = Text(photo.Root, "Note", AvTextRole.Label, 13f, TextAlignmentOptions.BottomRight, false);
            photo.Note.overflowMode = TextOverflowModes.Ellipsis;
            photo.Note.characterSpacing = 2f;
            Anchor(photo.Note.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Border - 2f, 14f), new Vector2(200f, 38f));

            // The day under the picture's place in the timeline, where the picture's own label is too small to read, and the pin it hangs from.
            photo.Day = Text(root, "Day" + photos.Count, AvTextRole.DataStrong, 18f, TextAlignmentOptions.Left, false);
            Anchor(photo.Day.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(150f, 22f));
            photo.Pin = Raw(root, "Pin" + photos.Count, dotTexture);
            Anchor(photo.Pin.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), centre, Vector2.zero, new Vector2(14f, 14f));
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

        private bool Has(CutFx effect) => cut && feedOn && (fx & effect) != 0;

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

            bool any = bars > 0.001f || dip > 0.001f || cardAlpha > 0.001f || lineAlpha > 0.001f || hintAlpha > 0.001f || CombatShown;
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
            vignette.color = Color.black.WithAlpha(bars * (0.9f + 0.1f * Mathf.PerlinNoise(real * 7f, 0.3f)));
            float feedBars = feedOn ? bars : 0f;
            PaintFeed(real, feedBars);
            PaintEffects(real, feedBars);
            PaintBoards(real, bars);
            // The camera's shutter: a short white blink as a picture lands.
            shutter.color = Color.white.WithAlpha(cut ? 0.28f * (1f - Mathf.Clamp01((real - snap) / 0.22f)) : 0f);

            float shownAlpha = cut ? Mathf.Clamp01((real - captionFrom) / 0.4f) : 0f;
            caption.color = Color.white.WithAlpha(shownAlpha);
            skip.color = Color.white.WithAlpha(cut ? 0.5f * bars : 0f);
            bool stamped = cut && feedOn && stamp.text.Length > 0;
            stamp.color = AvTheme.Warning.WithAlpha(stamped ? 1f : 0f);
            stampMark.color = AvTheme.Warning.WithAlpha(stamped ? 1f : 0f);
            if (stamped) stamp.maxVisibleCharacters = (int)((real - captionShown) * StampCharsPerSecond);

            PaintPhotos(real, feedBars);

            if (cardInCut && cut) Decode(real - captionShown);
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
            PaintCombat(game, real);
        }

        /// <summary>A cutscene title comes in scrambled and resolves left to right.</summary>
        private void Decode(float age)
        {
            if (titleFull.Length == 0) return;
            int resolved = (int)(age * 26f);
            if (resolved >= titleFull.Length)
            {
                if (cardTitle.text != titleFull) cardTitle.text = titleFull;
                return;
            }
            char[] chars = titleFull.ToCharArray();
            for (int i = resolved; i < chars.Length; i++)
                if (chars[i] != ' ') chars[i] = Glyphs[Random.Range(0, Glyphs.Length)];
            cardTitle.text = new string(chars);
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
            Color cross = Paper.WithAlpha(cardInCut || Has(CutFx.Lock) ? 0f : 0.3f * on);
            crossAcross.color = cross;
            crossUp.color = cross;

            // A burst on every cut, a long one on a blast, and short ones at random through a glitching shot.
            float since = real - captionShown;
            float burst = cut && feedOn ? Mathf.Max(1f - Mathf.Clamp01(since / 0.24f), Has(CutFx.Strike) ? 1f - Mathf.Clamp01(since / 0.8f) : 0f) : 0f;
            if (Has(CutFx.Glitch) && real >= nextGlitch)
            {
                glitchUntil = real + Random.Range(0.05f, 0.2f);
                nextGlitch = real + Random.Range(0.4f, 1.5f);
            }
            bool glitching = Has(CutFx.Glitch) && real < glitchUntil;
            if (glitching) burst = Mathf.Max(burst, 0.85f);
            // Torn words: the caption and the stamp jump sideways while the feed breaks up.
            caption.rectTransform.anchoredPosition = new Vector2(glitching ? Random.Range(-18f, 18f) : 0f, 16f);
            stamp.rectTransform.anchoredPosition = new Vector2(86f + (glitching ? Random.Range(-10f, 10f) : 0f), -1080f * BarShare - 44f);
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

        /// <summary>Grain always; the shot's own effects on top: grade, red alert, blast, tracking lock, lost signal.</summary>
        private void PaintEffects(float real, float bars)
        {
            float on = cut ? bars : 0f, since = real - captionShown;

            Color tint = Has(CutFx.Warm) ? new Color(1f, 0.6f, 0.25f, 0.08f) : Has(CutFx.Cold) ? new Color(0.4f, 0.62f, 1f, 0.07f) :
                Has(CutFx.Night) ? new Color(0.25f, 1f, 0.4f, 0.24f) : Color.clear;
            grade.color = tint.WithAlpha(tint.a * on);

            // The last second of a lost shot drops to static.
            float lost = Has(CutFx.Lost) ? Mathf.Clamp01((since - (shotSeconds - 1.1f)) / 0.2f) : 0f;
            grain.uvRect = new Rect(Random.value, Random.value, 15f, 8.4f);
            grain.color = Color.white.WithAlpha(bars * (Has(CutFx.Night) ? 0.16f : 0.055f) + 0.88f * lost);
            lostText.color = Paper.WithAlpha(lost * ((int)(real * 4f) % 2 == 0 ? 1f : 0.45f));

            float pulse = 0.5f + 0.5f * Mathf.Sin(real * 5f);
            alertEdge.color = AvTheme.Alert.WithAlpha(Has(CutFx.Alert) ? on * (0.2f + 0.35f * pulse) : 0f);
            banner.color = AvTheme.Alert.WithAlpha(Has(CutFx.Alert) ? on * ((int)(real * 2.5f) % 2 == 0 ? 1f : 0.35f) : 0f);

            float hit = Has(CutFx.Strike) ? 1f - Mathf.Clamp01(since / 0.7f) : 0f;
            blast.color = new Color(1f, 0.88f, 0.66f, 0.9f * hit * hit);

            // The tracking box closes for most of a second, blinking, then holds locked in red.
            float locking = Has(CutFx.Lock) ? on : 0f;
            float close = Mathf.Clamp01((since - 0.25f) / 0.9f);
            bool locked = close >= 1f;
            float half = Mathf.Lerp(230f, 64f, close * close * (3f - 2f * close));
            Color boxInk = (locked ? AvTheme.Alert : AvTheme.Warning).WithAlpha(locking * (locked || (int)(real * 8f) % 2 == 0 ? 1f : 0.35f));
            for (int corner = 0; corner < 4; corner++)
            {
                var at = new Vector2(corner % 2 == 0 ? -half : half, corner / 2 == 0 ? -half : half);
                lockLines[2 * corner].rectTransform.anchoredPosition = at;
                lockLines[2 * corner + 1].rectTransform.anchoredPosition = at;
                lockLines[2 * corner].color = boxInk;
                lockLines[2 * corner + 1].color = boxInk;
            }
            lockLabel.text = locked ? "TGT LOCKED // TRK 0417" : "ACQUIRING";
            lockLabel.rectTransform.anchoredPosition = new Vector2(0f, -half - 22f);
            lockLabel.color = boxInk;
        }

        /// <summary>
        /// Each picture drops in with a bounce, develops out of a murky grey, is held swaying a little, then travels up to its place in the timeline and is
        /// pinned to the red string that joins the places taken.
        /// </summary>
        private void PaintPhotos(float real, float bars)
        {
            int docked = 0;
            for (int i = 0; i < shown && i < photos.Count; i++)
            {
                Polaroid photo = photos[i];
                float age = real - photo.LandedAt;
                float land = Mathf.Clamp01(age / LandSeconds);
                // Ease out with an overshoot: the print slaps down and settles.
                float settle = 1f + 2.70158f * Mathf.Pow(land - 1f, 3f) + 1.70158f * Mathf.Pow(land - 1f, 2f);
                float develop = Mathf.Clamp01((age - 0.15f) / DevelopSeconds);
                float dock = photo.DockAt < 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((real - photo.DockAt) / DockSeconds));
                if (dock >= 1f) docked = i + 1;
                Vector2 slot = FirstSlot + new Vector2(photo.Slot * SlotStep, 0f);
                photo.Root.anchoredPosition = Vector2.Lerp(HeldAt, slot, dock);
                float scale = Mathf.Lerp(Mathf.LerpUnclamped(1.45f, 1f, settle), DockScale, dock);
                photo.Root.localScale = new Vector3(scale, scale, 1f);
                float sway = (1f - dock) * land * 0.7f * Mathf.Sin(age * 0.9f + photo.Slot);
                photo.Root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(photo.Angle + 12f * (1f - settle), photo.Angle * 0.4f, dock) + sway);

                float alpha = bars * Mathf.Clamp01(land * 3f), held = alpha * (1f - dock);
                photo.Frame.color = Paper.WithAlpha(alpha);
                photo.Edge.color = Ink.WithAlpha(0.75f * alpha);
                Color print = Color.Lerp(Undeveloped, Color.white, Mathf.Sqrt(develop));
                photo.Photo.color = Color.Lerp(print, Sepia, 0.85f * dock).WithAlpha(alpha);
                photo.Mist.color = Haze.WithAlpha(alpha * 0.85f * (1f - develop) * (1f - develop));
                photo.Shadow.color = Color.black.WithAlpha(0.5f * alpha);
                photo.TapeStrip.color = Tape.WithAlpha(0.6f * alpha);
                photo.Label.color = Ink.WithAlpha(held);
                photo.Note.color = Ink.WithAlpha(0.7f * held);
                photo.Underline.rectTransform.sizeDelta = new Vector2(photo.LabelWidth * Mathf.Clamp01((age - 0.5f) / 0.35f), 3f);
                photo.Underline.color = Marker.WithAlpha(held);

                photo.Day.rectTransform.anchoredPosition = slot + new Vector2(84f, 0f);
                photo.Day.color = Paper.WithAlpha(bars * dock);
                photo.Pin.rectTransform.anchoredPosition = slot + new Vector2(0f, PinRise);
                photo.Pin.color = Marker.WithAlpha(bars * Mathf.Clamp01(dock * 2f - 1f));
            }
            float length = docked > 1 ? (docked - 1) * SlotStep : 0f;
            rail.rectTransform.sizeDelta = new Vector2(length, 2f);
            rail.color = Marker.WithAlpha(docked > 1 ? 0.8f * bars : 0f);
            if (bars > 0.001f || cut) return;
            foreach (Polaroid photo in photos)
            {
                if (photo.Root.gameObject.activeSelf) photo.Root.gameObject.SetActive(false);
                photo.Pin.color = photo.Day.color = Color.clear;
            }
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
            foreach (Texture2D texture in textures)
                if (texture != null) Destroy(texture);
            if (screenTexture != null) screenTexture.Release();
            Destroy(screenTexture);
            DestroyLook();
        }
    }
}
