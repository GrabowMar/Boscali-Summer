using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace BoscaliSummer.Modules.Scenario.Presentation
{
    /// <summary>
    /// A mission script's prerendered video, fallback transmission plate (normal dialogue uses COMMS), title cards, hints and dips to black, plus the countdown and the ace
    /// bar (ScenarioStageView.Combat.cs). One overlay canvas above the game's, switched off whenever nothing shows. It takes no input: nothing here can
    /// be clicked.
    /// </summary>
    internal sealed partial class ScenarioStageView : MonoBehaviour
    {
        private const int SortingOrder = 30004;
        private const float TypeCharsPerSecond = 60f, PlateWidth = 1040f, PlatePad = 24f, HintWidth = 780f;

        private Canvas canvas;
        private RectTransform root;
        private Image fade, band, plate, plateAccent, hintPlate, hintAccent;
        private TextMeshProUGUI cardTitle, cardText, callsign, role, line, hint;
        private RectTransform dialogue, hintBox;

        // The dip runs on real time; everything else on game time, so a paused game holds the words with the story.
        private float flash = -100f;
        private float cardFrom, cardUntil = -1f, lineFrom, lineUntil = -1f, hintFrom, hintUntil = -1f;

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

            band = AvLay.Solid(root, "CardBand", Color.clear);
            Anchor(band.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 250f));
            cardTitle = Text(root, "CardTitle", AvTextRole.Headline, 76f, TextAlignmentOptions.Center, false);
            cardTitle.characterSpacing = 14f;
            Anchor(cardTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 34f), new Vector2(-240f, 100f));
            cardText = Text(root, "CardText", AvTextRole.Prose, 28f, TextAlignmentOptions.Center, true);
            Anchor(cardText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -52f), new Vector2(-420f, 80f));

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

            fade = AvLay.Solid(root, "Fade", Color.clear);
            AvLay.Fill(fade.rectTransform);
            Paint();
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
            cardTitle.text = title ?? "";
            cardText.text = words ?? "";
            // Words alone sit in the middle of the band; under a title they sit below it.
            cardText.rectTransform.anchoredPosition = new Vector2(0f, string.IsNullOrEmpty(title) ? 0f : -52f);
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

        private VideoPlayer video;
        private RenderTexture videoTexture;
        private RawImage videoImage;
        private Image videoBack;
        private TextMeshProUGUI videoSkip;

        /// <summary>A video is on screen, or loading to be.</summary>
        internal bool VideoPlaying { get; private set; }

        /// <summary>
        /// Plays a video file over the whole screen, letterboxed on black, with its own sound; it runs on real time, so a paused game does not hold it.
        /// A file Unity cannot play is reported through <paramref name="failed"/> and ends at once.
        /// </summary>
        internal void PlayVideo(string path, System.Action<string> failed)
        {
            if (video == null)
            {
                videoBack = AvLay.Solid(root, "VideoBack", Color.black);
                AvLay.Fill(videoBack.rectTransform);
                videoTexture = new RenderTexture(1920, 1080, 0) { name = "ScenarioVideo" };
                var holder = new GameObject("Video", typeof(RectTransform), typeof(CanvasRenderer));
                holder.transform.SetParent(root, false);
                videoImage = holder.AddComponent<RawImage>();
                videoImage.raycastTarget = false;
                videoImage.texture = videoTexture;
                AvLay.Fill(videoImage.rectTransform);
                holder.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                holder.GetComponent<AspectRatioFitter>().aspectRatio = 16f / 9f;
                videoSkip = AvText.Make(root, "VideoSkip", AvTextRole.Label, "SPACE  SKIP", TextAlignmentOptions.Right, false);
                AvText.Size(videoSkip, 14f);
                videoSkip.raycastTarget = false;
                videoSkip.color = Color.white.WithAlpha(0.45f);
                RectTransform skip = videoSkip.rectTransform;
                skip.anchorMin = skip.anchorMax = skip.pivot = new Vector2(1f, 0f);
                skip.anchoredPosition = new Vector2(-40f, 14f);
                skip.sizeDelta = new Vector2(320f, 24f);
                video = gameObject.AddComponent<VideoPlayer>();
                video.playOnAwake = false;
                video.source = VideoSource.Url;
                video.renderMode = VideoRenderMode.RenderTexture;
                video.targetTexture = videoTexture;
                video.audioOutputMode = VideoAudioOutputMode.Direct;
                video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
                video.skipOnDrop = true;
                video.loopPointReached += _ => StopVideo();
                video.errorReceived += (_, why) => { failed?.Invoke(why); StopVideo(); };
            }
            RenderTexture was = RenderTexture.active;
            RenderTexture.active = videoTexture;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = was;
            VideoPlaying = true;
            fade.transform.SetAsLastSibling();
            video.url = path;
            video.Play();
            Paint();
        }

        /// <summary>Ends the video (its end, a skip, a mission that ends under it) with a dip through black.</summary>
        internal void StopVideo()
        {
            if (!VideoPlaying) return;
            VideoPlaying = false;
            video.Stop();
            Dip();
        }

        /// <summary>A quick fade through black, starting black now.</summary>
        internal void Dip() => flash = Time.unscaledTime;

        private void Update() => Paint();

        private void Paint()
        {
            float real = Time.unscaledTime, game = Time.time;
            float dip = 1f - Mathf.Clamp01((real - flash) / 0.9f);
            float cardAlpha = Window(game, cardFrom, cardUntil, 0.5f);
            float lineAlpha = Window(game, lineFrom, lineUntil, 0.25f);
            float hintAlpha = Window(game, hintFrom, hintUntil, 0.4f);

            if (videoBack != null && videoBack.enabled != VideoPlaying) videoBack.enabled = videoImage.enabled = videoSkip.enabled = VideoPlaying;
            bool any = VideoPlaying || dip > 0.001f || cardAlpha > 0.001f || lineAlpha > 0.001f || hintAlpha > 0.001f || CombatShown;
            if (canvas.enabled != any) canvas.enabled = any;
            if (!any) return;

            fade.color = new Color(0f, 0f, 0f, dip);

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

        private void OnDestroy()
        {
            if (videoTexture == null) return;
            videoTexture.Release();
            Destroy(videoTexture);
        }

        /// <summary>1 inside [from, until], easing over <paramref name="edge"/> seconds at both ends, 0 outside.</summary>
        private static float Window(float now, float from, float until, float edge)
        {
            if (now < from || now > until) return 0f;
            return Mathf.Clamp01(Mathf.Min((now - from) / edge, (until - now) / edge));
        }
    }
}
