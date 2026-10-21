using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Core.Scenario;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.Video;

namespace BoscaliSummer.Modules.Scenario.Presentation
{
    /// <summary>
    /// A cut's board (<c>board=</c>): what the shot is dressed as instead of the surveillance feed. Live footage is a TV news picture (<c>news</c>, two channels);
    /// between the footage come documents: a front page (<c>paper</c>), a situation map drawn on the game's own map (<c>map</c>), a satellite pass
    /// (<c>sat</c>), a contractor's file with a polaroid clipped on (<c>dossier</c>), and the title and
    /// chapter cards (<c>title</c>, <c>chapter</c>), or nothing but the narration (<c>clean</c>). Each board sits between the letterbox bars and has one move of its own. News and satellite boards also
    /// wash the picture through a URP volume (TV colour, or black and white) that exists only while they are up.
    /// </summary>
    internal sealed partial class ScenarioStageView
    {
        private const int MaxSegments = 160, MaxMarks = 6, MaxLabels = 8;
        private static readonly Color Navy = new Color(0.07f, 0.23f, 0.45f, 1f), Amber = new Color(0.88f, 0.65f, 0.15f, 1f), Newsprint = new Color(0.95f, 0.93f, 0.88f, 1f);
        private static readonly Color Manila = new Color(0.79f, 0.66f, 0.41f, 1f), MapInk = new Color(0.05f, 0.1f, 0.14f, 1f), Retreat = new Color(0.36f, 0.61f, 0.88f, 1f);
        private static readonly Vector2 TopLeft = new Vector2(0f, 1f), TopRight = Vector2.one, BottomLeft = Vector2.zero, Middle = new Vector2(0.5f, 0.5f);

        private readonly Dictionary<string, CanvasGroup> boards = new Dictionary<string, CanvasGroup>();
        private readonly Dictionary<Texture, Texture2D> greys = new Dictionary<Texture, Texture2D>();
        private string board, boardKey;
        private float boardFrom;
        private bool feedOn = true;

        // News
        private Image newsChannel, newsLive, newsKicker, newsHead, newsSub, newsTag;
        private TextMeshProUGUI newsChannelText, newsKickerText, newsHeadText, newsSubText, newsTagText, newsClock, newsTickerLabelText, newsTicker;
        private RectTransform newsLower;
        private RawImage phoneVignette;
        private float tickerWidth;
        // Paper
        private RectTransform paperSheet;
        private TextMeshProUGUI paperMast, paperDate, paperEdition, paperHead, paperDeck, paperBody;
        private RawImage paperPhoto;
        // Map
        private RawImage mapImage;
        private TextMeshProUGUI mapHead, mapDate, mapLegend;
        private readonly List<Image> segments = new List<Image>();
        private readonly List<TextMeshProUGUI> mapLabels = new List<TextMeshProUGUI>();
        /// <summary>A line on the map: its points on the board, its colour and width, when it starts drawing, and whether it ends in a head or is dashed.</summary>
        private sealed class MapLine { public readonly List<Vector2> Points = new List<Vector2>(); public Color Ink; public float Width, Delay; public bool Head, Dashed; }
        private readonly List<MapLine> mapLines = new List<MapLine>();
        // Satellite
        private RectTransform satArea;
        private TextMeshProUGUI satHeader, satDate;
        private RawImage satSweep;
        private readonly Image[] markLines = new Image[MaxMarks * 4];
        private readonly TextMeshProUGUI[] markLabels = new TextMeshProUGUI[MaxMarks];
        private readonly Unit[] markUnits = new Unit[MaxMarks];
        private readonly string[] markNames = new string[MaxMarks];
        private float nextMarkSearch;
        // Dossier
        private RectTransform folder, clip, stampBox;
        private TextMeshProUGUI dosCompany, dosDate, dosHeading, dosBody, dosStamp, clipNote;
        private readonly List<Image> stampEdges = new List<Image>();
        private RawImage clipPhoto;
        // Title and chapter
        private TextMeshProUGUI titleDate, titleBig, chapterNumber, chapterBig, chapterLine;
        private Image titleRule, chapterRule;
        // Screen: a motion clip rendered outside the game (tools/MotionScreens), played into a texture.
        private RawImage screenImage;
        private VideoPlayer screenPlayer;
        private RenderTexture screenTexture;
        // The washed picture under news and satellite boards.
        private GameObject lookHolder;
        private Volume look;
        private VolumeProfile lookProfile;
        private ColorAdjustments lookColour;
        private FilmGrain lookGrain;
        private ChromaticAberration lookFringe;

        /// <summary>Puts up the cut's board. True when it is paper the viewer should hear land (a front page, a file).</summary>
        private bool ShowBoard(ScenarioCut cutWords, Texture picture, string video)
        {
            // The next shot of the same story (same board, same headline) carries its graphics over the cut instead of building them again.
            string key = cutWords.Board + "|" + cutWords.Word("headline") + "|" + cutWords.Word("masthead") + "|" + cutWords.Word("heading");
            bool carried = board == cutWords.Board && key == boardKey;
            HideBoards();
            board = cutWords.Board;
            boardKey = key;
            if (!carried) boardFrom = Time.unscaledTime;
            CanvasGroup group = Board(board);
            if (!carried) group.alpha = 0f;
            group.gameObject.SetActive(true);
            group.transform.SetAsLastSibling();
            caption.transform.SetAsLastSibling();
            blast.transform.SetAsLastSibling();
            shutter.transform.SetAsLastSibling();
            fade.transform.SetAsLastSibling();
            // A title or chapter card carries its own words; every other board leaves the caption as the narration under the picture.
            caption.text = board == "chapter" ? "" : board == "title" ? cutWords.Caption ?? "" : cutWords.Caption ?? cutWords.Title ?? "";
            switch (board)
            {
                case "news": SetNews(cutWords); break;
                case "paper": SetPaper(cutWords, picture); break;
                case "map": SetMap(cutWords); break;
                case "sat": SetSat(cutWords); break;
                case "dossier": SetDossier(cutWords, picture); break;
                case "screen": SetScreen(video); break;
                case "title":
                    titleBig.text = (cutWords.Title ?? "").ToUpperInvariant();
                    titleDate.text = cutWords.Word("date", "");
                    break;
                case "chapter":
                    chapterNumber.text = cutWords.Word("number", "");
                    chapterBig.text = (cutWords.Title ?? cutWords.Caption ?? "").ToUpperInvariant();
                    chapterLine.text = cutWords.Title != null ? cutWords.Caption ?? "" : "";
                    break;
            }
            return board == "paper" || board == "dossier";
        }

