using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// COC — one faction's chain of command at a time, switched by an ALLIED/HOSTILE toggle.
    ///
    /// <para>This is a reading board, not a control board: the staff lives on its own. Every
    /// commander earns their faction a bonus while they serve (income, kill value, patrol
    /// reach), occasionally travels between bases in a VIP convoy, and can be killed at their
    /// post or on the road; a successor takes over and the bonus is interrupted. Nothing here
    /// can be ordered, marked or spent - the page exists so a pilot knows who runs the enemy
    /// side of the map, where they are, and what killing them costs.</para>
    ///
    /// <para>The full-width roster is followed by the selected file and staff log.
    /// Selecting a row scrolls to its file; BACK TO POSTS returns to the roster.
    /// Enemy posts are listed by identity; until local intel
    /// confirms one, its portrait and rail stay inert and its card reads unconfirmed.</para>
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        /// <summary>
        /// HighCommand's wire ceiling is eight posts per faction; rows are pooled once.
        /// </summary>
        private const int CocRosterRows = 8;

        /// <summary>The title line, the hairline under it and the reading, down to the roster.</summary>
        private const float CocHeaderHeight = 38f;

        private const float CocRowGap = 5f;

        /// <summary>
        /// Personnel details use full-width key/value rows below the portrait and identity.
        /// </summary>
        private const float CocDetailHeight = 400f;
        private const float CocPortraitWidth = 84f;
        private const float CocPortraitHeight = 104f;
        private const int CocBonusEntries = 3;
        private const int CocRedactionBars = 3;
        private const float CocDetailPad = 14f;

        /// <summary>
        /// The band the ALLIED/HOSTILE switch sits in. On the header line it overlapped the
        /// card's form number and the card's frame ran through its middle.
        /// </summary>
        private const float CocToggleBand = 40f;

        /// <summary>Where the record's own fields begin, under the fixed identity block.</summary>
        private const float CocFileFieldsTop = 168f;
        private const float CocFormLine = 14f;
        private const float CocBonusEntryHeight = 27f;

        private const int CocRuleFields = 0;
        private const int CocRuleDisposition = 1;
        private const int CocRuleShare = 2;
        private const int CocRuleBonus = 3;
        private const int CocRuleRecord = 4;

        /// <summary>The staff log follows the selected file.</summary>
        private const int CocLogRows = 4;
        /// <summary>A log sentence gets two lines at the small face, not one clipped one.</summary>
        private const float CocLogPitch = 30f;
        private const float CocLogHeader = 26f;
        private const float CocLogGap = 6f;
        private const float CocLogBlock = CocLogHeader + CocLogRows * CocLogPitch + CocLogGap;
        private const float CocEmptyDossierHeight = 104f;

        private readonly CommanderRow[] cocRows = new CommanderRow[CocRosterRows];
        private readonly CommanderView[] cocVisible = new CommanderView[CocRosterRows];
        private readonly int[] cocIds = new int[CocRosterRows];
        private readonly int[] cocParents = new int[CocRosterRows];
        private readonly int[] cocOrder = new int[CocRosterRows];
        private readonly bool[] cocOrdered = new bool[CocRosterRows];
        private readonly CocLogRow[] cocLogRows = new CocLogRow[CocLogRows];
        private readonly CocBonusEntry[] cocBonusEntries = new CocBonusEntry[CocBonusEntries];
        private readonly Image[] cocRedactions = new Image[CocRedactionBars];
        private RectTransform cocRoot, cocLogBlock, cocDossierBlock, cocDossierEmpty;
        private RectTransform cocPortraitWell;
        private Image cocSpine;
        private Rect cocBody;
        private ScrollRect cocScroll;
        private bool cocFocusFile;
        private float cocRosterTop, cocRowPitch, cocRowHeight, cocDossierWidth, cocDossierHeight;
        private int cocFilledRows;
        private TMP_Text cocTreeNote, cocLogNote;
        private TMP_Text cocFileNo, cocPhotoRef, cocName, cocRole, cocBio, cocEndNote;
        private TMP_Text cocBonusNote, cocRecordNote;
        private Image cocDossierFill, cocDossierRail, cocShareTrack, cocShareBar;
        private Image[] cocDossierBorder, cocRules;
        private CocFormRow cocRankRow, cocStationRow, cocShareRow, cocDispositionRow;
        private Image cocPortrait;
        private TMP_Text cocPortraitFallback;
        private CocPips cocDossierPips;
        private AvButton cocAlliedToggle, cocHostileToggle;
        private bool cocShowHostile;
        private int cocSelectedId = -1;
        private int cocPortraitSeed = int.MinValue;

        private static readonly Color CocPortraitBack = new Color32(6, 10, 16, 255);
        private static readonly Color CocHover = new Color(1f, 1f, 1f, 0.06f);

        private void ResetCoc()
        {
            Array.Clear(cocRows, 0, cocRows.Length);
            Array.Clear(cocVisible, 0, cocVisible.Length);
            Array.Clear(cocIds, 0, cocIds.Length);
            Array.Clear(cocParents, 0, cocParents.Length);
            Array.Clear(cocOrder, 0, cocOrder.Length);
            Array.Clear(cocOrdered, 0, cocOrdered.Length);
            Array.Clear(cocLogRows, 0, cocLogRows.Length);
            Array.Clear(cocBonusEntries, 0, cocBonusEntries.Length);
            Array.Clear(cocRedactions, 0, cocRedactions.Length);
            cocRoot = cocLogBlock = cocDossierBlock = cocDossierEmpty = null;
            cocPortraitWell = null;
            cocSpine = null;
            cocBody = default(Rect);
            cocScroll = null;
            cocFocusFile = false;
            cocRosterTop = cocRowPitch = cocRowHeight = 0f;
            cocDossierWidth = cocDossierHeight = 0f;
            cocFilledRows = 0;
            cocTreeNote = cocLogNote = null;
            cocFileNo = cocPhotoRef = cocName = cocRole = cocBio = cocEndNote = null;
            cocBonusNote = cocRecordNote = null;
            cocDossierFill = cocDossierRail = cocShareTrack = cocShareBar = null;
            cocDossierBorder = cocRules = null;
            cocRankRow = cocStationRow = cocShareRow = cocDispositionRow = null;
            cocPortrait = null;
            cocPortraitFallback = null;
            cocDossierPips = null;
            cocAlliedToggle = cocHostileToggle = null;
            cocShowHostile = false;
            cocSelectedId = -1;
            cocPortraitSeed = int.MinValue;
        }

        private void BuildCocPage(GameObject page)
        {
            Rect view = shell.Body;

            // Readable rows keep the same rhythm in normal and compact bays.
            cocRowPitch = 54f;
            cocRowHeight = cocRowPitch - CocRowGap;

            float rosterHeight = CocToggleBand + CocHeaderHeight + CocRosterRows * cocRowPitch +
                                 CocLogBlock + 8f;
            Rect body;
            // Always keep a viewport: a long service record can grow after the roster builds.
            cocRoot = AvScreen.Scroll((RectTransform)page.transform, view,
                                      Mathf.Max(rosterHeight, view.height + 1f), out body);
            cocScroll = cocRoot.GetComponentInParent<ScrollRect>();
            cocBody = body;
            float usable = body.width - AvScreen.SpineInset;
            // All sections share one content column inside the scrolling viewport.
            float x = body.x + AvScreen.SpineInset;
            float detailWidth = usable;
            float detailX = x;
            float top = body.y - CocToggleBand;
            cocSpine = AvStyled.Spine(cocRoot, new Rect(body.x, body.y, 3f, body.height));

            float toggleWidth = (usable - 8f) * .5f;
            float toggleY = body.y - 2f;
            cocHostileToggle = AvStyled.Button(
                cocRoot, new Rect(x + usable - toggleWidth, toggleY, toggleWidth, 30f),
                "HOSTILE  —", "btn",
                () => { cocShowHostile = true; nextRefresh = 0f; },
                AvButtonStyle.Toggle)
                .WithTooltip("Show the opposing chain of command. A post stays unconfirmed until local intel has seen it.");
            cocAlliedToggle = AvStyled.Button(
                cocRoot,
                new Rect(x, toggleY, toggleWidth, 30f),
                "ALLIED  —", "btn",
                () => { cocShowHostile = false; nextRefresh = 0f; },
                AvButtonStyle.Toggle)
                .WithTooltip("Show the allied chain of command.");
            cocAlliedToggle.SetLatched(true);

            float headerBottom = SectionHeader(
                cocRoot, x, top, usable, "CHAIN OF COMMAND", null, band: false);
            // The reading gets its own line rather than the half-header beside the title: a
            // tally that gets ellipsised is a tally the panel did not give.
            cocTreeNote = AvStyled.Label(
                cocRoot, new Rect(x, headerBottom, usable, 12f), "", "section-title-note");

            float y = top - CocHeaderHeight;
            cocRosterTop = y;
            for (int i = 0; i < CocRosterRows; i++)
            {
                cocRows[i] = new CommanderRow(
                    cocRoot, x, y - i * cocRowPitch, usable, cocRowHeight, cocRowPitch, SelectCoc);
            }

            BuildCocLogBlock(x, y - CocRosterRows * cocRowPitch, usable);
            BuildCocDossierBlock(detailX, top, detailWidth);
        }

        /// <summary>The staff log: the sentences the status strip shows, kept and dated.</summary>
        private void BuildCocLogBlock(float x, float y, float width)
        {
            var root = new GameObject("CocLogBlock", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(cocRoot, false);
            AvKit.Place(rect, new Rect(x, y, width, CocLogBlock));
            cocLogBlock = rect;

            // The log is a section like every other on the panel, so it wears the same
            // title, rule and live right-hand count as the sections it sits under.
            float top = SectionHeader(rect, 0f, 0f, width, "STAFF LOG", null, false, out cocLogNote);
            for (int i = 0; i < cocLogRows.Length; i++)
                cocLogRows[i] = new CocLogRow(rect, 0f, top - 2f - i * CocLogPitch, width, SelectCoc);
        }

        /// <summary>
        /// The commander's file. Everything above the first field is a fixed block - the form
        /// number, the photo, the name and office - so the record under it can be laid out
        /// against the text it actually holds and the card's own frame can follow the record.
        /// </summary>
        private void BuildCocDossierBlock(float x, float y, float width)
        {
            var root = new GameObject("CocDossierBlock", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(cocRoot, false);
            AvKit.Place(rect, new Rect(x, y, width, CocDetailHeight));
            cocDossierBlock = rect;
            cocDossierWidth = width;
            cocDossierHeight = CocDetailHeight;

            float inner = width - CocDetailPad * 2f;

            Rect card = new Rect(0f, 0f, width, CocDetailHeight);
            cocDossierFill = AvKit.Panel(rect, card, AvTheme.Surface, AvSprites.Card);
            cocDossierBorder = AvKit.Outline(rect, card, AvTheme.Hairline);
            cocDossierRail = AvStyled.Rail(rect, new Rect(5f, -10f, 3f, CocDetailHeight - 20f), "locked");

            // ---- the form's own header ---------------------------------------------------
            // Keep the navigation action separate from the identity block below.
            AvStyled.Label(rect, new Rect(CocDetailPad, -10f, inner, 12f), "PERSONNEL FILE",
                           "file-form");
            cocFileNo = AvStyled.Label(rect, new Rect(CocDetailPad + CocPortraitWidth + 16f, -40f,
                inner - CocPortraitWidth - 16f, 14f), "", "row-sub");
            AvStyled.Button(rect, new Rect(width - 142f, -6f, 128f, 26f),
                "BACK TO POSTS", "btn", () =>
                {
                    if (cocScroll != null) cocScroll.verticalNormalizedPosition = 1f;
                }).WithTooltip("Return to the command roster; the current file stays selected.");
            Divider(rect, CocDetailPad, -38f, inner);

            // ---- photo and identity -------------------------------------------------------
            float portraitX = CocDetailPad;
            Rect frame = new Rect(portraitX, -44f, CocPortraitWidth, CocPortraitHeight);
            AvKit.Panel(rect, frame, CocPortraitBack);
            AvKit.Outline(rect, frame, AvTheme.Frame);

            cocPortraitFallback = AvStyled.Label(rect,
                new Rect(frame.x + 2f, frame.y - 34f, frame.width - 4f, 32f), "NO\nVISUAL", "row-sub",
                align: TextAlignmentOptions.Center);

            // The photo sits in a well of its own so the file can crop a portrait to a plate
            // rather than showing whatever shape the sprite happens to be.
            var wellObject = new GameObject("PortraitWell", typeof(RectTransform), typeof(RectMask2D));
            cocPortraitWell = (RectTransform)wellObject.transform;
            cocPortraitWell.SetParent(rect, false);
            AvKit.Place(cocPortraitWell,
                        new Rect(frame.x + 1f, frame.y - 1f, frame.width - 2f, frame.height - 2f));
            cocPortrait = AvKit.Panel(cocPortraitWell,
                new Rect(0f, 0f, frame.width - 2f, frame.height - 2f), Color.white);
            cocPortrait.type = Image.Type.Simple;
            cocPortrait.preserveAspect = false;
            cocPortrait.raycastTarget = false;
            cocPortrait.enabled = false;

            float identityX = CocDetailPad + CocPortraitWidth + 16f;
            float identityWidth = width - identityX - CocDetailPad;
            cocPhotoRef = AvStyled.Label(rect, new Rect(identityX, -126f, identityWidth, 16f), "", "row-sub");
            cocName = AvStyled.Label(rect, new Rect(identityX, -58f, identityWidth, 42f), "", "file-title");
            // A long name shrinks to fit the file rather than being cut off by it; fifteen
            // points is the size the card is drawn for, ten the smallest it may fall back to.
            cocName.fontSize = 18f;
            cocName.enableWordWrapping = true;
            cocName.enableAutoSizing = true;
            cocName.fontSizeMin = 14f;
            cocName.fontSizeMax = 18f;
            cocRole = AvStyled.Label(rect, new Rect(CocDetailPad, -188f, inner, 13f), "", "section-title-note");
            // The office is the second thing a reader looks for; the sheet's tracking would
            // ellipsise "GROUND COMPONENT CMDR" away, so this one line sets its own tracking.
            cocRole.characterSpacing = 0f;
            cocDossierPips = new CocPips(rect, 0f, 0f, AvTheme.Accent);

            // ---- the record below: laid out on every refresh ------------------------------
            cocRankRow = new CocFormRow(rect);
            cocStationRow = new CocFormRow(rect);
            cocShareRow = new CocFormRow(rect);
            cocDispositionRow = new CocFormRow(rect);
            cocShareTrack = AvStyled.Box(rect, new Rect(0f, 0f, 0f, 4f), "bar");
            cocShareBar = AvKit.Panel(rect, new Rect(0f, 0f, 0f, 2f), AvTheme.Accent);

            cocBonusNote = CocSectionLabel(rect);
            cocRecordNote = CocSectionLabel(rect);


            for (int i = 0; i < cocBonusEntries.Length; i++) cocBonusEntries[i] = new CocBonusEntry(rect);
            for (int i = 0; i < cocRedactions.Length; i++)
                cocRedactions[i] = AvStyled.Box(rect, new Rect(0f, 0f, 10f, 8f), "redact");

            cocBio = AvStyled.Label(rect, new Rect(0f, 0f, 10f, CocFormLine), "", "row-sub");
            cocEndNote = AvStyled.Label(rect, new Rect(0f, 0f, 10f, 12f), "END OF RECORD", "file-meta",
                align: TextAlignmentOptions.Center);

            cocRules = new Image[5];
            for (int i = 0; i < cocRules.Length; i++) cocRules[i] = CocClause(rect);

            var emptyObject = new GameObject("CocDossierEmpty", typeof(RectTransform));
            cocDossierEmpty = (RectTransform)emptyObject.transform;
            cocDossierEmpty.SetParent(cocRoot, false);
            AvKit.Place(cocDossierEmpty, new Rect(x, y, width, CocEmptyDossierHeight));
            AvKit.TacticalCard(cocDossierEmpty,
                new Rect(0f, 0f, width, CocEmptyDossierHeight), AvTheme.RailInfo);
            AvStyled.Label(cocDossierEmpty,
                new Rect(CocDetailPad, -18f, inner, 20f), "SELECT A POST", "file-title");
            AvStyled.Label(cocDossierEmpty,
                new Rect(CocDetailPad, -48f, inner, 36f),
                "Select a command post or staff-log entry to open its personnel file.", "hint");
            cocDossierEmpty.gameObject.SetActive(false);
        }

        /// <summary>
        /// Lay the record out against the text it holds, and report the height the file needs.
        /// A card that clips its own service record is a card that lied about the record.
        /// </summary>
        private float LayoutCocDossier(CommanderView view)
        {
            float width = cocDossierWidth;
            float inner = width - CocDetailPad * 2f;
            float x = CocDetailPad;
            bool sealedFile = view == null || (!view.IsFriendly && !view.IsKnown);
            Color value = sealedFile || (view != null && view.IsKia) ? AvTheme.Dim : AvTheme.TextPrimary;

            float y = -CocFileFieldsTop;
            y = cocRankRow.Bind(x, y, inner, "RANK", view?.Rank, value);
            y = cocStationRow.Bind(x, y, inner, "STATION", Pretty(view?.Location), value);
            y = CocClauseAt(cocRules[CocRuleFields], x, y - 8f, inner);

            y = cocDispositionRow.Bind(x, y - 4f, inner, "STATUS",
                view == null ? "NO FILE" : StatusOf(view), view == null ? AvTheme.Dim : StatusColor(view));
            y = CocClauseAt(cocRules[CocRuleDisposition], x, y, inner);

            y = cocShareRow.Bind(x, y - 4f, inner, "STAFF SHARE", Percent(view), value);
            y = PlaceShareBar(x, y, inner, view);
            y = CocClauseAt(cocRules[CocRuleShare], x, y, inner);

            y = CocSectionHeading(cocBonusNote, "BONUS", x, y - 4f, inner);
            y -= BindBonus(view == null ? null : view.Bonus, x, y, inner);
            y = CocClauseAt(cocRules[CocRuleBonus], x, y - 8f, inner);

            y = CocSectionHeading(cocRecordNote, "SERVICE RECORD", x, y - 4f, inner);
            y -= BindRecord(x, y, inner, view);
            y = CocClauseAt(cocRules[CocRuleRecord], x, y - 10f, inner);
            // The closing line is the sheet's footer, not the record's last row: the card is
            // resized after this and PlaceCocEnd pins it to whatever the card's bottom is.
            return -y + CocDetailPad + 16f;
        }

        /// <summary>Base names arrive off the wire as identifiers; a file prints words.</summary>
        private static string Pretty(string text) =>
            string.IsNullOrEmpty(text) ? text : text.Replace('_', ' ').ToUpperInvariant();

        private void SelectCoc(int id)
        {
            cocSelectedId = id;
            cocFocusFile = true;
            cocPortraitSeed = int.MinValue;
            nextRefresh = 0f;
        }

        private void RefreshCoc()
        {
            if (cocTreeNote == null) return;

            bool available = highCommand != null && highCommand.Available;
            if (!available)
            {
                // No staff: the log block belongs at the top of the tree, and the count the
                // nopost card hands back to PlaceCocLog is the empty one.
                cocFilledRows = 0;
                cocDossierHeight = CocEmptyDossierHeight;
                PlaceCocLog(0);
                cocDossierBlock.gameObject.SetActive(false);
                if (cocDossierEmpty != null) cocDossierEmpty.gameObject.SetActive(false);
                // An empty page is not a reading: say there is no board. The reason itself is
                // the host's own sentence, which only the status strip has room for.
                cocTreeNote.text = "NO STAFF BOARD";
                cocLogNote.text = "NO TRAFFIC";
                BindCocSideToggles("—", "—");
                for (int i = 0; i < cocRows.Length; i++) cocRows[i].Hide();
                for (int i = 0; i < cocLogRows.Length; i++) cocLogRows[i].Hide();
                if (highCommand != null) highCommand.Highlight(-1);
                return;
            }

            IReadOnlyList<CommanderView> list = highCommand.Commanders;
            int ownCount = 0, enemyTotal = 0, enemyKnown = 0, visible = 0;
            CommanderView selected = null;
            bool selectedOnActiveSide = false;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    CommanderView view = list[i];
                    if (view.IsFriendly) ownCount++;
                    else
                    {
                        enemyTotal++;
                        if (view.IsKnown) enemyKnown++;
                    }

                    bool activeSide = view.IsFriendly != cocShowHostile;
                    if (view.Id == cocSelectedId)
                    {
                        selected = view;
                        selectedOnActiveSide = activeSide;
                    }

                    if (!activeSide || visible >= CocRosterRows) continue;
                    cocVisible[visible] = view;
                    cocIds[visible] = view.Id;
                    cocParents[visible] = view.ParentId;
                    visible++;
                }
            }

            int ordered = CommandRosterOrder.Sort(visible, cocIds, cocParents, cocOrder, cocOrdered);
            for (int i = 0; i < ordered; i++)
            {
                CommanderView view = cocVisible[cocOrder[i]];
                cocRows[i].Bind(view, view.Id == cocSelectedId);
            }
            for (int i = ordered; i < CocRosterRows; i++) cocRows[i].Hide();

            PlaceCocLog(ordered);
            cocFilledRows = ordered;
            BindCocSideToggles(ownCount.ToString(), enemyTotal.ToString());
            RefreshCocLog();
            if (!cocShowHostile)
            {
                cocTreeNote.text = ownCount == 0
                    ? "NO POSTS REPORTED"
                    : ownCount + (ownCount == 1 ? " POST" : " POSTS") + "  ·  " +
                      Mathf.RoundToInt(Mathf.Clamp01(highCommand.FriendlyCohesion) * 100f) + "% EFFECTIVE";
            }
            else
            {
                cocTreeNote.text = enemyTotal == 0 ? "NO POSTS REPORTED"
                    : enemyKnown == 0 ? "NO CONFIRMED CONTACTS"
                    : enemyKnown + " OF " + enemyTotal + " CONFIRMED";
            }

            if (selected == null) cocSelectedId = -1;
            CommanderView open = selectedOnActiveSide ? selected : null;
            // The map rings the post whose file is open, so a reader can find the general
            // they are reading about. An empty file rings nothing.
            if (highCommand != null) highCommand.Highlight(open != null ? open.Id : -1);
            BindDossier(open);
        }

        /// <summary>Hang the log under the last post the tree filled.</summary>
        private void PlaceCocLog(int posts)
        {
            if (cocLogBlock == null) return;

            // Full-width master/detail flow: names and service records no longer compete
            // for two cramped columns. The selected file follows the roster, then the log.
            float detailY = cocRosterTop - posts * cocRowPitch - 12f;
            float width = cocBody.width - AvScreen.SpineInset;
            float x = cocBody.x + AvScreen.SpineInset;
            if (cocDossierBlock != null)
                AvKit.Place(cocDossierBlock, new Rect(x, detailY, width, cocDossierHeight));
            if (cocDossierEmpty != null)
                AvKit.Place(cocDossierEmpty, new Rect(x, detailY, width, CocEmptyDossierHeight));
            float y = detailY - cocDossierHeight - 16f;
            AvKit.Place(cocLogBlock,
                new Rect(x, y, width, CocLogBlock));

            // Keep the viewport aligned when the roster or selected file becomes shorter.
            if (cocScroll == null || cocRoot == null) return;
            float depth = cocBody.y - (y - CocLogBlock);
            float height = depth + 8f;
            if (Mathf.Abs(cocRoot.sizeDelta.y - height) > 0.5f)
            {
                cocRoot.sizeDelta = new Vector2(cocRoot.sizeDelta.x, height);
                // The spine is the page's own column mark; it runs the content it was given.
                if (cocSpine != null)
                    AvKit.Place(cocSpine.rectTransform, new Rect(0f, 0f, 3f, height));
            }
            Vector2 position = cocRoot.anchoredPosition;
            position.y = Mathf.Clamp(position.y, 0f, Mathf.Max(0f, height - cocScroll.viewport.rect.height));
            cocRoot.anchoredPosition = position;
            if (cocFocusFile && cocDossierBlock != null && cocDossierBlock.gameObject.activeSelf)
            {
                float travel = Mathf.Max(1f, height - cocScroll.viewport.rect.height);
                cocScroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(-detailY / travel);
                cocFocusFile = false;
            }
        }

        private void RefreshCocLog()
        {
            IReadOnlyList<CommanderLogLine> lines = cocShowHostile ? highCommand.HostileLog : highCommand.Log;
            int count = lines != null ? lines.Count : 0;
            int shown = 0;
            for (int i = 0; i < count && shown < CocLogRows; i++)
            {
                CommanderLogLine line = lines[i];
                if (line == null) continue;
                cocLogRows[shown].Bind(line, LogTargetOnVisibleSide(line.TargetId));
                shown++;
            }
            for (int i = shown; i < cocLogRows.Length; i++) cocLogRows[i].Hide();

            cocLogNote.text = count == 0 ? "NO TRAFFIC"
                : count + (count == 1 ? " ENTRY" : " ENTRIES");
        }

        /// <summary>
        /// A log line is clickable only when its subject is on the side the tree is
        /// showing, so selecting a line never clears the card.
        /// </summary>
        private bool LogTargetOnVisibleSide(int id)
        {
            if (id < 0 || highCommand == null) return false;
            IReadOnlyList<CommanderView> list = highCommand.Commanders;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id == id) return list[i].IsFriendly != cocShowHostile;
            return false;
        }

        private void BindCocSideToggles(string alliedCount, string hostileCount)
        {
            cocAlliedToggle.SetText("ALLIED  " + alliedCount);
            cocHostileToggle.SetText("HOSTILE  " + hostileCount);
            // Latched, never a solid plate: a switch is chrome, so it lights in the player's
            // accent, and which side is on is carried by the latched state plus the side's
            // own name, rail and file. The count in the label is the reading either way.
            cocAlliedToggle.SetLatched(!cocShowHostile);
            cocHostileToggle.SetLatched(cocShowHostile);
        }

        /// <summary>
        /// Put the selected commander's file on the page. The top of the card is the same for
        /// every post - form number, photo, name, office - and the record under it is laid out
        /// against the text it actually holds, with the card's frame following the record.
        /// </summary>
        private void BindDossier(CommanderView view)
        {
            if (cocName == null) return;

            if (view == null)
            {
                cocDossierBlock.gameObject.SetActive(false);
                if (cocDossierEmpty != null) cocDossierEmpty.gameObject.SetActive(true);
                cocDossierHeight = CocEmptyDossierHeight;
                PlaceCocLog(cocFilledRows);
                return;
            }

            cocDossierBlock.gameObject.SetActive(true);
            if (cocDossierEmpty != null) cocDossierEmpty.gameObject.SetActive(false);
            cocFileNo.text = view.IsFriendly ? "ALLIED PERSONNEL" : "INTELLIGENCE FILE";
            cocPhotoRef.text = view.IsFriendly ? "CONFIRMED IDENTITY" : view.IsKnown ? "IDENTITY CONFIRMED" : "IDENTITY UNCONFIRMED";
            cocName.text = view.Name;
            cocName.color = view.IsKia ? AvTheme.Disabled : AvTheme.TextPrimary;
            cocDossierPips.Bind(view.Tier, view.IsFriendly ? AvTheme.Accent : AvTheme.Warning);
            SetPortrait(view.Portrait, view.PortraitSeed);
            cocPortrait.color = view.IsKia || (!view.IsFriendly && !view.IsKnown)
                ? new Color(1f, 1f, 1f, 0.45f)
                : Color.white;
            cocDossierRail.color = StatusColor(view);

            PlaceCocRole(view);
            ResizeCocDossier(LayoutCocDossier(view));
            PlaceCocLog(cocFilledRows);
        }

        /// <summary>The office line, centred under the name with its tier pips as one group.</summary>
        private void PlaceCocRole(CommanderView view)
        {
            float identityX = CocDetailPad + CocPortraitWidth + 16f;
            float inner = cocDossierWidth - identityX - CocDetailPad;
            string role = view == null ? "" : view.Role;
            cocRole.text = role;
            float roleWidth = Mathf.Min(inner - 20f, Mathf.Ceil(cocRole.GetPreferredValues(role).x) + 2f);
            AvKit.Place(cocDossierPips.Rect, new Rect(identityX, -108f, 12f, 8f));
            AvKit.Place(cocRole.rectTransform, new Rect(identityX + 20f, -103f, roleWidth, 16f));
        }

        private static string Percent(CommanderView view) =>
            view == null ? "—" : Mathf.RoundToInt(Mathf.Clamp01(view.Weight) * 100f) + "%";


        /// <summary>The share of staff as one track with a fill, sized rather than filled.</summary>
        private float PlaceShareBar(float x, float y, float width, CommanderView view)
        {
            float top = y - 2f;
            AvKit.Place((RectTransform)cocShareTrack.transform, new Rect(x, top, width, 4f));

            bool visible = view != null && !view.IsKia && (view.IsFriendly || view.IsKnown);
            float fraction = visible ? Mathf.Clamp01(view.Weight) : 0f;
            cocShareBar.color = view != null && !view.IsFriendly ? AvTheme.Warning : AvTheme.Accent;
            AvKit.Place((RectTransform)cocShareBar.transform,
                        new Rect(x + 1f, top - 1f, Mathf.Max(0f, (width - 2f) * fraction), 2f));
            return top - 12f;
        }

        /// <summary>
        /// The bonus the commander carries, as file entries: the trait, then what it pays. The
        /// contract hands it over as one sentence joined by " · " (<c>CommandTraits.BonusLine</c>).
        /// </summary>
        private float BindBonus(string bonus, float x, float y, float width)
        {
            if (string.IsNullOrEmpty(bonus) || bonus == "NO STAFF BONUS")
            {
                for (int i = 1; i < cocBonusEntries.Length; i++) cocBonusEntries[i].Hide();
                return cocBonusEntries[0].Bind(x, y, width, "NO NOTABLE TRAITS", "", true);
            }

            int used = 0;
            int start = 0;
            float cursor = y;
            while (used < cocBonusEntries.Length)
            {
                int end = bonus.IndexOf(" · ", start, StringComparison.Ordinal);
                string entry = end < 0 ? bonus.Substring(start) : bonus.Substring(start, end - start);
                SplitBonus(entry, out string label, out string pay);
                bool last = end < 0 || used + 1 >= cocBonusEntries.Length;
                // A contract that carried more entries than the file has room for says so
                // rather than dropping the rest: a file that hides a clause is a false file.
                if (last && end >= 0)
                    pay = string.IsNullOrEmpty(pay) ? "…" : pay + "  ·  …";
                cursor -= cocBonusEntries[used].Bind(x, cursor, width, label, pay, last);
                used++;
                if (last) break;
                start = end + 3;
            }
            for (int i = used; i < cocBonusEntries.Length; i++) cocBonusEntries[i].Hide();
            return y - cursor;
        }

        /// <summary>
        /// The service record, or the bars the file seals it with when local intel has not
        /// confirmed the post. Returns the height the section used.
        /// </summary>
        private float BindRecord(float x, float y, float width, CommanderView view)
        {
            bool sealedFile = view == null || (!view.IsFriendly && !view.IsKnown);

            cocBio.gameObject.SetActive(!sealedFile);
            for (int i = 0; i < cocRedactions.Length; i++)
                cocRedactions[i].gameObject.SetActive(sealedFile);

            if (sealedFile)
            {
                float[] widths = { 1f, 0.86f, 0.62f };
                for (int i = 0; i < cocRedactions.Length; i++)
                    AvKit.Place((RectTransform)cocRedactions[i].transform,
                                new Rect(x, y - i * 13f, width * widths[i % widths.Length], 8f));
                cocEndNote.text = "FILE SEALED";
                return cocRedactions.Length * 13f;
            }

            string bio = view.Bio ?? "";
            cocBio.text = bio;
            float height = Mathf.Max(CocFormLine, Mathf.Ceil(cocBio.GetPreferredValues(bio, width, 0f).y) + 2f);
            AvKit.Place(cocBio.rectTransform, new Rect(x, y, width, height));
            cocEndNote.text = "END OF RECORD";
            return height;
        }

        /// <summary>Grow the card to the record it holds, frame and all.</summary>
        private void ResizeCocDossier(float height)
        {
            cocDossierHeight = Mathf.Max(340f, height);
            cocDossierBlock.sizeDelta = new Vector2(cocDossierWidth, cocDossierHeight);

            // The closing line belongs to the sheet, so it sits just above the card's bottom
            // edge however much record the file happens to carry.
            float inner = cocDossierWidth - CocDetailPad * 2f;
            AvKit.Place(cocEndNote.rectTransform,
                        new Rect(CocDetailPad, -(cocDossierHeight - 24f), inner, 12f));

            AvKit.Place((RectTransform)cocDossierFill.transform,
                        new Rect(0f, 0f, cocDossierWidth, cocDossierHeight));
            AvKit.Place((RectTransform)cocDossierBorder[0].transform, new Rect(0f, 0f, cocDossierWidth, 1f));
            AvKit.Place((RectTransform)cocDossierBorder[1].transform,
                        new Rect(0f, -cocDossierHeight + 1f, cocDossierWidth, 1f));
            AvKit.Place((RectTransform)cocDossierBorder[2].transform, new Rect(0f, 0f, 1f, cocDossierHeight));
            AvKit.Place((RectTransform)cocDossierBorder[3].transform,
                        new Rect(cocDossierWidth - 1f, 0f, 1f, cocDossierHeight));


            AvKit.Place((RectTransform)cocDossierRail.transform,
                        new Rect(5f, -10f, 3f, cocDossierHeight - 20f));
        }

        private static float CocSectionHeading(TMP_Text label, string text, float x, float y, float width)
        {
            label.text = text;
            AvKit.Place(label.rectTransform, new Rect(x, y, width, 12f));
            return y - 16f;
        }

        private static TMP_Text CocSectionLabel(RectTransform parent) =>
            AvStyled.Label(parent, new Rect(0f, 0f, 10f, 12f), "", "file-form");

        /// <summary>The faint rule a form draws between its clauses.</summary>
        private static Image CocClause(RectTransform parent) =>
            AvKit.Rule(parent, new Rect(0f, 0f, 0f, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));

        private static float CocClauseAt(Image rule, float x, float y, float width)
        {
            AvKit.Place((RectTransform)rule.transform, new Rect(x, y, width, 1f));
            return y - 10f;
        }


        /// <summary>
        /// Fill the gap between a key and its value with leader dots, the way a form does it.
        /// Measured against the row's own font, so the dots stop exactly at the value.
        /// </summary>
        private static string LeaderDots(float gap, TMP_Text template)
        {
            if (gap < 10f) return "";
            float dot = template.GetPreferredValues("··").x * 0.5f;
            if (dot < 0.5f) return "";
            int count = Mathf.FloorToInt((gap - 4f) / dot);
            return count < 2 ? "" : new string('·', Mathf.Min(count, 64));
        }

        /// <summary>Split "LOGISTICS MIND +15% STIPEND" into the trait and what it pays.</summary>
        private static void SplitBonus(string entry, out string label, out string pay)
        {
            label = entry;
            pay = "";
            for (int i = 1; i < entry.Length - 1; i++)
            {
                if (entry[i] != ' ' || (entry[i + 1] != '+' && entry[i + 1] != '-')) continue;
                label = entry.Substring(0, i);
                pay = entry.Substring(i + 1);
                return;
            }
        }


        private void SetPortrait(Sprite sprite, int seed)
        {
            if (cocPortrait == null || seed == cocPortraitSeed) return;
            cocPortraitSeed = seed;
            bool has = sprite != null;
            cocPortrait.enabled = has;
            cocPortrait.sprite = sprite;
            Vector2 well = cocPortraitWell != null ? cocPortraitWell.sizeDelta : Vector2.zero;
            FitPortrait(cocPortrait, well.x, well.y);
            if (cocPortraitFallback != null) cocPortraitFallback.gameObject.SetActive(!has);
        }

        /// <summary>
        /// Lay a portrait over its well the way a file crops a print: the sprite covers the well,
        /// centred, and the well's mask trims the overflow. The fit is computed here rather than
        /// left to <c>Image.preserveAspect</c> because that anchors the sprite by its own pivot:
        /// the portrait sprites do not share one, which is what made a column of plates uneven,
        /// and a sprite with a transparent margin (the card's portrait, here) showed it as a
        /// band down one side of the plate.
        /// </summary>
        private static void FitPortrait(Image image, float width, float height)
        {
            Rect area = new Rect(0f, 0f, width, height);
            Sprite sprite = image != null ? image.sprite : null;
            if (sprite != null && width > 0f && height > 0f && sprite.rect.height > 0.5f)
            {
                float aspect = sprite.rect.width / sprite.rect.height;
                area = aspect > width / height
                    ? new Rect((width - height * aspect) * 0.5f, 0f, height * aspect, height)
                    : new Rect(0f, (height - width / aspect) * 0.5f, width, width / aspect);
            }
            AvKit.Place(image.rectTransform, area);
        }

        private static string StatusOf(CommanderView view)
        {
            if (view.IsKia) return "KIA";
            if (!view.IsFriendly && !view.IsKnown) return "UNCONFIRMED";
            if (view.Alert) return "UNDER FIRE";
            if (view.InTransit) return "EN ROUTE";
            if (view.Disrupted) return "SUCCESSION";
            if (!view.IsFriendly && view.IntelAge >= 0f) return "SEEN " + Mathf.RoundToInt(view.IntelAge) + "S";
            return "ACTIVE";
        }

        /// <summary>The rail a post wears: the state colour, and nothing else.</summary>
        private static Color StatusColor(CommanderView view)
        {
            switch (StateOf(view))
            {
                case "danger": return AvTheme.RailDanger;
                case "info": return AvTheme.RailInfo;
                case "cooling": return AvTheme.RailCaution;
                case "ready": return AvTheme.RailReady;
                default: return AvTheme.RailInert;
            }
        }

        private static string StateOf(CommanderView view)
        {
            if (view.IsKia) return "locked";
            if (!view.IsFriendly && !view.IsKnown) return "locked";
            if (view.Alert) return "danger";
            if (view.InTransit) return "info";
            if (view.Disrupted) return "cooling";
            return view.IsFriendly ? "ready" : "hostile";
        }

        private static string ChipStateOf(CommanderView view)
        {
            if (view.IsKia) return "danger";
            if (!view.IsFriendly && !view.IsKnown) return "inert";
            if (view.Alert) return "danger";
            if (view.Disrupted) return "warn";
            if (view.InTransit) return "info";
            return view.IsFriendly ? "live" : "warn";
        }

        /// <summary>Rail and text colour for a log tone; the mapping lives with the console.</summary>
        private static Color LogColor(CommanderLogTone tone)
        {
            switch (tone)
            {
                case CommanderLogTone.Economy: return AvTheme.RailReady;
                case CommanderLogTone.Order: return AvTheme.RailInfo;
                case CommanderLogTone.Contact: return AvTheme.RailCaution;
                case CommanderLogTone.Loss: return AvTheme.RailDanger;
                case CommanderLogTone.Alert: return AvTheme.Alert;
                default: return AvTheme.Dim;
            }
        }

        /// <summary>Tier insignia: one bar per level of command, all three at the top.</summary>
        private sealed class CocPips
        {
            private readonly Image[] bars = new Image[3];

            /// <summary>The pip strip's own rect, so a row can lay it out per tier.</summary>
            public RectTransform Rect { get; }

            public CocPips(RectTransform parent, float x, float y, Color color)
            {
                var root = new GameObject("CocPips", typeof(RectTransform));
                Rect = (RectTransform)root.transform;
                Rect.SetParent(parent, false);
                AvKit.Place(Rect, new Rect(x, y, 12f, 8f));

                for (int i = 0; i < bars.Length; i++)
                    bars[i] = AvKit.Rule(Rect, new Rect(i * 4f, -2f, 2f, 8f), color);
            }

            public void Bind(int tier, Color color)
            {
                int lit = Mathf.Clamp(3 - tier, 1, 3);
                for (int i = 0; i < bars.Length; i++)
                    bars[i].color = i < lit ? color : AvTheme.RailInert;
            }
        }

        /// <summary>
        /// One field of the file: a key, the leader dots that carry the eye across, and the
        /// value. Written as a form, so a value that needs two lines gets two lines.
        /// </summary>
        private sealed class CocFormRow
        {
            private readonly TMP_Text key, leader, value;

            public CocFormRow(RectTransform parent)
            {
                key = AvStyled.Label(parent, new Rect(0f, 0f, 10f, CocFormLine), "", "form-key");
                leader = AvStyled.Label(parent, new Rect(0f, 0f, 10f, CocFormLine), "", "leader");
                value = AvStyled.Label(parent, new Rect(0f, 0f, 10f, CocFormLine), "", "form-value");
                value.enableWordWrapping = true;
                // A value is never traded for an ellipsis: it wraps to the room the field
                // gives it, shrinks to the micro floor, and only overflows after that.
                value.overflowMode = TextOverflowModes.Overflow;
                value.enableAutoSizing = true;
                value.fontSizeMin = AvTokens.FontMicro;
                value.fontSizeMax = value.fontSize;
            }

            /// <summary>Lay the field at y; returns the y the next field should use.</summary>
            public float Bind(float x, float y, float width, string keyText, string valueText, Color color)
            {
                key.text = keyText ?? "";
                // The key may take a little over half the field: the cap exists to leave the
                // value room, but at 50% "STAFF SHARE" itself was ellipsised.
                float keyWidth = Mathf.Min(Mathf.Ceil(key.GetPreferredValues(key.text).x) + 6f, width * 0.62f);
                AvKit.Place(key.rectTransform, new Rect(x, y, keyWidth, CocFormLine));

                value.text = string.IsNullOrEmpty(valueText) ? "—" : valueText;
                value.color = color;
                float valueWidth = Mathf.Max(40f, width - keyWidth - 6f);
                float height = Mathf.Clamp(Mathf.Ceil(value.GetPreferredValues(value.text, valueWidth, 0f).y),
                                           CocFormLine, CocFormLine * 2f);
                AvKit.Place(value.rectTransform, new Rect(x + width - valueWidth, y, valueWidth, height));

                float gap = width - keyWidth - valueWidth - 4f;
                leader.text = LeaderDots(gap, leader);
                AvKit.Place(leader.rectTransform, new Rect(x + keyWidth, y, Mathf.Max(0f, gap), CocFormLine));
                return y - height - 2f;
            }
        }

        /// <summary>
        /// One entry of the bonus the commander carries: the trait, then what it pays, with the
        /// clause rule a form draws under each entry. Sized to the effect's own copy.
        /// </summary>
        private sealed class CocBonusEntry
        {
            private readonly GameObject root;
            private readonly TMP_Text name, pay;
            private readonly Image rule;

            public CocBonusEntry(RectTransform parent)
            {
                root = new GameObject("CocBonusEntry", typeof(RectTransform));
                var rect = root.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(0f, 0f, 10f, CocBonusEntryHeight));

                name = AvStyled.Label(rect, new Rect(0f, 0f, 10f, 12f), "", "form-key");
                pay = AvStyled.Label(rect, new Rect(0f, 0f, 10f, 13f), "", "form-value");
                // What the trait pays is data: wrap to the entry's room, shrink to the
                // micro floor, then overflow rather than ending in an ellipsis.
                pay.enableWordWrapping = true;
                pay.overflowMode = TextOverflowModes.Overflow;
                pay.enableAutoSizing = true;
                pay.fontSizeMin = AvTokens.FontMicro;
                pay.fontSizeMax = pay.fontSize;
                rule = CocClause(rect);
                rule.color = AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.09f));
                root.SetActive(false);
            }

            /// <summary>Lay the entry at y; returns the height it used.</summary>
            public float Bind(float x, float y, float width, string label, string effect, bool last)
            {
                if (!root.activeSelf) root.SetActive(true);

                name.text = label ?? "";
                AvKit.Place(name.rectTransform, new Rect(x, y, width, 12f));

                bool hasPay = !string.IsNullOrEmpty(effect);
                pay.text = effect ?? "";
                float payHeight = hasPay
                    ? Mathf.Clamp(Mathf.Ceil(pay.GetPreferredValues(pay.text, width, 0f).y), 13f, 26f)
                    : 0f;
                pay.color = hasPay && pay.text[0] == '-' ? AvTheme.RailCaution : AvTheme.TextPrimary;
                AvKit.Place(pay.rectTransform, new Rect(x, y - 12f, width, Mathf.Max(1f, payHeight)));

                float height = hasPay ? 14f + payHeight : 12f;
                rule.gameObject.SetActive(!last);
                if (!last) AvKit.Place((RectTransform)rule.transform, new Rect(x, y - height, width, 1f));
                return height + 2f;
            }

            public void Hide()
            {
                if (root.activeSelf) root.SetActive(false);
            }
        }

        /// <summary>
        /// One staff-log line: a tone dot, the age, and the sentence. Clicking a line opens
        /// the subject post's card when the event has one.
        /// </summary>
        private sealed class CocLogRow
        {
            private const float AgeWidth = 34f;

            private readonly GameObject root;
            private readonly MfdGlyph dot;
            private readonly TMP_Text age, text;
            private readonly AvButton hit;
            private int id = -1;

            public CocLogRow(RectTransform parent, float x, float y, float width, Action<int> select)
            {
                root = new GameObject("CocLogRow", typeof(RectTransform));
                var rect = root.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, CocLogPitch - 2f));

                var dotObject = new GameObject("Tone", typeof(RectTransform), typeof(MfdGlyph));
                var dotRect = (RectTransform)dotObject.transform;
                dotRect.SetParent(rect, false);
                dot = dotObject.GetComponent<MfdGlyph>();
                dot.raycastTarget = false;
                AvKit.Place(dotRect, new Rect(2f, -(CocLogPitch - 2f - 6f) * 0.5f, 6f, 6f));
                dot.SetKind("dot", AvTheme.Dim);

                // The age column is a stamp of when, not a right-aligned figure: right-aligned
                // it ended flush against the sentence and read as "1mSTIPEND PAID".
                age = AvStyled.Label(rect, new Rect(12f, 0f, AgeWidth, CocLogPitch - 2f), "", "metric-cap",
                                     align: TextAlignmentOptions.Left);
                text = AvStyled.Label(rect,
                    new Rect(12f + AgeWidth + 4f, 0f, width - AgeWidth - 20f, CocLogPitch - 2f), "", "row-sub");
                text.fontSize = AvTokens.FontSmall;
                // The sentence wraps to its row and shrinks to the micro floor; only a
                // log line that even two lines cannot hold ends in an ellipsis, which
                // states the omission instead of cutting it silently.
                text.enableWordWrapping = true;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.enableAutoSizing = true;
                text.fontSizeMin = AvTokens.FontMicro;
                text.fontSizeMax = text.fontSize;

                // Built once and reading the row's own id, so a refresh does not hand the button
                // a fresh closure four times a second.
                hit = AvKit.HitButton(rect, new Rect(0f, 0f, width, CocLogPitch - 2f), () =>
                {
                    if (id >= 0) select(id);
                });
                hit.SetEnabled(false);
                root.SetActive(false);
            }

            public void Bind(CommanderLogLine line, bool clickable)
            {
                id = line.TargetId;
                Color tone = LogColor(line.Tone);
                dot.SetKind("dot", tone);
                age.text = TheaterReadout.Age(line.Age);
                text.text = line.Text;
                text.color = tone;

                // The action is the constructor's; a refresh only says whether it may fire.
                // An inert row still explains itself, so the pointer is never left reading
                // nothing over a line that looks live.
                hit.SetEnabled(clickable);
                hit.WithTooltip(clickable ? "Open this post's card." : "Post is not on this side.");
                if (!root.activeSelf) root.SetActive(true);
            }

            public void Hide()
            {
                id = -1;
                if (root.activeSelf) root.SetActive(false);
            }
        }

        /// <summary>
        /// One post, read as a row of the tree: a state rail, the commander's portrait, tier
        /// insignia, name and office, what the post is worth to the faction, the disposition,
        /// and a track for the post's share of the staff. A post below the theater commander
        /// is indented under a trunk that runs from the post it reports to.
        /// </summary>
        private sealed class CommanderRow
        {
            private const float PortraitWidth = 26f;
            private const float PortraitHeight = 32f;
            private const float TierStep = 12f;

            /// <summary>The row height every offset in this row is written against.</summary>
            private const float DesignHeight = 44f;

            private const float PulseSeconds = 0.6f;

            private readonly GameObject root;
            private readonly Image background, rail, guide, tick, weight, portrait;
            private readonly RectTransform portraitRoot, portraitWell;
            private readonly GameObject portraitGlyph;
            private readonly TMP_Text rank, name, role, status;
            private readonly CocPips pips;
            private readonly AvButton hit;
            private readonly float width, height, pitch, portraitHeight;
            private int id = -1;
            private int portraitSeed = int.MinValue;
            private bool portraitDim;
            private string lastState;
            private float pulseUntil;

            public CommanderRow(
                RectTransform parent, float x, float y, float width,
                float height, float pitch, Action<int> select)
            {
                this.width = width;
                this.height = height;
                this.pitch = pitch;
                portraitHeight = Mathf.Min(PortraitHeight, height - 6f);

                root = new GameObject("CommanderRow", typeof(RectTransform));
                var rect = root.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, height));

                background = AvKit.Panel(rect, new Rect(0f, 0f, width, height), Color.clear);
                rail = AvStyled.Rail(rect, new Rect(0f, -3f, 3f, height - 8f), "locked");
                guide = AvKit.Rule(rect, new Rect(0f, 0f, 1f, 1f), AvTheme.Hairline);
                tick = AvKit.Rule(rect, new Rect(0f, 0f, TierStep, 1f), AvTheme.Hairline);

                // The plate is one group, indented as a whole by the tier. Its parts used to be
                // placed separately, so a post below the theater commander left its frame behind
                // at the column edge while the photo moved under the indent.
                var plateObject = new GameObject("Portrait", typeof(RectTransform));
                portraitRoot = (RectTransform)plateObject.transform;
                portraitRoot.SetParent(rect, false);
                AvKit.Place(portraitRoot, new Rect(0f, 0f, PortraitWidth, portraitHeight));
                AvKit.Panel(portraitRoot, new Rect(0f, 0f, PortraitWidth, portraitHeight),
                            AvTheme.SurfaceInert);
                AvKit.Outline(portraitRoot, new Rect(0f, 0f, PortraitWidth, portraitHeight),
                              AvTheme.Frame.WithAlpha(0.6f));

                var wellObject = new GameObject("Well", typeof(RectTransform), typeof(RectMask2D));
                portraitWell = (RectTransform)wellObject.transform;
                portraitWell.SetParent(portraitRoot, false);
                AvKit.Place(portraitWell,
                            new Rect(1f, -1f, PortraitWidth - 2f, portraitHeight - 2f));

                portrait = AvKit.Panel(portraitWell,
                    new Rect(0f, 0f, PortraitWidth - 2f, portraitHeight - 2f), Color.white);
                portrait.type = Image.Type.Simple;
                portrait.preserveAspect = false;
                portrait.raycastTarget = false;
                portrait.enabled = false;

                // No portrait is a Wing Command gap, not a blank: the row's own person glyph
                // holds the frame so the row keeps its shape.
                var glyphObject = new GameObject("PortraitGlyph", typeof(RectTransform), typeof(MfdGlyph));
                var glyphRect = (RectTransform)glyphObject.transform;
                glyphRect.SetParent(portraitWell, false);
                portraitGlyph = glyphObject;
                MfdGlyph glyph = glyphObject.GetComponent<MfdGlyph>();
                glyph.raycastTarget = false;
                glyph.SetKind("person", AvTheme.Dim);
                AvKit.Place(glyphRect, new Rect((PortraitWidth - 2f - 12f) * 0.5f,
                                                -(portraitHeight - 2f - 12f) * 0.5f, 12f, 12f));
                glyphObject.SetActive(false);

                const float trail = 72f;
                float nameWidth = width - 34f - trail - 8f;
                pips = new CocPips(rect, 0f, 0f, AvTheme.Dim);
                rank = AvStyled.Label(rect, new Rect(0f, -3f, 52f, 12f), "", "section-title");
                rank.fontSize = AvTokens.FontMicro;
                name = AvStyled.Label(rect, new Rect(0f, -3f, nameWidth, 14f), "", "row-name");
                role = AvStyled.Label(rect, new Rect(0f, -20f, nameWidth, 12f), "", "section-title-note");
                role.fontSize = AvTokens.FontMicro;
                status = AvStyled.Label(rect, new Rect(width - trail, -3f, trail, 13f), "", "metric-cap");
                status.fontSize = AvTokens.FontMicro;
                // Built where it belongs: the fill this returns is the only part of a progress
                // bar that can be moved afterwards, so re-placing it in Bind left the track's
                // outline floating on the row's header line.
                weight = AvKit.ProgressBar(rect, new Rect(width - trail, -18f, trail, 3f), 0f,
                                           AvTheme.RailInert);

                Divider(rect, 0f, -(height - 2f), width);

                hit = AvKit.HitButton(rect, new Rect(0f, 0f, width, height), () =>
                {
                    if (id >= 0) select(id);
                });
                hit.SetRowHighlight(background, Color.clear, CocHover);
                root.SetActive(false);
            }

            public void Bind(CommanderView view, bool selected)
            {
                id = view.Id;
                float indent = view.Tier * TierStep;
                const float trail = 72f;
                const float nameX = 34f;
                float textX = indent + nameX;
                float textWidth = Mathf.Max(40f, width - textX - trail - 8f);

                // Rows take the pitch the page could afford, so the row is centred in
                // whatever height it was given rather than pinned to a design size.
                float top = -(height - DesignHeight) * 0.5f;
                AvKit.Place(portraitRoot, new Rect(indent + 3f, top - 1f, PortraitWidth, portraitHeight));
                AvKit.Place(pips.Rect, new Rect(textX, top - 17f, 12f, 12f));
                AvKit.Place(rank.rectTransform, new Rect(textX + 15f, top - 17f, 54f, 12f));
                AvKit.Place(name.rectTransform, new Rect(textX, top - 1f, textWidth, 14f));
                // The office line runs the full width under the state: it is *below* the status
                // and the share track, so nothing is beside it and nothing needs to be reserved.
                AvKit.Place(role.rectTransform, new Rect(textX, top - 30f, width - textX - 8f, 12f));

                if (view.PortraitSeed != portraitSeed)
                {
                    portraitSeed = view.PortraitSeed;
                    bool has = view.Portrait != null;
                    portrait.enabled = has;
                    portrait.sprite = view.Portrait;
                    FitPortrait(portrait, PortraitWidth - 2f, portraitHeight - 2f);
                    portraitGlyph.SetActive(!has);
                }

                bool dimPortrait = view.IsKia || (!view.IsFriendly && !view.IsKnown);
                portrait.color = dimPortrait ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                if (dimPortrait != portraitDim)
                {
                    portraitDim = dimPortrait;
                    portraitGlyph.GetComponent<MfdGlyph>().SetKind("person",
                        dimPortrait ? AvTheme.Disabled.WithAlpha(0.45f) : AvTheme.Dim);
                }

                Color state = StatusColor(view);
                pips.Bind(view.Tier, view.IsFriendly ? AvTheme.Accent : AvTheme.Warning);
                rank.text = view.Rank;
                rank.color = AvTheme.Dim;
                name.text = view.IsKia ? view.Name + "  [KIA]" : view.Name;
                name.color = selected ? AvTheme.Accent
                    : view.IsKia ? AvTheme.Disabled
                    : !view.IsFriendly && !view.IsKnown ? AvTheme.Disabled
                    : view.IsFriendly ? AvTheme.TextPrimary
                    : AvTheme.Warning;

                // The office. What the post is worth is the card's line, not the row's: at this
                // width role and bonus together always ended in an ellipsis, so the row stated
                // neither. The bonus still rides the row's tooltip.
                role.text = view.Role;

                status.text = StatusOf(view);
                status.color = selected ? AvTheme.Accent
                    : view.IsKia || (!view.IsFriendly && !view.IsKnown) ? AvTheme.Disabled
                    : state;

                weight.fillAmount = Mathf.Clamp01(view.Weight);
                weight.color = view.IsKia || (!view.IsFriendly && !view.IsKnown) ? AvTheme.RailInert
                    : view.IsFriendly ? AvTheme.Accent
                    : AvTheme.Warning;

                Color rest = selected
                    ? AvTheme.Unity(AvTokens.RowFill(AvTheme.Accent.ToRgba(), true))
                    : Color.clear;
                hit.SetRowHighlight(background, rest, CocHover);

                // A row that changed since the last refresh carries a short flash: the page
                // shows that something happened even when the reading itself is easy to miss.
                string stateKey = StateOf(view) + (view.Alert ? "!" : "");
                if (lastState != null && lastState != stateKey) pulseUntil = Time.unscaledTime + PulseSeconds;
                lastState = stateKey;
                if (Time.unscaledTime < pulseUntil)
                {
                    float strength = (pulseUntil - Time.unscaledTime) / PulseSeconds;
                    background.color = Color.Lerp(rest, Color.white, 0.16f * strength);
                }

                rail.color = selected ? AvTheme.Accent : state;

                // The trunk: one pitch-long segment per post at the parent's indent, tiling with
                // the post above into a single line down the branch, with the elbow marking
                // which row it belongs to. A level's line ends where that level's posts end.
                bool branch = view.Tier > 0;
                guide.gameObject.SetActive(branch);
                tick.gameObject.SetActive(branch);
                if (branch)
                {
                    float gx = indent - TierStep + 3f;
                    AvKit.Place(guide.rectTransform, new Rect(gx, pitch - height, 1f, pitch));
                    AvKit.Place(tick.rectTransform, new Rect(gx, top - 23f, TierStep, 1f));
                }

                // The row's copy is the office; the bonus its post carries rides the tooltip,
                // where there is room for the whole sentence.
                string bonus = !view.IsFriendly && !view.IsKnown ? "" : FirstBonus(view.Bonus);
                hit.WithTooltip((view.IsFriendly
                    ? "Open the card for " + view.Name + "  ·  " + view.Role
                    : view.IsKnown
                        ? "Confirmed contact: " + view.Name + "  ·  " + view.Role
                        : "Unconfirmed post: " + view.Name + " — no local intel.") +
                    (string.IsNullOrEmpty(bonus) ? "" : "  ·  " + bonus) +
                    (selected ? "  ·  Bracketed on the map." : ""));
                if (!root.activeSelf) root.SetActive(true);
            }

            /// <summary>The first entry of the bonus line, for the row's second line.</summary>
            private static string FirstBonus(string bonus)
            {
                if (string.IsNullOrEmpty(bonus)) return "";
                int at = bonus.IndexOf(" · ", StringComparison.Ordinal);
                return at < 0 ? bonus : bonus.Substring(0, at);
            }

            public void Hide()
            {
                id = -1;
                if (root.activeSelf) root.SetActive(false);
            }
        }
    }
}
