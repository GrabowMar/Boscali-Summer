using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Space;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation
{
    internal enum FeedImageKind : byte { None, Optical, Sar }

    /// <summary>A host-revealed contact bracket over the picture: 0..1 across the picture, origin bottom-left.</summary>
    internal struct FeedBracketView
    {
        public int Id;
        public float U, V;
        public ProbableClass Class;
        public bool Marked, Selected;
        public string Label;
    }

    /// <summary>One of the six fixed contact targets.</summary>
    internal struct FeedTileView
    {
        public bool Present, Selected, Marked;
        public int Id;
        public ProbableClass Class;
        public string Title, Sub;
    }

    /// <summary>One TASKED card: target and payoff, source, the host quote and one state word.</summary>
    internal struct FeedCardView
    {
        public bool Present, Armed, Enabled;
        public int PostId;
        public string Title, Sub, Price, State;
        public AvState Tone;
    }

    /// <summary>
    /// The one view model both surfaces paint: the compact page on the OPS MFD and the full-screen window. Built each refresh
    /// by <see cref="SpaceFeedController"/> from the faction mirror (never from a client registry); reused, never reallocated.
    /// </summary>
    internal sealed class SpaceFeedView
    {
        public const int MaxBrackets = 16;

        public bool Ground, ThreatActive;
        public string Threat = "";
        public FeedImageKind ImageKind;
        public Texture Image;
        public float ImageAspect = 1.6f;
        public string Refusal = "";
        public readonly List<FeedBracketView> Brackets = new List<FeedBracketView>(MaxBrackets);
        public BirdKind Source;
        public int Zoom;
        public string Status = "";
        public AvState StatusTone = AvState.Inert;
        public readonly FeedTileView[] Tiles = new FeedTileView[SpaceFeedRules.ContactsPerPage];
        public int Page, Pages = 1;
        public bool CanConfirm, CanSend, ConfirmFull, ZoomEnabled = true;
        public int SendCount;
        public string ConfirmHelp = "", SendHelp = "";
        public string TaskedCaption = "";
        public readonly FeedCardView[] Cards = new FeedCardView[6];
        public int CardCount;
        public string Words = "";
        public AvState WordsTone = AvState.Inert;
    }

    /// <summary>What the feed surfaces ask of the controller. Every method is a request: none spends or decides anything.</summary>
    internal interface ISpaceFeedActions
    {
        void SelectEntry(int id);
        void PrevPage();
        void NextPage();
        void Confirm();
        void Send();
        void SetSource(BirdKind source);
        void CycleZoom();
        void ToggleFull();
        void PressCard(int postId);
        /// <summary>Any real operator input on the feed (keeps the idle clock and the host lease).</summary>
        void Touch();
    }

    /// <summary>
    /// The SPACE feed board: threat strip, source/zoom/full toolbar, the picture with host-revealed brackets, one status line,
    /// six fixed 36 px contact targets with a page switch, CONFIRM / SEND, the TASKED cards and one words line. Every row height
    /// is fixed after construction (<see cref="SpaceFeedLayout"/>); only the picture takes the leftover height. Text shrinks
    /// toward the 10 px floor, never wraps and never moves a row.
    /// </summary>
    internal sealed class SpaceFeedPanel : AvPart
    {
        private const float Pad = 6f;
        private readonly ISpaceFeedActions actions;
        private readonly SpaceFeedLayout layout;
        private readonly bool full;
        private readonly float boardWidth, boardHeight;

        // Threat strip, words line, status line.
        private readonly Image threatBack, threatRail, wordsBack, wordsRail;
        private readonly TMP_Text threatText, wordsText, statusText, captionText, hintText;

        // Toolbar and actions.
        private readonly AvControl opticalButton, radarButton, zoomButton, fullButton, prevButton, nextButton, confirmButton, sendButton;
        private readonly TMP_Text pageText;

        // Picture.
        private readonly RectTransform imageRoot;
        private readonly Image imageBack;
        private readonly RawImage picture;
        private readonly TMP_Text refusalText;
        private readonly Bracket[] brackets = new Bracket[SpaceFeedView.MaxBrackets];

        // Tiles and cards.
        private readonly Tile[] tiles = new Tile[SpaceFeedLayout.Tiles6];
        private readonly Card[] cards;

        private AvState threatTone = AvState.Inert, wordsTone = AvState.Inert;
        private SpaceFeedView last;

        public SpaceFeedPanel(RectTransform parent, ISpaceFeedActions actions, float width, float height, bool full)
        {
            this.actions = actions ?? NullActions.Instance;
            this.full = full;
            boardWidth = width;
            boardHeight = height;
            layout = SpaceFeedLayout.Compute(width, height, full);
            Rect = AvLay.Child(parent, "SpaceFeed");
            AvLay.Place(Rect, 0f, 0f, width, height);

            // ---- Threat strip ----
            threatBack = AvLay.Solid(Rect, "ThreatBack", Color.clear);
            threatRail = AvLay.Solid(Rect, "ThreatRail", Color.clear);
            threatText = Line(Rect, "Threat", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            if (full) { threatText.fontSize = 22f; threatText.fontSizeMax = 22f; }
            Put(threatBack.rectTransform, layout.Threat);
            AvLay.Place(threatRail.rectTransform, layout.Threat.X, layout.Threat.Y, 3f, layout.Threat.H);
            AvLay.Place(threatText.rectTransform, layout.Threat.X + 10f, layout.Threat.Y, layout.Threat.W - 14f, layout.Threat.H);

            // ---- Toolbar ----
            FeedBox tb = layout.Toolbar;
            float bw = (tb.W - 3f * 4f) / 4f;
            opticalButton = Button(Rect, "OPTICAL", () => Do(() => actions.SetSource(BirdKind.Optical)), AvButtonStyle.Default, tb.X, tb.Y, bw, tb.H,
                "Show the OPTICAL bird's camera picture. Daylight only: cloud softens it, night refuses.");
            radarButton = Button(Rect, "RADAR", () => Do(() => actions.SetSource(BirdKind.Radar)), AvButtonStyle.Default, tb.X + (bw + 4f), tb.Y, bw, tb.H,
                "Show the RADAR bird's SAR product from the last RADAR SCAN or MTI SWEEP.");
            zoomButton = Button(Rect, "ZOOM WIDE", () => Do(actions.CycleZoom), AvButtonStyle.Quiet, tb.X + 2f * (bw + 4f), tb.Y, bw, tb.H,
                "Cycle the picture zoom: WIDE, MID, CLOSE. CLOSE centres on the selected target.");
            fullButton = Button(Rect, full ? "EXIT" : "OPEN FULL", () => Do(actions.ToggleFull), AvButtonStyle.Quiet, tb.X + 3f * (bw + 4f), tb.Y, bw, tb.H,
                full ? "Close the full-screen feed (Esc)." : "Open the full-screen feed. Flight controls stay live.");

            // ---- Picture ----
            FeedBox im = layout.Image;
            imageRoot = AvLay.Child(Rect, "Picture");
            AvLay.Place(imageRoot, im.X, im.Y, im.W, im.H);
            imageRoot.gameObject.AddComponent<RectMask2D>();
            imageBack = AvLay.Solid(imageRoot, "Back", Color.black);
            AvLay.Fill(imageBack.rectTransform);
            imageBack.raycastTarget = true; // swallow clicks so a miss never reaches the map behind
            var pictureGo = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer));
            pictureGo.transform.SetParent(imageRoot, false);
            picture = pictureGo.AddComponent<RawImage>();
            picture.raycastTarget = false;
            AvLay.Fill(picture.rectTransform);
            picture.enabled = false;
            refusalText = AvText.Make(imageRoot, "Refusal", AvTextRole.Prose, "", TextAlignmentOptions.Center, true);
            AvLay.Fill(refusalText.rectTransform, 12f);
            for (int i = 0; i < brackets.Length; i++) brackets[i] = new Bracket(imageRoot, i, id => Do(() => actions.SelectEntry(id)));

            // ---- Status ----
            statusText = Line(Rect, "Status", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            Put(statusText.rectTransform, layout.Status);

            // ---- Tiles ----
            for (int i = 0; i < tiles.Length; i++)
            {
                FeedBox box = layout.Tile(i);
                tiles[i] = new Tile(Rect, box, id => Do(() => actions.SelectEntry(id)));
            }

            // ---- Actions ----
            FeedBox ac = layout.Actions;
            const float navW = 48f, pageW = 52f, g = 4f;
            float rest = ac.W - (navW * 2f + pageW + 4f * g);
            float actionW = rest * 0.5f;
            prevButton = Button(Rect, "PREV", () => Do(actions.PrevPage), AvButtonStyle.Quiet, ac.X, ac.Y, navW, ac.H, "Previous page of contact targets.");
            pageText = Line(Rect, "Page", AvTextRole.Micro, TextAlignmentOptions.Center);
            AvLay.Place(pageText.rectTransform, ac.X + navW + g, ac.Y, pageW, ac.H);
            nextButton = Button(Rect, "NEXT", () => Do(actions.NextPage), AvButtonStyle.Quiet, ac.X + navW + g + pageW + g, ac.Y, navW, ac.H, "Next page of contact targets.");
            float cx = ac.X + navW * 2f + pageW + 3f * g;
            confirmButton = Button(Rect, "CONFIRM", () => Do(actions.Confirm), AvButtonStyle.Primary, cx, ac.Y, actionW, ac.H,
                "MARK the selected target. The host answers CONFIRMED, NEUTRAL, DECOY or FRIENDLY; only CONFIRMED can be posted.");
            sendButton = Button(Rect, "SEND", () => Do(actions.Send), AvButtonStyle.Primary, cx + actionW + g, ac.Y, actionW, ac.H,
                "Post your live MARKs as one TASKED call. The first pilot to claim it fires.");

            // ---- TASKED cards ----
            FeedBox th = layout.TaskedHeader;
            captionText = Line(Rect, "Tasked", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            AvLay.Place(captionText.rectTransform, th.X, th.Y, th.W * 0.4f, th.H);
            hintText = Line(Rect, "TaskedHint", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            AvLay.Place(hintText.rectTransform, th.X + th.W * 0.4f, th.Y, th.W * 0.6f, th.H);
            cards = new Card[layout.CardCount];
            for (int i = 0; i < cards.Length; i++) cards[i] = new Card(Rect, layout.Cards[i], id => Do(() => actions.PressCard(id)));

            // ---- Words ----
            wordsBack = AvLay.Solid(Rect, "WordsBack", Color.clear);
            wordsRail = AvLay.Solid(Rect, "WordsRail", Color.clear);
            wordsText = Line(Rect, "Words", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            Put(wordsBack.rectTransform, layout.Words);
            AvLay.Place(wordsRail.rectTransform, layout.Words.X, layout.Words.Y, 3f, layout.Words.H);
            AvLay.Place(wordsText.rectTransform, layout.Words.X + 10f, layout.Words.Y, layout.Words.W - 14f, layout.Words.H);

            Restyle();
        }

        public RectTransform ImageRoot => imageRoot;
        public SpaceFeedLayout Layout => layout;

        /// <summary>A kit v2 standalone console for the offline render harness: the OPS chrome with this page alone.</summary>
        internal static SpaceFeedPanel BuildForHarness(RectTransform root, float height)
        {
            AvConsole shell = AvConsole.Build(root, "OPS", "SPACE", 1, AvTokens.PanelWidth, height);
            AvFlow page = shell.Page(0);
            float body = OpsPage.BodyHeight(height, tabs: true);
            var panel = new SpaceFeedPanel(page.Content, NullActions.Instance, OpsPage.BoardWidth, body - OpsPage.FlowInset, false);
            page.Add(panel);
            shell.Finish();
            return panel;
        }

        private void Do(Action act)
        {
            actions.Touch();
            act();
        }

        public override float Measure(float width) => boardHeight;

        public override void Place(AvSlot s)
        {
            base.Place(s);
        }

        // ---- Paint -----------------------------------------------------------------------------------------------

        /// <summary>Applies the view. Text and states change only when they differ, so a refresh costs nothing when nothing moved.</summary>
        public void Paint(SpaceFeedView view)
        {
            if (view == null) return;
            last = view;

            SetTone(ref threatTone, view.ThreatActive ? AvState.Danger : view.Ground ? AvState.Inert : AvState.Ready);
            OpsText.Set(threatText, view.Threat);
            SetTone(ref wordsTone, view.WordsTone);
            OpsText.Set(wordsText, view.Words);
            OpsText.Set(statusText, view.Status);
            statusText.color = OpsInk.Word(view.StatusTone == AvState.Inert ? AvState.Info : view.StatusTone);
            OpsText.Set(pageText, "PAGE " + (view.Page + 1) + "/" + Math.Max(1, view.Pages));

            opticalButton.Latched = view.Source == BirdKind.Optical;
            radarButton.Latched = view.Source == BirdKind.Radar;
            SetLabel(zoomButton, view.ZoomEnabled ? "ZOOM " + SpaceFeedRules.ZoomWord(view.Zoom) : "ZOOM FIXED");
            SetEnabled(zoomButton, view.ZoomEnabled);
            SetEnabled(prevButton, view.Page > 0);
            SetEnabled(nextButton, view.Page + 1 < view.Pages);
            SetEnabled(confirmButton, view.CanConfirm);
            SetLabel(confirmButton, view.ConfirmFull ? "MARKS FULL" : "CONFIRM");
            SetHelp(confirmButton, view.ConfirmHelp);
            SetEnabled(sendButton, view.CanSend);
            SetLabel(sendButton, view.SendCount > 0 ? "SEND " + view.SendCount : "SEND");
            SetHelp(sendButton, view.SendHelp);

            PaintPicture(view);
            for (int i = 0; i < tiles.Length; i++) tiles[i].Paint(view.Tiles[i]);

            OpsText.Set(captionText, view.TaskedCaption);
            OpsText.Set(hintText, view.CardCount > 0 ? "CLICK A CARD TO ARM · CLICK AGAIN TO FIRE" : "NOTHING POSTED · MARK, THEN SEND");
            for (int i = 0; i < cards.Length; i++) cards[i].Paint(i < view.CardCount ? view.Cards[i] : default);
        }

        private void PaintPicture(SpaceFeedView view)
        {
            bool show = view.Image != null && view.ImageKind != FeedImageKind.None && view.Refusal.Length == 0;
            if (picture.enabled != show) picture.enabled = show;
            OpsText.Set(refusalText, show ? "" : view.Refusal);
            refusalText.color = OpsInk.Word(AvState.Caution);
            if (!show)
            {
                for (int i = 0; i < brackets.Length; i++) brackets[i].Hide();
                return;
            }
            if (picture.texture != view.Image) picture.texture = view.Image;
            Rect area = imageRoot.rect;
            float areaAspect = area.height > 1f ? area.width / area.height : 1.6f;
            SpaceFeedRules.CoverCrop(areaAspect, view.ImageAspect, out float u0, out float v0, out float uw, out float vh);
            picture.uvRect = new Rect(u0, v0, uw, vh);
            int shown = 0;
            for (int i = 0; i < view.Brackets.Count && shown < brackets.Length; i++)
            {
                FeedBracketView b = view.Brackets[i];
                if (!SpaceFeedRules.CropPoint(b.U, b.V, u0, v0, uw, vh, out float ax, out float ay)) continue;
                brackets[shown++].Paint(b, ax * area.width, (1f - ay) * area.height);
            }
            for (int i = shown; i < brackets.Length; i++) brackets[i].Hide();
        }

        private void SetTone(ref AvState current, AvState next)
        {
            if (current == next) return;
            current = next;
            Restyle();
        }

        public override void Restyle()
        {
            if (threatBack == null) return;
            Color Back(AvState s) => AvStyleHost.Resolve(AvStyleHost.FuiStyle("row " + AvStates.Class(s)).Background, AvTheme.SurfaceInert);
            threatBack.color = threatTone == AvState.Danger ? Color.Lerp(Back(threatTone), OpsInk.Rail(AvState.Danger), 0.22f) : Back(threatTone);
            threatRail.color = OpsInk.Rail(threatTone);
            threatText.color = threatTone == AvState.Inert ? OpsInk.Dim : OpsInk.Word(threatTone);
            wordsBack.color = Back(wordsTone);
            wordsRail.color = OpsInk.Rail(wordsTone);
            wordsText.color = wordsTone == AvState.Inert ? OpsInk.Dim : OpsInk.Word(wordsTone);
            statusText.color = OpsInk.Word(AvState.Info);
            pageText.color = OpsInk.Dim;
            captionText.color = OpsInk.Key;
            hintText.color = OpsInk.Muted;
            imageBack.color = new Color(0.02f, 0.03f, 0.03f, 1f);
            foreach (AvControl c in new[] { opticalButton, radarButton, zoomButton, fullButton, prevButton, nextButton, confirmButton, sendButton })
                c?.Restyle();
            if (tiles != null) foreach (Tile t in tiles) t?.Restyle();
            if (cards != null) foreach (Card c in cards) c?.Restyle();
            if (brackets != null) foreach (Bracket b in brackets) b?.Restyle();
        }

        // ---- Small helpers ---------------------------------------------------------------------------------------

        private static TMP_Text Line(RectTransform parent, string name, AvTextRole role, TextAlignmentOptions align)
        {
            TMP_Text t = OpsText.Line(parent, name, role, align);
            t.fontSizeMin = AvTokens.FontMicro;
            return t;
        }

        private static void SetEnabled(AvControl c, bool enabled) { if (c.Interactable != enabled) c.Interactable = enabled; }
        private static void SetLabel(AvControl c, string label) { if (c.Label != label) c.Label = label; }
        private static void SetHelp(AvControl c, string help) { if (!string.IsNullOrEmpty(help) && c.Help != help) c.Help = help; }

        private static void Put(RectTransform t, FeedBox b) => AvLay.Place(t, b.X, b.Y, b.W, b.H);

        private static AvControl Button(RectTransform parent, string label, Action click, AvButtonStyle style, float x, float y, float w, float h, string help)
        {
            AvControl c = AvControl.Make(parent, new AvControl.Spec(label, click, style));
            c.SingleLine();
            c.Help = help;
            AvLay.Place(c.Rect, x, y, w, h);
            return c;
        }

        internal static Color ClassInk(ProbableClass c) =>
            c == ProbableClass.Hostile ? OpsInk.Rail(AvState.Danger)
            : c == ProbableClass.Friendly ? OpsInk.Rail(AvState.Ready)
            : c == ProbableClass.Neutral ? OpsInk.Rail(AvState.Info) : OpsInk.Rail(AvState.Caution);

        // ---- Parts -----------------------------------------------------------------------------------------------

        /// <summary>A contact bracket over the picture: a 36 px click target around a small outline and a label.</summary>
        private sealed class Bracket
        {
            private const float Hit = 36f, Box = 22f;
            private readonly RectTransform root;
            private readonly AvFrame outline;
            private readonly TMP_Text label;
            private readonly Image hitArea;
            private int id;
            private bool selected, marked;
            private ProbableClass cls;

            public Bracket(RectTransform parent, int index, Action<int> select)
            {
                root = AvLay.Child(parent, "Bracket " + index);
                AvLay.Place(root, 0f, 0f, Hit, Hit);
                hitArea = AvLay.Solid(root, "Hit", Color.clear);
                AvLay.Fill(hitArea.rectTransform);
                hitArea.raycastTarget = true;
                AvHit hit = hitArea.gameObject.AddComponent<AvHit>();
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) select(id); };
                outline = AvFrame.Add(root, "Outline", default(AvChamfer));
                outline.Fill = false;
                outline.Stroke = 1.4f;
                outline.Bracket = 5f;
                outline.raycastTarget = false;
                AvLay.Place(outline.rectTransform, (Hit - Box) * 0.5f, (Hit - Box) * 0.5f, Box, Box);
                label = Line(root, "Label", AvTextRole.Micro, TextAlignmentOptions.Top);
                AvLay.Place(label.rectTransform, -30f, Hit - 6f, Hit + 60f, 14f);
                label.enableWordWrapping = false;
                root.gameObject.SetActive(false);
            }

            public void Hide() { if (root.gameObject.activeSelf) root.gameObject.SetActive(false); }

            public void Paint(in FeedBracketView b, float x, float y)
            {
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                id = b.Id; selected = b.Selected; marked = b.Marked; cls = b.Class;
                AvLay.Place(root, x - Hit * 0.5f, y - Hit * 0.5f, Hit, Hit);
                OpsText.Set(label, b.Label);
                Restyle();
            }

            public void Restyle()
            {
                if (outline == null) return;
                Color ink = selected ? OpsInk.Select : marked ? OpsInk.Rail(AvState.Ready) : ClassInk(cls);
                outline.StrokeColor = ink;
                outline.BracketColor = ink;
                outline.Stroke = selected || marked ? 2.2f : 1.4f;
                outline.SetVerticesDirty();
                label.color = selected ? OpsInk.Select : OpsInk.Ink;
            }
        }

        /// <summary>A fixed 36 px contact target: class word over percent/state, selected and MARKed marks.</summary>
        private sealed class Tile
        {
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text title, sub;
            private int id;
            private bool present, selected, marked, hover;
            private ProbableClass cls;

            public Tile(RectTransform parent, FeedBox box, Action<int> select)
            {
                RectTransform root = AvLay.Child(parent, "Tile");
                AvLay.Place(root, box.X, box.Y, box.W, box.H);
                frame = AvFrame.Add(root, "Frame", default(AvChamfer));
                AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(root, "Rail", Color.clear);
                AvLay.Place(rail.rectTransform, 0f, 0f, 3f, box.H);
                title = Line(root, "Title", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
                sub = Line(root, "Sub", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
                AvLay.Place(title.rectTransform, 8f, 2f, box.W - 10f, 16f);
                AvLay.Place(sub.rectTransform, 8f, 18f, box.W - 10f, 15f);
                AvHit hit = AvHit.On(frame);
                hit.Hover = h => { hover = h; Restyle(); };
                hit.Click = e => { if (present && e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) select(id); };
                Restyle();
            }

            public void Paint(in FeedTileView v)
            {
                bool changed = present != v.Present || selected != v.Selected || marked != v.Marked || cls != v.Class;
                present = v.Present; selected = v.Selected; marked = v.Marked; cls = v.Class; id = v.Id;
                OpsText.Set(title, v.Present ? v.Title : "—");
                OpsText.Set(sub, v.Present ? v.Sub : "NO TARGET");
                if (changed) Restyle();
            }

            public void Restyle()
            {
                if (frame == null) return;
                AvStyle r = AvStyleHost.FuiStyle("row inert", selected ? "armed" : hover && present ? "hover" : null);
                frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                    r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
                rail.color = !present ? OpsInk.Hairline : selected ? OpsInk.Select : marked ? OpsInk.Rail(AvState.Ready) : ClassInk(cls);
                title.color = present ? OpsInk.Ink : OpsInk.Muted;
                sub.color = marked ? OpsInk.Word(AvState.Ready) : OpsInk.Dim;
            }
        }

        /// <summary>A TASKED card: target and payoff, source, the host quote (price) and one state word.</summary>
        private sealed class Card
        {
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text title, sub, price, state;
            private int postId;
            private bool present, armed, enabled, hover;
            private AvState tone = AvState.Inert;

            public Card(RectTransform parent, FeedBox box, Action<int> press)
            {
                RectTransform root = AvLay.Child(parent, "Card");
                AvLay.Place(root, box.X, box.Y, box.W, box.H);
                frame = AvFrame.Add(root, "Frame", default(AvChamfer));
                AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(root, "Rail", Color.clear);
                AvLay.Place(rail.rectTransform, 0f, 0f, 3f, box.H);
                float right = Math.Min(132f, box.W * 0.3f), left = box.W - right - 14f;
                title = Line(root, "Title", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
                sub = Line(root, "Sub", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
                price = Line(root, "Price", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
                state = Line(root, "State", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
                AvLay.Place(title.rectTransform, 10f, 3f, left, 18f);
                AvLay.Place(sub.rectTransform, 10f, 21f, left, 16f);
                AvLay.Place(price.rectTransform, box.W - right - 6f, 3f, right, 18f);
                AvLay.Place(state.rectTransform, box.W - right - 6f, 21f, right, 16f);
                AvHit hit = AvHit.On(frame);
                hit.Hover = h => { hover = h; Restyle(); };
                hit.Click = e => { if (present && enabled && e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) press(postId); };
                root.gameObject.SetActive(true);
                Restyle();
            }

            public void Paint(in FeedCardView v)
            {
                bool changed = present != v.Present || armed != v.Armed || enabled != v.Enabled || tone != v.Tone;
                present = v.Present; armed = v.Armed; enabled = v.Enabled; tone = v.Tone; postId = v.PostId;
                OpsText.Set(title, v.Present ? v.Title : "NO TASKED CALL");
                OpsText.Set(sub, v.Present ? v.Sub : "POSTED CALLS APPEAR HERE");
                OpsText.Set(price, v.Present ? v.Price : "");
                OpsText.Set(state, v.Present ? v.State : "");
                if (changed) Restyle();
            }

            public void Restyle()
            {
                if (frame == null) return;
                AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(present ? tone : AvState.Inert), armed ? "armed" : hover && enabled ? "hover" : null);
                frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                    r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
                rail.color = armed ? OpsInk.Select : present ? OpsInk.Rail(tone) : OpsInk.Hairline;
                title.color = present ? OpsInk.Ink : OpsInk.Muted;
                sub.color = OpsInk.Dim;
                price.color = present ? OpsInk.Word(tone == AvState.Inert ? AvState.Info : tone) : OpsInk.Muted;
                state.color = present ? OpsInk.Word(tone) : OpsInk.Muted;
            }
        }

        /// <summary>Does nothing: the offline harness paints without a controller.</summary>
        private sealed class NullActions : ISpaceFeedActions
        {
            public static readonly NullActions Instance = new NullActions();
            public void SelectEntry(int id) { }
            public void PrevPage() { }
            public void NextPage() { }
            public void Confirm() { }
            public void Send() { }
            public void SetSource(BirdKind source) { }
            public void CycleZoom() { }
            public void ToggleFull() { }
            public void PressCard(int postId) { }
            public void Touch() { }
        }
    }

    /// <summary>Shared arithmetic of the OPS console pages (CALLS and SPACE).</summary>
    internal static class OpsPage
    {
        /// <summary>The flow's own top and bottom padding (pad above and below); a page's content must fit its viewport minus this.</summary>
        public const float FlowInset = 2f * AvGridTokens.Pad;

        /// <summary>The width a full-width part gets in a page flow of a standard console (480 less both pads and the gutter).</summary>
        public const float BoardWidth = AvTokens.PanelWidth - 2f * AvGridTokens.Pad - AvGridTokens.Gutter;

        /// <summary>Viewport height of one page under the chrome: header, optional tab bar, the footer. No chip or metric strip.</summary>
        public static float BodyHeight(float consoleHeight, bool tabs) =>
            consoleHeight - AvGridTokens.Footer - (AvGridTokens.Header + 4f) - (tabs ? AvGridTokens.Tab + 4f : 0f);
    }
}