        private void HideBoards()
        {
            foreach (CanvasGroup group in boards.Values)
                if (group.gameObject.activeSelf) group.gameObject.SetActive(false);
            if (screenPlayer != null) screenPlayer.Stop();
            board = null;
        }

        /// <summary>The size of the picture between the letterbox bars, in canvas units.</summary>
        private Vector2 Inner() => new Vector2(root.rect.width, root.rect.height * (1f - 2f * BarShare));

        private CanvasGroup Board(string name)
        {
            if (boards.TryGetValue(name, out CanvasGroup existing)) return existing;
            RectTransform area = AvLay.Child(root, "Board." + name);
            // A screen clip is the whole picture, over the letterbox bars; every other board stays between them.
            bool full = name == "screen";
            Anchor(area, full ? Vector2.zero : new Vector2(0f, BarShare), full ? Vector2.one : new Vector2(1f, 1f - BarShare), Middle, Vector2.zero, Vector2.zero);
            CanvasGroup group = area.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
            // A board stays between the bars, however its pieces move.
            area.gameObject.AddComponent<RectMask2D>();
            switch (name)
            {
                case "news": BuildNews(area); break;
                case "paper": BuildPaper(area); break;
                case "map": BuildMap(area); break;
                case "sat": BuildSat(area); break;
                case "dossier": BuildDossier(area); break;
                case "title": BuildTitle(area); break;
                case "chapter": BuildChapter(area); break;
                case "screen": BuildScreen(area); break;
            }
            boards[name] = group;
            return group;
        }

        private TextMeshProUGUI Words(RectTransform parent, string name, AvTextRole textRole, float size, Color ink, TextAlignmentOptions align, bool wrap)
        {
            TextMeshProUGUI text = Text(parent, name, textRole, size, align, wrap);
            text.color = ink;
            return text;
        }

        private RawImage Shadow(RectTransform parent, float spread, Vector2 offset, float strength)
        {
            RawImage shadow = Raw(parent, "Shadow", shadowTexture);
            Anchor(shadow.rectTransform, BottomLeft, Vector2.one, Middle, offset, new Vector2(spread, spread));
            shadow.color = new Color(0f, 0f, 0f, strength);
            return shadow;
        }

        // ---- Screen: a rendered clip filling the picture, on black. ----

        private void BuildScreen(RectTransform area)
        {
            Image black = AvLay.Solid(area, "Black", Color.black);
            AvLay.Fill(black.rectTransform);
            // The clips are 1920x1080: a full-screen picture should not be upscaled from less.
            screenTexture = new RenderTexture(1920, 1080, 0) { name = "ScenarioScreen" };
            screenImage = Raw(area, "Clip", screenTexture);
            screenImage.color = Color.white;
            AvLay.Fill(screenImage.rectTransform);
            AspectRatioFitter fit = screenImage.gameObject.AddComponent<AspectRatioFitter>();
            // Fills the screen; a wider screen crops a little off the top and bottom of the clip, never shows bars.
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = 16f / 9f;
            screenPlayer = area.gameObject.AddComponent<VideoPlayer>();
            screenPlayer.playOnAwake = false;
            screenPlayer.source = VideoSource.Url;
            screenPlayer.renderMode = VideoRenderMode.RenderTexture;
            screenPlayer.targetTexture = screenTexture;
            screenPlayer.audioOutputMode = VideoAudioOutputMode.None;
            screenPlayer.isLooping = false;
            screenPlayer.skipOnDrop = true;
            screenPlayer.waitForFirstFrame = true;
            screenPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            screenPlayer.prepareCompleted += player => player.Play();
            screenPlayer.errorReceived += (player, message) => Debug.LogWarning("[Scenario] screen clip: " + message);
        }

        /// <summary>Plays the clip from its first frame; the texture is black until it has one (the clips open on black).</summary>
        private void SetScreen(string video)
        {
            screenPlayer.Stop();
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = screenTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = before;
            if (string.IsNullOrEmpty(video)) return;
            screenPlayer.url = video;
            screenPlayer.Prepare();
        }

        // ---- News: a TV picture with the channel's own graphics. ----

