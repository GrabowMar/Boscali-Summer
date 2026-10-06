using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
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

    /// <summary>One track-file row: a host-revealed contact (or a MARK whose reveal lapsed, a fixed point).</summary>
    internal struct FeedTileView
    {
        public bool Present, Selected, Marked, Moving, FixedPoint;
        public int Id;
        public byte Percent;
        public ProbableClass Class;
        public string Title, Sub;
    }

    /// <summary>One TASKED row: target count and payoff, source, the host quote, a short state word and the full words for the hover help.</summary>
    internal struct FeedCardView
    {
        public bool Present, Armed, Enabled;
        public int PostId;
        public string Title, Chip, Sub, Price, State, Detail, Button;
        /// <summary>The domain slab of the post (SPC, CYB, SOF); empty reads SPC.</summary>
        public string Slab;
        public AvState Tone;
    }

    /// <summary>One constellation cell: the bird, its real state word and the tone of that word.</summary>
    internal struct FeedBirdView
    {
        public string State;
        public AvState Tone;
    }

    /// <summary>
    /// The one view model both surfaces paint: the ORBIT page on the OPS MFD and the full-screen station. Built each refresh
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
        /// <summary>The single line that replaces the track-file rows when none is revealed (empty when there are contacts).</summary>
        public string NoContacts = "";
        public AvState NoContactsTone = AvState.Inert;
        public int Page, Pages = 1;
        public int SelectedId;
        public bool CanConfirm, CanSend, ConfirmFull, ZoomEnabled = true;
        public int SendCount;
        public string ConfirmHelp = "", SendHelp = "";
        public string TaskedCaption = "";
        public readonly FeedCardView[] Cards = new FeedCardView[C2Board.Rows896];
        public int CardCount;
        /// <summary>Posts on the faction board still claimable or launching, and those gone stale (the BOARD page header).</summary>
        public int PostsLive, PostsStale;
        /// <summary>The three bird cells in <see cref="BirdKind"/> order: OPTICAL, RADAR, KINETIC.</summary>
        public readonly FeedBirdView[] Birds = new FeedBirdView[3];
        public string ConstellationMeta = "";
        /// <summary>The host console the station shows (the OPS page shows it on CAP).</summary>
        public C2Console Console;
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
    /// The SPACE feed, laid out as the ORBIT page of the C2 terminal (and, wider, as the full-screen tasking station): threat strip,
    /// constellation cells with the cosmetic orbit art, the sensor frame (OPTICAL / RADAR / ZOOM / FULL toolbar, the picture with
    /// host-revealed brackets and a one-line status), the track file with its page buttons, CONFIRM / TRANSMIT and the TASKED rows.
    /// Every height is fixed after construction (<see cref="SpaceFeedLayout"/>); only the picture takes the leftover height. Text is
    /// fitted with an ellipsis to its slot, never wraps and never moves a row. The panel owns no policy: every press is a request.
    /// </summary>
    internal sealed class SpaceFeedPanel : AvPart
    {
        private static readonly BirdKind[] Birds = { BirdKind.Optical, BirdKind.Radar, BirdKind.Kinetic };
        private readonly ISpaceFeedActions actions;
        private readonly SpaceFeedLayout layout;
        private readonly float boardHeight;
        private readonly List<AvPart> parts = new List<AvPart>();

        // Threat strip.
        private readonly Image threatBack, threatRail;
        private readonly TMP_Text threatText;

        // Constellation.
        private readonly C2Box constBox;
        private readonly AvFrame constFrame;
        private readonly OrbitArt art;
        private readonly TMP_Text[] birdKey = new TMP_Text[3], birdState = new TMP_Text[3], birdTele = new TMP_Text[3];
        private readonly Image[] cellRule = new Image[2];

        // Sensor frame.
        private readonly C2Box sensorBox;
        private readonly AvControl opticalButton, radarButton, zoomButton, fullButton;
        private readonly RectTransform imageRoot;
        private readonly Image imageBack, statusBack;
        private readonly RawImage picture;
        private readonly TMP_Text refusalText, statusText;
        private readonly Bracket[] brackets = new Bracket[SpaceFeedView.MaxBrackets];

        // Track file, actions, TASKED.
        private readonly C2Box trackBox, taskedBox;
        private readonly AvControl prevButton, nextButton, confirmButton, sendButton;
        private readonly C2Row[] trackRows, cardRows;
        private readonly int[] trackIds, cardIds;
        private readonly TMP_Text noContactsText, noTaskedText;
        private readonly C2ConsoleView consoleView;

        // What each track / TASKED row last painted: a row whose data is unchanged is skipped before any string is built.
        private FeedTileView[] shownTiles;
        private FeedCardView[] shownCards;
        private bool[] tileKnown, cardKnown;
        private int metaPage = -1, metaPages = -1, labelSelected = -1, labelSend = -1, metaCards = -1;
        private bool metaNone, labelFull, labelsKnown;

        private AvState threatTone = AvState.Inert;
        private string threatRaw = "", statusRaw = "", noTaskedRaw = "";
        private AvState statusTone = AvState.Info;

        public SpaceFeedPanel(RectTransform parent, ISpaceFeedActions actions, float width, float height, bool full)
        {
            this.actions = actions ?? NullActions.Instance;
            boardHeight = height;
            layout = SpaceFeedLayout.Compute(width, height, full);
            Rect = AvLay.Child(parent, "SpaceFeed");
            AvLay.Place(Rect, 0f, 0f, width, height);

            // ---- Threat strip (the warning bar in the station) ----
            threatBack = AvLay.Solid(Rect, "ThreatBack", Color.clear);
            threatRail = AvLay.Solid(Rect, "ThreatRail", Color.clear);
            threatText = C2Kit.Mono(Rect, "Threat", full ? 16f : 10.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
            Put(threatBack.rectTransform, layout.Threat);
            AvLay.Place(threatRail.rectTransform, layout.Threat.X, layout.Threat.Y, 3f, layout.Threat.H);
            Put(threatText.rectTransform, new FeedBox(layout.Threat.X + 10f, layout.Threat.Y, layout.Threat.W - 14f, layout.Threat.H));

            // ---- Constellation ----
            if (layout.Art)
            {
                constBox = Add(new C2Box(Rect, "CONSTELLATION"));
                constBox.BodyHeight = layout.Constellation.H - C2Box.HeaderH;
                constBox.Place(Slot(layout.Constellation));
                art = Add(new OrbitArt(Rect, layout.ConstArt.W, layout.ConstArt.H));
                AvLay.Place(art.Rect, layout.ConstArt.X, layout.ConstArt.Y, layout.ConstArt.W, layout.ConstArt.H);
            }
            else
            {
                constFrame = AvFrame.Add(Rect, "ConstFrame", default(AvChamfer));
                Put(constFrame.rectTransform, layout.Constellation);
            }
            for (int i = 0; i < 3; i++)
            {
                FeedBox cell = layout.BirdCell(i);
                birdKey[i] = C2Kit.Mono(Rect, "BirdKey" + i, 10f, TextAlignmentOptions.MidlineLeft, false, 2f);
                birdState[i] = C2Kit.Mono(Rect, "BirdState" + i, 12f, TextAlignmentOptions.MidlineRight, true);
                birdTele[i] = C2Kit.Mono(Rect, "BirdTele" + i, 10f, TextAlignmentOptions.MidlineLeft);
                AvLay.Place(birdKey[i], cell.X + 8f, cell.Y + 2f, cell.W * 0.5f - 8f, 13f);
                AvLay.Place(birdState[i], cell.X + cell.W * 0.5f, cell.Y + 1f, cell.W * 0.5f - 8f, 16f);
                AvLay.Place(birdTele[i], cell.X + 8f, cell.Y + cell.H - 13f, cell.W - 12f, 12f);
                birdKey[i].text = C2Orbit.BirdName(Birds[i]);
                birdTele[i].text = C2Orbit.Telemetry(Birds[i]); // cosmetic
                if (i > 0)
                {
                    cellRule[i - 1] = AvLay.Solid(Rect, "CellRule" + i, Color.clear);
                    AvLay.Place(cellRule[i - 1].rectTransform, cell.X, cell.Y + 3f, 1f, cell.H - 6f);
                }
            }

            // ---- Sensor frame ----
            sensorBox = Add(new C2Box(Rect, "SENSOR FRAME"));
            sensorBox.BodyHeight = layout.Sensor.H - C2Box.HeaderH;
            sensorBox.Place(Slot(layout.Sensor));
            FeedBox tb = layout.Toolbar;
            float bw = (tb.W - 3f * 4f) / 4f;
            opticalButton = Button("OPTICAL", () => Do(() => this.actions.SetSource(BirdKind.Optical)), AvButtonStyle.Default, tb.X, tb.Y, bw, tb.H,
                "Show the OPTICAL bird's camera picture. Daylight only: cloud softens it, night refuses.");
            radarButton = Button("RADAR", () => Do(() => this.actions.SetSource(BirdKind.Radar)), AvButtonStyle.Default, tb.X + (bw + 4f), tb.Y, bw, tb.H,
                "Show the RADAR bird's SAR product from the last RADAR SCAN or MTI SWEEP.");
            zoomButton = Button("ZOOM WIDE", () => Do(this.actions.CycleZoom), AvButtonStyle.Quiet, tb.X + 2f * (bw + 4f), tb.Y, bw, tb.H,
                "Cycle the picture zoom: WIDE, MID, CLOSE. CLOSE centres on the selected target.");
            fullButton = Button(full ? "EXIT" : "OPEN FULL", () => Do(this.actions.ToggleFull), AvButtonStyle.Quiet, tb.X + 3f * (bw + 4f), tb.Y, bw, tb.H,
                full ? "Close the full-screen feed (Esc)." : "Open the full-screen tasking station. Keyboard and joystick stay live; the mouse drives the feed.");

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
            statusBack = AvLay.Solid(imageRoot, "StatusBack", Color.clear);
            statusBack.raycastTarget = false;
            AvLay.Place(statusBack.rectTransform, 0f, im.H - 16f, im.W, 16f);
            statusText = C2Kit.Mono(imageRoot, "Status", 10f, TextAlignmentOptions.MidlineLeft);
            AvLay.Place(statusText, 6f, im.H - 16f, im.W - 12f, 16f);
            for (int i = 0; i < brackets.Length; i++) brackets[i] = new Bracket(imageRoot, i, id => Do(() => this.actions.SelectEntry(id)));

            // ---- Track file ----
            trackBox = Add(new C2Box(Rect, "TRACK FILE · HOST-REVEALED ONLY"));
            trackBox.BodyHeight = layout.Track.H - C2Box.HeaderH;
            trackBox.MetaInset = 62f;
            trackBox.Place(Slot(layout.Track));
            float navX = layout.Track.Right - 6f - 24f;
            prevButton = IconButton(AvIcon.ChevronLeft, () => Do(this.actions.PrevPage), navX - 26f, layout.Track.Y + 1f, 24f, 18f, "Previous page of contact targets.");
            nextButton = IconButton(AvIcon.ChevronRight, () => Do(this.actions.NextPage), navX, layout.Track.Y + 1f, 24f, 18f, "Next page of contact targets.");
            trackRows = new C2Row[layout.TrackRows];
            trackIds = new int[layout.TrackRows];
            shownTiles = new FeedTileView[layout.TrackRows];
            tileKnown = new bool[layout.TrackRows];
            for (int i = 0; i < trackRows.Length; i++)
            {
                int slot = i;
                trackRows[i] = Add(new C2Row(Rect, SpaceFeedLayout.RowH, false));
                trackRows[i].Place(Slot(layout.TrackRow(i)));
                trackRows[i].Primary.Clicked += () => Do(() => this.actions.SelectEntry(trackIds[slot]));
            }
            noContactsText = C2Kit.Mono(Rect, "NoContacts", 10.5f, TextAlignmentOptions.MidlineLeft, true);
            noContactsText.richText = false;
            FeedBox r0 = layout.TrackRow(0);
            AvLay.Place(noContactsText, r0.X + 8f, r0.Y, r0.W - 16f, SpaceFeedLayout.RowH * 2f);
            noContactsText.enableWordWrapping = true;
            noContactsText.alignment = TextAlignmentOptions.TopLeft;
            noContactsText.gameObject.SetActive(false);

            // ---- Actions ----
            FeedBox c0 = layout.ActionButton(0), c1 = layout.ActionButton(1);
            confirmButton = Button("CONFIRM", () => Do(this.actions.Confirm), AvButtonStyle.Primary, c0.X, c0.Y, c0.W, c0.H,
                "MARK the selected target. The host answers CONFIRMED, NEUTRAL, DECOY or FRIENDLY; only CONFIRMED can be posted.");
            sendButton = Button("TRANSMIT", () => Do(this.actions.Send), AvButtonStyle.Primary, c1.X, c1.Y, c1.W, c1.H,
                "Post your live MARKs as one TASKED call to the board. The first pilot to claim it fires.");

            // ---- TASKED ----
            taskedBox = Add(new C2Box(Rect, "TASKED CALLS"));
            taskedBox.BodyHeight = layout.Tasked.H - C2Box.HeaderH;
            taskedBox.Place(Slot(layout.Tasked));
            cardRows = new C2Row[layout.CardRows];
            cardIds = new int[layout.CardRows];
            shownCards = new FeedCardView[layout.CardRows];
            cardKnown = new bool[layout.CardRows];
            for (int i = 0; i < cardRows.Length; i++)
            {
                int slot = i;
                cardRows[i] = Add(new C2Row(Rect, SpaceFeedLayout.CardH, true));
                cardRows[i].Place(Slot(layout.CardRow(i)));
                cardRows[i].Primary.Clicked += () => Do(() => this.actions.PressCard(cardIds[slot]));
            }
            noTaskedText = C2Kit.Mono(Rect, "NoTasked", 10.5f, TextAlignmentOptions.MidlineLeft);
            FeedBox t0 = layout.CardRow(0);
            AvLay.Place(noTaskedText, t0.X + 8f, t0.Y, t0.W - 16f, SpaceFeedLayout.CardH);

            // ---- Host console (station only) ----
            if (layout.ConsoleLines > 0)
            {
                consoleView = Add(new C2ConsoleView(Rect, layout.ConsoleLines));
                consoleView.Place(Slot(layout.Console));
            }

            Restyle();
        }
        public SpaceFeedLayout Layout => layout;

        /// <summary>The footer words and tone of the last paint (the ORBIT page hands them to the OPS footer; the station has its own).</summary>
        public string Words { get; private set; } = "";
        public AvState WordsTone { get; private set; } = AvState.Inert;

        private T Add<T>(T part) where T : AvPart
        {
            parts.Add(part);
            return part;
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
            Words = view.Words;
            WordsTone = view.WordsTone;

            SetTone(ref threatTone, view.ThreatActive ? AvState.Danger : view.Ground ? AvState.Inert : AvState.Ready);
            SetText(threatText, ref threatRaw, view.Threat, layout.Threat.W - 18f);

            PaintConstellation(view);

            sensorBox.SetMeta(C2Orbit.SensorMeta(view.Source, view.Zoom, view.ZoomEnabled));
            opticalButton.Latched = view.Source == BirdKind.Optical;
            radarButton.Latched = view.Source == BirdKind.Radar;
            SetLabel(zoomButton, view.ZoomEnabled ? "ZOOM " + SpaceFeedRules.ZoomWord(view.Zoom) : "ZOOM FIXED");
            SetEnabled(zoomButton, view.ZoomEnabled);
            PaintPicture(view);
            SetText(statusText, ref statusRaw, view.Status, layout.Image.W - 14f);
            if (statusTone != view.StatusTone) { statusTone = view.StatusTone; RestyleStatus(); }

            PaintTrack(view);

            SetEnabled(confirmButton, view.CanConfirm);
            if (!labelsKnown || labelSelected != view.SelectedId || labelFull != view.ConfirmFull || labelSend != view.SendCount)
            {
                labelsKnown = true; labelSelected = view.SelectedId; labelFull = view.ConfirmFull; labelSend = view.SendCount;
                SetLabel(confirmButton, C2Orbit.ConfirmLabel(view.SelectedId, view.ConfirmFull));
                SetLabel(sendButton, C2Orbit.TransmitLabel(view.SendCount));
            }
            SetHelp(confirmButton, view.ConfirmHelp);
            SetEnabled(sendButton, view.CanSend);
            SetHelp(sendButton, view.SendHelp);

            PaintTasked(view);
            consoleView?.Show(view.Console);
        }

        private void PaintConstellation(SpaceFeedView view)
        {
            constBox?.SetMeta(view.ConstellationMeta);
            for (int i = 0; i < 3; i++)
            {
                FeedBirdView b = view.Birds[i];
                string word = string.IsNullOrEmpty(b.State) ? "—" : b.State;
                if (AvText.Set(birdState[i], word) || birdState[i].color != OpsInk.Word(b.Tone))
                    birdState[i].color = b.Tone == AvState.Inert ? AvInk.Muted : OpsInk.Word(b.Tone);
            }
            art?.SetStates(view.Birds[0].Tone, view.Birds[1].Tone, view.Birds[2].Tone);
        }

        private void PaintPicture(SpaceFeedView view)
        {
            bool show = view.Image != null && view.ImageKind != FeedImageKind.None && view.Refusal.Length == 0;
            if (picture.enabled != show) picture.enabled = show;
            AvText.Set(refusalText, show ? "" : view.Refusal);
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

        private void PaintTrack(SpaceFeedView view)
        {
            bool none = view.NoContacts.Length > 0;
            if (none != metaNone || view.Page != metaPage || view.Pages != metaPages || metaPages < 0)
            {
                metaNone = none; metaPage = view.Page; metaPages = view.Pages;
                trackBox.SetMeta(none ? "NO TRACKS" : C2Orbit.PageMeta(view.Page, view.Pages));
            }
            SetEnabled(prevButton, !none && view.Page > 0);
            SetEnabled(nextButton, !none && view.Page + 1 < view.Pages);
            if (noContactsText.gameObject.activeSelf != none) noContactsText.gameObject.SetActive(none);
            AvText.Set(noContactsText, view.NoContacts);
            noContactsText.color = OpsInk.Word(view.NoContactsTone == AvState.Inert ? AvState.Info : view.NoContactsTone);
            for (int i = 0; i < trackRows.Length; i++)
            {
                FeedTileView t = i < view.Tiles.Length ? view.Tiles[i] : default;
                bool on = t.Present && !none;
                if (trackRows[i].Rect.gameObject.activeSelf != on) trackRows[i].Rect.gameObject.SetActive(on);
                if (!on) { tileKnown[i] = false; continue; }
                trackIds[i] = t.Id;
                if (tileKnown[i] && SameTile(shownTiles[i], t)) continue;
                shownTiles[i] = t;
                tileKnown[i] = true;
                AvState cls = t.Class == ProbableClass.Hostile ? AvState.Danger : t.Class == ProbableClass.Friendly ? AvState.Ready
                    : t.Class == ProbableClass.Neutral ? AvState.Info : AvState.Caution;
                trackRows[i].Armed = t.Selected;
                trackRows[i].Set(C2Orbit.TrackId(t.Id), t.Title, t.Percent > 0 && t.Percent <= 100 ? t.Percent + "%" : "", cls, "",
                    t.Moving ? "MOVING" : "STATIC", t.Marked ? "MARKED" : t.FixedPoint ? "FIXED" : "", t.Marked ? AvState.Ready : AvState.Inert,
                    t.Selected ? "SELECTED" : "SELECT", t.Selected ? AvButtonStyle.Default : AvButtonStyle.Primary, !t.Selected);
                trackRows[i].SetHelp("Select this target on the picture. CONFIRM then MARKs it.", "");
            }
        }

        private void PaintTasked(SpaceFeedView view)
        {
            taskedBox.SetTitle(view.TaskedCaption);
            if (view.CardCount != metaCards) { metaCards = view.CardCount; taskedBox.SetMeta(C2Orbit.TaskedMeta(view.CardCount)); }
            bool none = view.CardCount == 0;
            string empty = "NO TASKED CALL · POSTED CALLS APPEAR HERE";
            if (noTaskedText.gameObject.activeSelf != none) noTaskedText.gameObject.SetActive(none);
            SetText(noTaskedText, ref noTaskedRaw, none ? empty : "", layout.CardRow(0).W - 16f);
            noTaskedText.color = AvInk.Muted;
            for (int i = 0; i < cardRows.Length; i++)
            {
                FeedCardView c = i < view.CardCount ? view.Cards[i] : default;
                bool on = c.Present;
                if (cardRows[i].Rect.gameObject.activeSelf != on) cardRows[i].Rect.gameObject.SetActive(on);
                if (!on) { cardKnown[i] = false; continue; }
                cardIds[i] = c.PostId;
                if (cardKnown[i] && SameCard(shownCards[i], c)) continue;
                shownCards[i] = c;
                cardKnown[i] = true;
                cardRows[i].Armed = c.Armed;
                cardRows[i].Set((i + 1).ToString("00"), c.Title, c.Chip, AvState.Ready, c.Sub, c.Price, c.State, c.Tone, c.Button,
                    c.Armed ? AvButtonStyle.Danger : c.Enabled && c.Tone == AvState.Ready ? AvButtonStyle.Primary : AvButtonStyle.Default, c.Enabled);
                cardRows[i].SetHelp(c.Detail, "");
                if (cardRows[i].Primary.Help != c.Detail) cardRows[i].Primary.Help = c.Detail;
            }
        }

        private static bool SameTile(in FeedTileView a, in FeedTileView b) =>
            a.Selected == b.Selected && a.Marked == b.Marked && a.Moving == b.Moving && a.FixedPoint == b.FixedPoint && a.Id == b.Id &&
            a.Percent == b.Percent && a.Class == b.Class && a.Title == b.Title && a.Sub == b.Sub;

        private static bool SameCard(in FeedCardView a, in FeedCardView b) =>
            a.Armed == b.Armed && a.Enabled == b.Enabled && a.PostId == b.PostId && a.Tone == b.Tone && a.Title == b.Title && a.Chip == b.Chip &&
            a.Sub == b.Sub && a.Price == b.Price && a.State == b.State && a.Detail == b.Detail && a.Button == b.Button;

        private void SetTone(ref AvState current, AvState next)
        {
            if (current == next) return;
            current = next;
            RestyleStrip();
        }

        private void SetText(TMP_Text text, ref string raw, string value, float room)
        {
            string v = value ?? "";
            if (v == raw && text.text.Length > 0 == (v.Length > 0)) return;
            raw = v;
            AvText.Set(text, C2Kit.FitTo(text, v, room));
        }

        // ---- Theme -----------------------------------------------------------------------------------------------

        public override void Restyle()
        {
            if (threatBack == null) return;
            RestyleStrip();
            RestyleStatus();
            imageBack.color = new Color(0.02f, 0.03f, 0.03f, 1f);
            foreach (AvPart p in parts) p.Restyle();
            constFrame?.Paint(AvInk.Inert, AvInk.Hairline);
            foreach (TMP_Text t in birdKey) if (t != null) t.color = AvInk.Muted;
            foreach (TMP_Text t in birdTele) if (t != null) t.color = AvInk.Dim;
            foreach (Image r in cellRule) if (r != null) r.color = AvInk.Hairline;
            if (art != null) art.Restyle();
            foreach (AvControl c in new[] { opticalButton, radarButton, zoomButton, fullButton, prevButton, nextButton, confirmButton, sendButton })
                c?.Restyle();
            if (noTaskedText != null) noTaskedText.color = AvInk.Muted;
            if (brackets != null) foreach (Bracket b in brackets) b?.Restyle();
        }

        private void RestyleStrip()
        {
            if (threatBack == null) return;
            AvStyle row = AvStyleHost.FuiStyle("row " + AvStates.Class(threatTone));
            Color back = AvStyleHost.Resolve(row.Background, AvTheme.SurfaceInert);
            threatBack.color = threatTone == AvState.Danger ? Color.Lerp(back, OpsInk.Rail(AvState.Danger), 0.22f) : back;
            threatRail.color = OpsInk.Rail(threatTone);
            threatText.color = threatTone == AvState.Inert ? AvInk.Dim : OpsInk.Word(threatTone);
        }

        private void RestyleStatus()
        {
            if (statusBack == null) return;
            statusBack.color = OpsInk.A(AvStyleHost.FuiColor("ground", Color.black), 0.72f);
            statusText.color = OpsInk.Word(statusTone == AvState.Inert ? AvState.Info : statusTone);
        }

        // ---- Small helpers ---------------------------------------------------------------------------------------

        private static void SetEnabled(AvControl c, bool enabled) { if (c.Interactable != enabled) c.Interactable = enabled; }
        private static void SetLabel(AvControl c, string label) { if (c.Label != label) c.Label = label; }
        private static void SetHelp(AvControl c, string help) { if (!string.IsNullOrEmpty(help) && c.Help != help) c.Help = help; }

        private static void Put(RectTransform t, FeedBox b) => AvLay.Place(t, b.X, b.Y, b.W, b.H);
        private static AvSlot Slot(FeedBox b) => new AvSlot(b.X, b.Y, b.W, b.H);

        private AvControl Button(string label, Action click, AvButtonStyle style, float x, float y, float w, float h, string help)
        {
            AvControl c = AvControl.Make(Rect, new AvControl.Spec(label, click, style));
            c.SingleLine();
            c.Help = help;
            AvLay.Place(c.Rect, x, y + 1f, w, h - 2f);
            return c;
        }

        private AvControl IconButton(AvIcon icon, Action click, float x, float y, float w, float h, string help)
        {
            AvControl c = AvControl.Make(Rect, new AvControl.Spec("", click, AvButtonStyle.Quiet, icon));
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
                label = OpsText.Line(root, "Label", AvTextRole.Micro, TextAlignmentOptions.Top);
                label.fontSizeMin = AvTokens.FontMicro;
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
                AvText.Set(label, b.Label);
                Restyle();
            }

            public void Restyle()
            {
                if (outline == null) return;
                Color ink = selected ? AvInk.Select : marked ? OpsInk.Rail(AvState.Ready) : ClassInk(cls);
                outline.StrokeColor = ink;
                outline.BracketColor = ink;
                outline.Stroke = selected || marked ? 2.2f : 1.4f;
                outline.SetVerticesDirty();
                label.color = selected ? AvInk.Select : AvInk.Ink;
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

    /// <summary>Shared arithmetic of the OPS console pages and the station.</summary>
    internal static class OpsPage
    {
        /// <summary>The flow's own top and bottom padding (pad above and below); a page's content must fit its viewport minus this.</summary>
        public const float FlowInset = 2f * AvGridTokens.Pad;
    }
}