        private void BuildNews(RectTransform area)
        {
            // A phone camera's dark corners, for footage a channel did not film itself.
            phoneVignette = Raw(area, "PhoneVignette", vignetteTexture);
            AvLay.Fill(phoneVignette.rectTransform);

            newsChannel = AvLay.Solid(area, "Channel", Navy);
            Anchor(newsChannel.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(56f, -34f), new Vector2(110f, 44f));
            newsChannelText = Words(newsChannel.rectTransform, "Text", AvTextRole.Headline, 26f, Color.white, TextAlignmentOptions.Center, false);
            AvLay.Fill(newsChannelText.rectTransform);
            newsLive = AvLay.Solid(area, "Live", Marker);
            Anchor(newsLive.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(172f, -34f), new Vector2(76f, 44f));
            TextMeshProUGUI live = Words(newsLive.rectTransform, "Text", AvTextRole.Headline, 24f, Color.white, TextAlignmentOptions.Center, false);
            live.text = "LIVE";
            AvLay.Fill(live.rectTransform);
            newsClock = Words(area, "Clock", AvTextRole.DataStrong, 20f, Color.white, TextAlignmentOptions.Right, false);
            Anchor(newsClock.rectTransform, TopRight, TopRight, TopRight, new Vector2(-56f, -44f), new Vector2(420f, 30f));
            newsTag = AvLay.Solid(area, "Tag", Amber);
            Anchor(newsTag.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(360f, 34f));
            newsTagText = Words(newsTag.rectTransform, "Text", AvTextRole.DataStrong, 18f, Ink, TextAlignmentOptions.Center, false);
            newsTagText.characterSpacing = 10f;
            AvLay.Fill(newsTagText.rectTransform);

            newsLower = AvLay.Child(area, "LowerThird");
            Anchor(newsLower, BottomLeft, BottomLeft, BottomLeft, new Vector2(56f, 112f), new Vector2(1400f, 150f));
            newsKicker = AvLay.Solid(newsLower, "Kicker", Marker);
            Anchor(newsKicker.rectTransform, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(200f, 36f));
            newsKickerText = Words(newsKicker.rectTransform, "Text", AvTextRole.Headline, 22f, Color.white, TextAlignmentOptions.Center, false);
            newsKickerText.characterSpacing = 10f;
            AvLay.Fill(newsKickerText.rectTransform);
            newsHead = AvLay.Solid(newsLower, "Headline", new Color(0.96f, 0.96f, 0.95f, 1f));
            Anchor(newsHead.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(0f, -36f), new Vector2(1000f, 64f));
            newsHeadText = Words(newsHead.rectTransform, "Text", AvTextRole.Headline, 42f, new Color(0.07f, 0.07f, 0.08f, 1f), TextAlignmentOptions.Left, false);
            Anchor(newsHeadText.rectTransform, BottomLeft, Vector2.one, Middle, Vector2.zero, new Vector2(-40f, 0f));
            newsSub = AvLay.Solid(newsLower, "Sub", Navy);
            Anchor(newsSub.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(0f, -100f), new Vector2(1000f, 40f));
            newsSubText = Words(newsSub.rectTransform, "Text", AvTextRole.Prose, 22f, Color.white, TextAlignmentOptions.Left, false);
            Anchor(newsSubText.rectTransform, BottomLeft, Vector2.one, Middle, Vector2.zero, new Vector2(-40f, 0f));

            Image strip = AvLay.Solid(area, "Ticker", new Color(0.03f, 0.04f, 0.06f, 0.88f));
            Anchor(strip.rectTransform, BottomLeft, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(0f, 50f));
            strip.gameObject.AddComponent<RectMask2D>();
            newsTicker = Words(strip.rectTransform, "Text", AvTextRole.Headline, 24f, new Color(0.95f, 0.95f, 0.95f, 1f), TextAlignmentOptions.Left, false);
            newsTicker.characterSpacing = 4f;
            Anchor(newsTicker.rectTransform, BottomLeft, TopLeft, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6000f, 0f));
            Image tickerLabel = AvLay.Solid(strip.rectTransform, "Label", new Color(1f, 0.82f, 0.29f, 1f));
            Anchor(tickerLabel.rectTransform, BottomLeft, TopLeft, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(110f, 0f));
            newsTickerLabelText = Words(tickerLabel.rectTransform, "Text", AvTextRole.Headline, 24f, Ink, TextAlignmentOptions.Center, false);
            AvLay.Fill(newsTickerLabelText.rectTransform);
        }

        private void SetNews(ScenarioCut words)
        {
            // brn is the Republic's own broadcaster (navy); any other channel is a foreign wire (black and amber, filmed on phones).
            string name = words.Word("channel", "brn");
            bool wire = name != "brn";
            string channel = wire ? name.ToUpperInvariant() : "BRN 1";
            newsChannelText.text = channel;
            newsChannel.color = wire ? new Color(0.06f, 0.06f, 0.07f, 1f) : Navy;
            newsChannelText.color = wire ? Amber : Color.white;
            newsChannel.rectTransform.sizeDelta = new Vector2(Mathf.Max(90f, newsChannelText.GetPreferredValues(channel).x + 28f), 44f);
            newsLive.rectTransform.anchoredPosition = new Vector2(56f + newsChannel.rectTransform.sizeDelta.x + 8f, -34f);
            newsClock.text = words.Word("clock", "");
            string tag = words.Word("tag");
            newsTag.gameObject.SetActive(tag != null);
            if (tag != null)
            {
                newsTagText.text = tag.ToUpperInvariant();
                newsTag.rectTransform.sizeDelta = new Vector2(newsTagText.GetPreferredValues(newsTagText.text).x + 40f, 34f);
            }
            newsKickerText.text = words.Word("kicker", "BREAKING").ToUpperInvariant();
            newsKicker.color = wire ? Amber : Marker;
            newsKickerText.color = wire ? Ink : Color.white;
            newsKicker.rectTransform.sizeDelta = new Vector2(newsKickerText.GetPreferredValues(newsKickerText.text).x + 36f, 36f);
            newsHeadText.text = words.Word("headline", "").ToUpperInvariant();
            newsSubText.text = words.Word("sub", "");
            newsSub.color = wire ? new Color(0.06f, 0.06f, 0.07f, 0.92f) : Navy;
            float width = Mathf.Clamp(Mathf.Max(newsHeadText.GetPreferredValues(newsHeadText.text).x, newsSubText.GetPreferredValues(newsSubText.text).x) + 48f, 500f, 1400f);
            newsHead.rectTransform.sizeDelta = new Vector2(width, 64f);
            newsSub.rectTransform.sizeDelta = new Vector2(width, 40f);
            newsSub.gameObject.SetActive(newsSubText.text.Length > 0);
            newsTickerLabelText.text = wire ? name.ToUpperInvariant() : "BRN";
            newsTicker.text = words.Word("ticker", "").Replace("|", "   •   ").ToUpperInvariant();
            tickerWidth = newsTicker.GetPreferredValues(newsTicker.text).x;
            phoneVignette.gameObject.SetActive(wire);
        }

        private void PaintNews(float age)
        {
            newsLive.color = Marker.WithAlpha((int)(Time.unscaledTime / 0.8f) % 2 == 0 ? 1f : 0.45f);
            // The lower third slides in from the left a moment after the cut.
            float slide = Mathf.Clamp01((age - 0.35f) / 0.35f);
            newsLower.anchoredPosition = new Vector2(Mathf.Lerp(-1500f, 56f, 1f - (1f - slide) * (1f - slide)), 112f);
            float start = Inner().x;
            newsTicker.rectTransform.anchoredPosition = new Vector2(start - (age * 170f) % (start + tickerWidth + 200f), 0f);
            phoneVignette.color = Color.black;
        }

        // ---- Paper: a front page dropped on the desk. ----

        private void BuildPaper(RectTransform area)
        {
            Image desk = AvLay.Solid(area, "Desk", new Color(0f, 0f, 0f, 0.5f));
            AvLay.Fill(desk.rectTransform);
            paperSheet = AvLay.Child(area, "Sheet");
            Anchor(paperSheet, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Middle, new Vector2(600f, 0f), new Vector2(860f, 880f));
            Shadow(paperSheet, 70f, new Vector2(22f, -26f), 0.6f);
            RawImage sheet = Raw(paperSheet, "Paper", paperTexture);
            sheet.uvRect = new Rect(0f, 0f, 5f, 5f);
            sheet.color = Newsprint;
            AvLay.Fill(sheet.rectTransform);
            Color ink = new Color(0.1f, 0.1f, 0.09f, 1f);
            var top = new Vector2(0.5f, 1f);
            paperMast = Words(paperSheet, "Masthead", AvTextRole.Headline, 62f, ink, TextAlignmentOptions.Center, false);
            paperMast.fontStyle = FontStyles.Bold | FontStyles.Italic;
            Anchor(paperMast.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -22f), new Vector2(-60f, 72f));
            Image ruleTop = AvLay.Solid(paperSheet, "RuleTop", ink);
            Anchor(ruleTop.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -100f), new Vector2(-60f, 2f));
            Image ruleBottom = AvLay.Solid(paperSheet, "RuleBottom", ink);
            Anchor(ruleBottom.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -132f), new Vector2(-60f, 4f));
            paperDate = Words(paperSheet, "Date", AvTextRole.Label, 15f, ink, TextAlignmentOptions.Left, false);
            Anchor(paperDate.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(30f, -104f), new Vector2(500f, 26f));
            paperEdition = Words(paperSheet, "Edition", AvTextRole.Label, 15f, ink, TextAlignmentOptions.Right, false);
            Anchor(paperEdition.rectTransform, TopRight, TopRight, TopRight, new Vector2(-30f, -104f), new Vector2(300f, 26f));
            paperHead = Words(paperSheet, "Headline", AvTextRole.Headline, 92f, ink, TextAlignmentOptions.Center, false);
            paperHead.fontStyle = FontStyles.Bold;
            Anchor(paperHead.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -140f), new Vector2(-60f, 104f));
            paperDeck = Words(paperSheet, "Deck", AvTextRole.Prose, 22f, ink, TextAlignmentOptions.Center, true);
            paperDeck.fontStyle = FontStyles.Italic;
            Anchor(paperDeck.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -248f), new Vector2(-90f, 60f));
            paperPhoto = Raw(paperSheet, "Photo", null);
            paperPhoto.color = new Color(0.92f, 0.9f, 0.86f, 1f);
            Anchor(paperPhoto.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -316f), new Vector2(-60f, 380f));
            paperBody = Words(paperSheet, "Body", AvTextRole.Prose, 17f, ink, TextAlignmentOptions.Justified, true);
            Anchor(paperBody.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -712f), new Vector2(-60f, 150f));
        }

        private void SetPaper(ScenarioCut words, Texture picture)
        {
            paperMast.text = words.Word("masthead", "The Daily");
            paperDate.text = words.Word("date", "").ToUpperInvariant();
            paperEdition.text = words.Word("edition", "").ToUpperInvariant();
            paperHead.text = words.Word("headline", "").ToUpperInvariant();
            paperDeck.text = words.Word("sub", "");
            paperBody.text = words.Word("body", "");
            paperPhoto.gameObject.SetActive(picture != null);
            if (picture != null) SetPicture(paperPhoto, Grey(picture), 800f / 380f);
        }

        private void PaintPaper(float age)
        {
            // Dropped onto the desk: falls from a little higher, turned further, and settles.
            float t = Mathf.Clamp01(age / 0.7f), ease = 1f - (1f - t) * (1f - t) * (1f - t);
            paperSheet.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-9f, -3f, ease));
            float scale = Mathf.Lerp(1.12f, 1f, ease) * Mathf.Min(1f, Inner().y / 900f);
            paperSheet.localScale = new Vector3(scale, scale, 1f);
            paperSheet.anchoredPosition = new Vector2(600f, Mathf.Lerp(60f, 0f, ease));
        }

        // ---- Map: a situation map on the game's own map picture, with arrows that draw forward. ----

        private void BuildMap(RectTransform area)
        {
            Image ground = AvLay.Solid(area, "Ground", MapInk);
            AvLay.Fill(ground.rectTransform);
            mapImage = Raw(area, "Map", null);
            mapImage.color = new Color(0.55f, 0.6f, 0.57f, 1f);
            AvLay.Fill(mapImage.rectTransform);
            RawImage corners = Raw(area, "Vignette", vignetteTexture);
            corners.color = Color.black;
            AvLay.Fill(corners.rectTransform);
            RectTransform marks = AvLay.Child(area, "Marks");
            AvLay.Fill(marks);
            for (int i = 0; i < MaxSegments; i++)
            {
                Image segment = AvLay.Solid(marks, "Segment" + i, Color.clear);
                Anchor(segment.rectTransform, Middle, Middle, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
                segments.Add(segment);
            }
            for (int i = 0; i < MaxLabels; i++)
            {
                TextMeshProUGUI label = Words(marks, "Label" + i, AvTextRole.Headline, 34f, Paper, TextAlignmentOptions.Left, false);
                label.characterSpacing = 10f;
                Anchor(label.rectTransform, Middle, Middle, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(600f, 44f));
                mapLabels.Add(label);
            }
            mapHead = Words(area, "Headline", AvTextRole.DataStrong, 20f, Paper.WithAlpha(0.85f), TextAlignmentOptions.TopLeft, false);
            mapHead.characterSpacing = 8f;
            Anchor(mapHead.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(64f, -48f), new Vector2(900f, 28f));
            mapDate = Words(area, "Date", AvTextRole.DataStrong, 18f, AvTheme.Warning, TextAlignmentOptions.TopLeft, false);
            mapDate.characterSpacing = 8f;
            Anchor(mapDate.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(64f, -80f), new Vector2(900f, 26f));
            mapLegend = Words(area, "Legend", AvTextRole.DataStrong, 15f, Paper.WithAlpha(0.6f), TextAlignmentOptions.BottomLeft, false);
            mapLegend.characterSpacing = 6f;
            Anchor(mapLegend.rectTransform, BottomLeft, BottomLeft, BottomLeft, new Vector2(64f, 40f), new Vector2(1200f, 24f));
        }

        private void SetMap(ScenarioCut words)
        {
            mapHead.text = words.Word("headline", "").ToUpperInvariant();
            mapDate.text = words.Word("date", "").ToUpperInvariant();
            mapLegend.text = words.Word("legend", "- - FRONT LINE      RED: ADVANCE      BLUE: WITHDRAWAL").ToUpperInvariant();
            Vector2 inner = Inner();
            // area="x z width": the stretch of the map to show, in metres; its height follows the board's shape.
            float[] view = Numbers(words.Word("area", "0 0 20000"));
            float cx = view.Length > 0 ? view[0] : 0f, cz = view.Length > 1 ? view[1] : 0f, width = view.Length > 2 && view[2] > 100f ? view[2] : 20000f;
            float height = width * inner.y / inner.x;
            MapSettings map = Object.FindObjectOfType<MapSettings>();
            if (map != null && map.MapImage != null && map.MapSize.x > 0f && map.MapSize.y > 0f)
            {
                Sprite sprite = map.MapImage;
                Rect pixels = sprite.textureRect;
                float tw = sprite.texture.width, th = sprite.texture.height;
                // The map picture spans the whole theatre, centred on the world origin, north up.
                float u = 0.5f + (cx - width * 0.5f) / map.MapSize.x, v = 0.5f + (cz - height * 0.5f) / map.MapSize.y;
                mapImage.texture = sprite.texture;
                mapImage.uvRect = new Rect((pixels.x + u * pixels.width) / tw, (pixels.y + v * pixels.height) / th,
                    width / map.MapSize.x * pixels.width / tw, height / map.MapSize.y * pixels.height / th);
                mapImage.gameObject.SetActive(true);
            }
            else mapImage.gameObject.SetActive(false);

            Vector2 Place(float x, float z) => new Vector2((x - cx) / width * inner.x, (z - cz) / height * inner.y);
            mapLines.Clear();
            // Each kind is a list of lines split by ';', each line a run of "x z" points.
            void Lines(string key, Color ink, float stroke, bool head, bool dashed, float delay)
            {
                string list = words.Word(key);
                if (list == null) return;
                foreach (string part in list.Split(';'))
                {
                    float[] n = Numbers(part);
                    if (n.Length < 4) continue;
                    var line = new MapLine { Ink = ink, Width = stroke, Head = head, Dashed = dashed, Delay = delay };
                    for (int i = 0; i + 1 < n.Length; i += 2) line.Points.Add(Place(n[i], n[i + 1]));
                    mapLines.Add(line);
                    delay += 0.45f;
                }
            }
            Lines("front", Marker, 4f, false, true, 0.2f);
            Lines("arrows", Marker, 11f, true, false, 0.6f);
            Lines("retreat", Retreat, 8f, true, false, 2.2f);
            // labels="REPUBLIC@x z; PALA@x z"
            string[] labels = (words.Word("labels") ?? "").Split(';');
            for (int i = 0; i < mapLabels.Count; i++)
            {
                TextMeshProUGUI label = mapLabels[i];
                int at = i < labels.Length ? labels[i].IndexOf('@') : -1;
                float[] n = at > 0 ? Numbers(labels[i].Substring(at + 1)) : new float[0];
                label.gameObject.SetActive(n.Length >= 2);
                if (n.Length < 2) continue;
                label.text = labels[i].Substring(0, at).Trim().ToUpperInvariant();
                label.rectTransform.anchoredPosition = Place(n[0], n[1]);
            }
        }

        private void PaintMap(float age)
        {
            int used = 0;
            foreach (MapLine line in mapLines)
            {
                float total = 0f;
                for (int i = 1; i < line.Points.Count; i++) total += Vector2.Distance(line.Points[i - 1], line.Points[i]);
                // Each line draws on over two seconds, then holds.
                float drawn = total * Mathf.Clamp01((age - line.Delay) / 2f), walked = 0f;
                Vector2 tip = line.Points[0], heading = Vector2.up;
                for (int i = 1; i < line.Points.Count && walked < drawn; i++)
                {
                    Vector2 a = line.Points[i - 1], b = line.Points[i];
                    float length = Vector2.Distance(a, b);
                    if (length < 0.5f) continue;
                    heading = (b - a) / length;
                    float show = Mathf.Min(length, drawn - walked);
                    if (line.Dashed)
                        for (float d = 0f; d < show && used < segments.Count; d += 26f)
                            used = Segment(used, a + heading * d, heading, Mathf.Min(14f, show - d), line.Width, line.Ink);
                    else if (used < segments.Count) used = Segment(used, a, heading, show, line.Width, line.Ink);
                    tip = a + heading * show;
                    walked += length;
                }
                if (!line.Head || drawn <= 1f) continue;
                // Two strokes back from the tip make the head.
                for (int side = -1; side <= 1 && used < segments.Count; side += 2)
                    used = Segment(used, tip, Rotate(-heading, side * 32f), line.Width * 3.2f, line.Width, line.Ink);
            }
            for (int i = used; i < segments.Count; i++)
                if (segments[i].color.a > 0f) segments[i].color = Color.clear;
            float labelsIn = Mathf.Clamp01((age - 1.2f) / 0.6f);
            foreach (TextMeshProUGUI label in mapLabels) label.alpha = labelsIn;
        }

        private int Segment(int index, Vector2 from, Vector2 direction, float length, float width, Color ink)
        {
            Image segment = segments[index];
            RectTransform rect = segment.rectTransform;
            rect.anchoredPosition = from;
            rect.sizeDelta = new Vector2(length, width);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            segment.color = ink;
            return index + 1;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private static float[] Numbers(string text)
        {
            var found = new List<float>();
            foreach (string part in (text ?? "").Split(new[] { ' ', ',', '\t' }, System.StringSplitOptions.RemoveEmptyEntries))
                if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float n)) found.Add(n);
            return found.ToArray();
        }

        // ---- Satellite: black and white, a grid, boxes on the named units, a slow scan sweep. ----

        private void BuildSat(RectTransform area)
        {
            satArea = area;
            var gridInk = new Color(0.8f, 0.9f, 0.82f, 0.12f);
            for (int i = 1; i < 12; i++)
            {
                Image line = AvLay.Solid(area, "GridX" + i, gridInk);
                Anchor(line.rectTransform, new Vector2(i / 12f, 0f), new Vector2(i / 12f, 1f), Middle, Vector2.zero, new Vector2(1f, 0f));
            }
            for (int i = 1; i < 8; i++)
            {
                Image line = AvLay.Solid(area, "GridY" + i, gridInk);
                Anchor(line.rectTransform, new Vector2(0f, i / 8f), new Vector2(1f, i / 8f), Middle, Vector2.zero, new Vector2(0f, 1f));
            }
            satSweep = Raw(area, "Sweep", Texture(32, 1, TextureWrapMode.Clamp, FilterMode.Bilinear,
                (x, y) => new Color32(220, 255, 230, (byte)(26f * (1f - Mathf.Abs((x + 0.5f) / 16f - 1f))))));
            satSweep.color = Color.white;
            Anchor(satSweep.rectTransform, BottomLeft, TopLeft, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(360f, 0f));
            var dataInk = new Color(0.86f, 0.92f, 0.87f, 1f);
            satHeader = Words(area, "Header", AvTextRole.DataStrong, 18f, dataInk, TextAlignmentOptions.TopLeft, false);
            satHeader.characterSpacing = 6f;
            Anchor(satHeader.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(56f, -36f), new Vector2(1000f, 26f));
            satDate = Words(area, "Date", AvTextRole.DataStrong, 18f, dataInk, TextAlignmentOptions.TopRight, false);
            satDate.characterSpacing = 6f;
            Anchor(satDate.rectTransform, TopRight, TopRight, TopRight, new Vector2(-56f, -36f), new Vector2(700f, 26f));
            for (int i = 0; i < markLines.Length; i++)
            {
                markLines[i] = AvLay.Solid(area, "Mark" + i, Color.clear);
                Anchor(markLines[i].rectTransform, Middle, Middle, Middle, Vector2.zero, Vector2.zero);
            }
            for (int i = 0; i < MaxMarks; i++)
            {
                markLabels[i] = Words(area, "MarkLabel" + i, AvTextRole.DataStrong, 15f, dataInk, TextAlignmentOptions.BottomLeft, false);
                markLabels[i].characterSpacing = 4f;
                Anchor(markLabels[i].rectTransform, Middle, Middle, BottomLeft, Vector2.zero, new Vector2(500f, 22f));
            }
        }

        private void SetSat(ScenarioCut words)
        {
            satHeader.text = words.Word("header", "").ToUpperInvariant();
            satDate.text = words.Word("date", "").ToUpperInvariant();
            // marks="fleet_1:DDG HOME FLEET, fleet_3:FFG": a unit's name and the label its box carries.
            string[] marks = (words.Word("marks") ?? "").Split(',');
            for (int i = 0; i < MaxMarks; i++)
            {
                string mark = i < marks.Length ? marks[i].Trim() : "";
                int colon = mark.IndexOf(':');
                markNames[i] = mark.Length == 0 ? null : colon < 0 ? mark : mark.Substring(0, colon).Trim();
                markLabels[i].text = colon < 0 ? "" : mark.Substring(colon + 1).Trim().ToUpperInvariant();
                markUnits[i] = null;
            }
            nextMarkSearch = 0f;
        }

        private void PaintSat(float age)
        {
            satSweep.rectTransform.anchoredPosition = new Vector2((age * 260f) % (Inner().x + 720f) - 360f, 0f);
            if (Time.unscaledTime >= nextMarkSearch)
            {
                nextMarkSearch = Time.unscaledTime + 1f;
                for (int i = 0; i < MaxMarks; i++)
                    if (markNames[i] != null && markUnits[i] == null) markUnits[i] = FindUnit(markNames[i]);
            }
            Camera eye = Camera.main;
            for (int i = 0; i < MaxMarks; i++)
            {
                Unit unit = markUnits[i];
                bool shown = false;
                Vector2 at = Vector2.zero;
                if (unit != null && eye != null)
                {
                    Vector3 screen = eye.WorldToScreenPoint(unit.transform.position);
                    shown = screen.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(satArea, screen, null, out at);
                }
                // Each box snaps on a little after the one before, then breathes.
                float on = shown ? Mathf.Clamp01((age - 0.6f - 0.35f * i) / 0.15f) : 0f;
                float half = 60f + 4f * Mathf.Sin(Time.unscaledTime * 3f + i);
                Box(i, at, half, half * 0.7f, new Color(0.9f, 0.96f, 0.92f, on));
                markLabels[i].rectTransform.anchoredPosition = at + new Vector2(-half, half * 0.7f + 6f);
                markLabels[i].alpha = on;
            }
        }

        private void Box(int index, Vector2 centre, float halfWidth, float halfHeight, Color ink)
        {
            for (int side = 0; side < 4; side++)
            {
                Image line = markLines[index * 4 + side];
                bool across = side < 2;
                float sign = side % 2 == 0 ? 1f : -1f;
                line.rectTransform.anchoredPosition = centre + (across ? new Vector2(0f, sign * halfHeight) : new Vector2(sign * halfWidth, 0f));
                line.rectTransform.sizeDelta = across ? new Vector2(2f * halfWidth, 2f) : new Vector2(2f, 2f * halfHeight);
                line.color = ink;
            }
        }

        private static Unit FindUnit(string name)
        {
            int count = Mathf.Min(4096, UnitRegistry.allUnits.Count);
            for (int i = 0; i < count; i++)
            {
                Unit unit = UnitRegistry.allUnits[i];
                if (unit != null && unit.NetworkUniqueName == name) return unit;
            }
            return null;
        }

        // ---- Dossier: a manila folder, a typed sheet, a polaroid clipped on, a stamp that lands. ----

        private void BuildDossier(RectTransform area)
        {
            Image desk = AvLay.Solid(area, "Desk", new Color(0f, 0f, 0f, 0.45f));
            AvLay.Fill(desk.rectTransform);
            folder = AvLay.Child(area, "Folder");
            Anchor(folder, Middle, Middle, Middle, new Vector2(-40f, 0f), new Vector2(860f, 900f));
            folder.localRotation = Quaternion.Euler(0f, 0f, 2f);
            Shadow(folder, 70f, new Vector2(24f, -26f), 0.6f);
            Image cover = AvLay.Solid(folder, "Cover", Manila);
            AvLay.Fill(cover.rectTransform);
            RawImage sheet = Raw(folder, "Sheet", paperTexture);
            sheet.uvRect = new Rect(0f, 0f, 5f, 5f);
            sheet.color = new Color(0.96f, 0.94f, 0.89f, 1f);
            Anchor(sheet.rectTransform, BottomLeft, Vector2.one, Middle, new Vector2(0f, -6f), new Vector2(-80f, -80f));
            RectTransform page = sheet.rectTransform;
            var ink = new Color(0.13f, 0.13f, 0.13f, 1f);
            var top = new Vector2(0.5f, 1f);
            dosCompany = Words(page, "Company", AvTextRole.Headline, 34f, ink, TextAlignmentOptions.TopLeft, false);
            dosCompany.characterSpacing = 10f;
            Anchor(dosCompany.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -34f), new Vector2(-80f, 44f));
            Image rule = AvLay.Solid(page, "Rule", ink);
            Anchor(rule.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -82f), new Vector2(-80f, 3f));
            dosDate = Words(page, "Date", AvTextRole.DataStrong, 17f, ink, TextAlignmentOptions.TopLeft, false);
            dosDate.characterSpacing = 4f;
            Anchor(dosDate.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -94f), new Vector2(-80f, 26f));
            dosHeading = Words(page, "Heading", AvTextRole.Headline, 68f, ink, TextAlignmentOptions.TopLeft, false);
            dosHeading.fontStyle = FontStyles.Bold;
            Anchor(dosHeading.rectTransform, TopLeft, TopRight, top, new Vector2(0f, -136f), new Vector2(-80f, 80f));
            dosBody = Words(page, "Body", AvTextRole.DataStrong, 20f, ink, TextAlignmentOptions.TopLeft, true);
            dosBody.lineSpacing = 12f;
            Anchor(dosBody.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(40f, -230f), new Vector2(430f, 420f));

            stampBox = AvLay.Child(page, "Stamp");
            Anchor(stampBox, BottomLeft, BottomLeft, Middle, new Vector2(200f, 130f), new Vector2(260f, 90f));
            for (int side = 0; side < 4; side++)
            {
                Image edge = AvLay.Solid(stampBox, "Edge" + side, Color.clear);
                bool across = side < 2;
                Vector2 from = across ? new Vector2(0f, side) : new Vector2(side - 2, 0f), to = across ? new Vector2(1f, side) : new Vector2(side - 2, 1f);
                Anchor(edge.rectTransform, from, to, Middle, Vector2.zero, across ? new Vector2(0f, 6f) : new Vector2(6f, 0f));
                stampEdges.Add(edge);
            }
            dosStamp = Words(stampBox, "Text", AvTextRole.Headline, 56f, Marker, TextAlignmentOptions.Center, false);
            dosStamp.fontStyle = FontStyles.Bold;
            dosStamp.characterSpacing = 14f;
            AvLay.Fill(dosStamp.rectTransform);

            clip = AvLay.Child(folder, "Polaroid");
            Anchor(clip, Middle, Middle, Middle, new Vector2(330f, -60f), new Vector2(380f, 440f));
            clip.localRotation = Quaternion.Euler(0f, 0f, 7f);
            Shadow(clip, 40f, new Vector2(12f, -16f), 0.5f);
            RawImage card = Raw(clip, "Card", paperTexture);
            card.uvRect = new Rect(0f, 0f, 3f, 3f);
            card.color = Paper;
            AvLay.Fill(card.rectTransform);
            clipPhoto = Raw(clip, "Photo", null);
            clipPhoto.color = Color.white;
            Anchor(clipPhoto.rectTransform, top, top, top, new Vector2(0f, -20f), new Vector2(340f, 340f));
            clipNote = Words(clip, "Note", AvTextRole.DataStrong, 20f, Ink, TextAlignmentOptions.BottomLeft, false);
            Anchor(clipNote.rectTransform, BottomLeft, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(-44f, 30f));
            Image tape = AvLay.Solid(clip, "Tape", Tape.WithAlpha(0.65f));
            Anchor(tape.rectTransform, top, top, Middle, new Vector2(-60f, 0f), new Vector2(120f, 34f));
            tape.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -6f);
        }

        private void SetDossier(ScenarioCut words, Texture picture)
        {
            dosCompany.text = words.Word("company", "").ToUpperInvariant();
            dosDate.text = words.Word("date", "").ToUpperInvariant();
            dosHeading.text = words.Word("heading", "").ToUpperInvariant();
            dosBody.text = words.Word("body", "");
            dosStamp.text = words.Word("mark", "").ToUpperInvariant();
            stampBox.gameObject.SetActive(dosStamp.text.Length > 0);
            stampBox.sizeDelta = new Vector2(dosStamp.GetPreferredValues(dosStamp.text).x + 60f, 90f);
            clip.gameObject.SetActive(picture != null);
            clipNote.text = words.Word("note", "");
            if (picture != null) SetPicture(clipPhoto, picture, 1f);
        }

        private void PaintDossier(float age)
        {
            float fit = Mathf.Min(1f, Inner().y / 920f);
            folder.localScale = new Vector3(fit, fit, 1f);
            // The stamp comes down hard a beat after the file is up.
            float t = Mathf.Clamp01((age - 1.2f) / 0.25f);
            float scale = t < 1f ? Mathf.Lerp(2.2f, 0.95f, t) : 1f;
            stampBox.localScale = new Vector3(scale, scale, 1f);
            stampBox.localRotation = Quaternion.Euler(0f, 0f, -9f);
            dosStamp.alpha = 0.85f * t;
            foreach (Image edge in stampEdges) edge.color = Marker.WithAlpha(0.85f * t);
        }

        // ---- Title and chapter cards. ----

        private void BuildTitle(RectTransform area)
        {
            var bottom = new Vector2(0.5f, 0f);
            titleDate = Words(area, "Date", AvTextRole.DataStrong, 18f, AvTheme.Warning, TextAlignmentOptions.Center, false);
            titleDate.characterSpacing = 28f;
            Anchor(titleDate.rectTransform, BottomLeft, new Vector2(1f, 0f), bottom, new Vector2(0f, 228f), new Vector2(0f, 28f));
            titleBig = Words(area, "Title", AvTextRole.Headline, 92f, Color.white, TextAlignmentOptions.Center, false);
            Anchor(titleBig.rectTransform, BottomLeft, new Vector2(1f, 0f), bottom, new Vector2(0f, 110f), new Vector2(0f, 110f));
            titleRule = AvLay.Solid(area, "Rule", Color.white.WithAlpha(0.55f));
            Anchor(titleRule.rectTransform, bottom, bottom, bottom, new Vector2(0f, 96f), new Vector2(420f, 2f));
        }

        private void BuildChapter(RectTransform area)
        {
            // Dark on the left, where the words sit; the picture shows on the right.
            RawImage shade = Raw(area, "Shade", Texture(64, 1, TextureWrapMode.Clamp, FilterMode.Bilinear,
                (x, y) => new Color32(0, 0, 0, (byte)(255f * Mathf.Clamp01(1f - (x - 18f) / 30f)))));
            shade.color = Color.white;
            AvLay.Fill(shade.rectTransform);
            var left = new Vector2(0f, 0.5f);
            chapterNumber = Words(area, "Number", AvTextRole.DataStrong, 20f, AvTheme.Warning, TextAlignmentOptions.Left, false);
            chapterNumber.characterSpacing = 30f;
            Anchor(chapterNumber.rectTransform, left, left, left, new Vector2(130f, 120f), new Vector2(900f, 30f));
            chapterBig = Words(area, "Title", AvTextRole.Headline, 110f, Color.white, TextAlignmentOptions.Left, false);
            chapterBig.characterSpacing = 12f;
            Anchor(chapterBig.rectTransform, left, left, left, new Vector2(126f, 36f), new Vector2(1100f, 130f));
            chapterRule = AvLay.Solid(area, "Rule", AvTheme.Warning);
            Anchor(chapterRule.rectTransform, left, left, left, new Vector2(130f, -40f), new Vector2(480f, 3f));
            chapterLine = Words(area, "Line", AvTextRole.Prose, 28f, new Color(0.88f, 0.87f, 0.84f, 1f), TextAlignmentOptions.TopLeft, true);
            chapterLine.fontStyle = FontStyles.Italic;
            Anchor(chapterLine.rectTransform, left, left, TopLeft, new Vector2(130f, -60f), new Vector2(820f, 120f));
        }

        private void PaintTitle(float age)
        {
            // The title settles in: its letters close up from wide spacing as it fades up.
            float t = Mathf.Clamp01(age / 1.6f), ease = 1f - (1f - t) * (1f - t);
            titleBig.characterSpacing = Mathf.Lerp(60f, 28f, ease);
            titleBig.alpha = ease;
            titleDate.alpha = Mathf.Clamp01((age - 0.6f) / 0.6f);
            titleRule.rectTransform.sizeDelta = new Vector2(420f * Mathf.Clamp01((age - 0.4f) / 0.8f), 2f);
        }

        private void PaintChapter(float age)
        {
            chapterBig.alpha = Mathf.Clamp01(age / 0.8f);
            chapterNumber.alpha = Mathf.Clamp01((age - 0.3f) / 0.6f);
            chapterRule.rectTransform.sizeDelta = new Vector2(480f * Mathf.Clamp01((age - 0.5f) / 0.7f), 3f);
            chapterLine.alpha = Mathf.Clamp01((age - 1f) / 0.8f);
        }

        // ---- Shared. ----

        /// <summary>Fills <paramref name="image"/> with <paramref name="picture"/>, cropped to the box's shape (<paramref name="aspect"/> = width / height).</summary>
        private static void SetPicture(RawImage image, Texture picture, float aspect)
        {
            image.texture = picture;
            if (picture == null || picture.height == 0) return;
            float own = (float)picture.width / picture.height;
            image.uvRect = own > aspect ? new Rect((1f - aspect / own) * 0.5f, 0f, aspect / own, 1f) : new Rect(0f, (1f - own / aspect) * 0.5f, 1f, own / aspect);
        }

        /// <summary>A black-and-white print of a picture, made once; a picture that cannot be read is used as it is.</summary>
        private Texture Grey(Texture picture)
        {
            if (!(picture is Texture2D source) || !source.isReadable) return picture;
            if (greys.TryGetValue(picture, out Texture2D made)) return made;
            Color32[] pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                // Luma, pushed apart a little the way a press photo is.
                float luma = (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;
                byte v = (byte)(Mathf.Clamp01((luma - 0.5f) * 1.25f + 0.5f) * 255f);
                pixels[i] = new Color32(v, v, v, 255);
            }
            made = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            made.SetPixels32(pixels);
            made.Apply(false, true);
            textures.Add(made);
            greys[picture] = made;
            return made;
        }

        /// <summary>The board on screen: fades with the shot and the bars, plays its own move, and drives the washed picture under it.</summary>
        private void PaintBoards(float real, float bars)
        {
            float alpha = board != null ? bars * Mathf.Clamp01((real - boardFrom) / 0.35f) : 0f;
            if (board != null && boards.TryGetValue(board, out CanvasGroup group))
            {
                group.alpha = alpha;
                float age = real - boardFrom;
                switch (board)
                {
                    case "news": PaintNews(age); break;
                    case "paper": PaintPaper(age); break;
                    case "map": PaintMap(age); break;
                    case "sat": PaintSat(age); break;
                    case "dossier": PaintDossier(age); break;
                    case "title": PaintTitle(age); break;
                    case "chapter": PaintChapter(age); break;
                }
            }
            PaintLook(board == "news" ? 1 : board == "sat" ? 2 : 0, alpha);
            if (!cut && bars <= 0.001f && board != null) HideBoards();
        }

        /// <summary>The washed picture: 1 a TV news picture, 2 a black-and-white satellite print, 0 off. Built on first use: one global volume over the game's.</summary>
        private void PaintLook(int kind, float weight)
        {
            if (kind == 0 || weight <= 0.001f)
            {
                if (look != null && look.weight != 0f) look.weight = 0f;
                return;
            }
            if (look == null)
            {
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                if (level == null || level.PostProcessing == null) return;
                lookHolder = new GameObject("BoscaliSummer.StoryLook");
                lookHolder.transform.SetParent(transform, false);
                // Cameras only blend volumes on layers in their volume mask: share the game's.
                lookHolder.layer = level.PostProcessing.gameObject.layer;
                lookProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                lookColour = lookProfile.Add<ColorAdjustments>();
                lookGrain = lookProfile.Add<FilmGrain>();
                lookFringe = lookProfile.Add<ChromaticAberration>();
                look = lookHolder.AddComponent<Volume>();
                look.isGlobal = true;
                look.priority = 0.9f;   // over the game's (0.5) and the display grade (0.75), under night vision (1)
                look.sharedProfile = lookProfile;
            }
            bool tv = kind == 1;
            lookColour.saturation.Override(tv ? -40f : -100f);
            lookColour.contrast.Override(tv ? 14f : 38f);
            lookGrain.type.Override(FilmGrainLookup.Medium3);
            lookGrain.intensity.Override(tv ? 0.35f : 0.55f);
            lookGrain.response.Override(0.8f);
            lookFringe.intensity.Override(tv ? 0.35f : 0.1f);
            look.weight = weight;
        }

        private void DestroyLook()
        {
            if (lookHolder != null) Destroy(lookHolder);
            if (lookProfile != null) Destroy(lookProfile);
            lookHolder = null;
            look = null;
        }
    }
}
