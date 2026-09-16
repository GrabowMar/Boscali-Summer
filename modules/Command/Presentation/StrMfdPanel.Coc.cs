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
    /// <para>The chain of command is the left column, the selected commander's card the right,
    /// and the staff log under the tree. Enemy posts are listed by identity; until local intel
    /// confirms one, its portrait and rail stay inert and its card reads unconfirmed.</para>
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        /// <summary>
        /// Rows the tree can hold. HighCommand's wire ceiling is eight posts per faction;
        /// six are fielded today and the pitch is chosen so six fill the column.
        /// </summary>
        private const int CocRosterRows = 8;

        /// <summary>Posts a faction fields. Used only to size the row pitch to the page.</summary>
        private const float CocFieldedPosts = 6f;

        /// <summary>The title line and the reading under it, down to the first row of the tree.</summary>
        private const float CocHeaderHeight = 36f;

        /// <summary>The tree column, and the gap to the commander card.</summary>
        private const float CocTreeWidth = 258f;
        private const float CocColumnGap = 10f;

        private const float CocRowGap = 5f;
        private const float CocRowMinHeight = 44f;
        private const float CocRowMaxHeight = 60f;

        /// <summary>
        /// The commander dossier: a personnel file laid out as a form - the form number, a
        /// photo with its reference, the identity, then the record itself with leader dots,
        /// a disposition stamp, the share of staff, the bonus the commander carries and the
        /// service record. Nothing to press, so it reads.
        /// </summary>
        private const float CocDetailHeight = 400f;
        private const float CocPortraitWidth = 84f;
        private const float CocPortraitHeight = 104f;
        private const int CocBonusEntries = 3;
        private const int CocRedactionBars = 3;
        private const float CocDetailPad = 10f;

        /// <summary>Where the record's own fields begin, under the fixed identity block.</summary>
        private const float CocFileFieldsTop = 198f;
        private const float CocFormLine = 14f;
        private const float CocBonusEntryHeight = 27f;
        private const float CocStampHeight = 22f;

        private const int CocRuleFields = 0;
        private const int CocRuleDisposition = 1;
        private const int CocRuleShare = 2;
        private const int CocRuleBonus = 3;
        private const int CocRuleRecord = 4;

        /// <summary>The staff log follows the last post the tree filled, in the left column.</summary>
        private const int CocLogRows = 4;
        private const float CocLogPitch = 13f;
        private const float CocLogHeader = 18f;
        private const float CocLogGap = 6f;
        private const float CocLogBlock = CocLogHeader + CocLogRows * CocLogPitch + CocLogGap;

        private readonly CommanderRow[] cocRows = new CommanderRow[CocRosterRows];
        private readonly CommanderView[] cocVisible = new CommanderView[CocRosterRows];
        private readonly int[] cocIds = new int[CocRosterRows];
        private readonly int[] cocParents = new int[CocRosterRows];
        private readonly int[] cocOrder = new int[CocRosterRows];
        private readonly bool[] cocOrdered = new bool[CocRosterRows];
        private readonly CocLogRow[] cocLogRows = new CocLogRow[CocLogRows];
        private readonly CocBonusEntry[] cocBonusEntries = new CocBonusEntry[CocBonusEntries];
        private readonly Image[] cocRedactions = new Image[CocRedactionBars];
        private RectTransform cocRoot, cocLogBlock, cocDossierBlock, cocStampRoot;
        private Rect cocBody;
        private bool cocScrolled;
        private float cocRosterTop, cocRowPitch, cocRowHeight, cocDossierWidth, cocDossierHeight;
        private int cocFilledRows;
        private TMP_Text cocTreeNote, cocLogNote;
        private TMP_Text cocFileNo, cocPhotoRef, cocName, cocRole, cocBio, cocEndNote;
        private TMP_Text cocDispositionNote, cocBonusNote, cocRecordNote;
        private TMP_Text cocDossierStamp;
        private Image cocDossierFill, cocDossierRail, cocShareTrack, cocShareBar;
        private Image[] cocDossierBorder, cocDossierTicks, cocRules, cocStampBorder;
        private Image cocStampFill;
        private CocFormRow cocRankRow, cocStationRow, cocShareRow;
        private Image cocPortrait;
        private TMP_Text cocPortraitFallback;
        private CocPips cocDossierPips;
        private AvButton cocAlliedToggle, cocHostileToggle;
        private bool cocShowHostile;
        private int cocSelectedId = -1;
        private int cocPortraitSeed = int.MinValue;

        private static readonly Color CocPortraitBack = new Color32(10, 18, 13, 255);
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
            cocRoot = cocLogBlock = cocDossierBlock = cocStampRoot = null;
            cocBody = default(Rect);
            cocScrolled = false;
            cocRosterTop = cocRowPitch = cocRowHeight = 0f;
            cocDossierWidth = cocDossierHeight = 0f;
            cocFilledRows = 0;
            cocTreeNote = cocLogNote = null;
            cocFileNo = cocPhotoRef = cocName = cocRole = cocBio = cocEndNote = null;
            cocDispositionNote = cocBonusNote = cocRecordNote = null;
            cocDossierStamp = null;
            cocDossierFill = cocDossierRail = cocShareTrack = cocShareBar = null;
            cocDossierBorder = cocDossierTicks = cocRules = cocStampBorder = null;
            cocStampFill = null;
            cocRankRow = cocStationRow = cocShareRow = null;
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
            float usable = view.width - AvScreen.SpineInset;
            float detailWidth = Mathf.Max(140f, usable - CocTreeWidth - CocColumnGap);
            float detailX = view.x + AvScreen.SpineInset + CocTreeWidth + CocColumnGap;

            // Six posts are fielded. Sizing the rows to the page rather than the page to the
            // rows is what stops a six-post staff leaving a hole above the status strip; a
            // roster longer than the page scrolls instead.
            float slack = view.height - (CocHeaderHeight + CocLogBlock + 8f);
            cocRowPitch = Mathf.Clamp(slack / CocFieldedPosts, CocRowMinHeight + CocRowGap,
                                      CocRowMaxHeight + CocRowGap);
            cocRowHeight = cocRowPitch - CocRowGap;

            float rosterHeight = CocHeaderHeight + CocRosterRows * cocRowPitch + CocLogBlock + 8f;
            Rect body;
            cocRoot = AvScreen.Scroll((RectTransform)page.transform, view,
                                      Mathf.Max(rosterHeight, CocDetailHeight + 8f), out body);
            cocScrolled = !ReferenceEquals(cocRoot, (RectTransform)page.transform);
            cocBody = body;

            float x = body.x + AvScreen.SpineInset;
            float headerY = body.y;
            AvStyled.Spine(cocRoot, new Rect(body.x, body.y, 3f, body.height));

            // The side switch rides the header line rather than a row of its own: the two
            // staffs are a property of the board, not a separate instrument.
            float toggleWidth = 74f;
            cocHostileToggle = AvStyled.Button(
                cocRoot, new Rect(x + usable - toggleWidth, headerY + 4f, toggleWidth, 18f),
                "HOSTILE  —", "btn",
                () => { cocShowHostile = true; nextRefresh = 0f; },
                AvButtonStyle.Toggle)
                .WithTooltip("Show the opposing chain of command. A post stays unconfirmed until local intel has seen it.");
            cocAlliedToggle = AvStyled.Button(
                cocRoot,
                new Rect(x + usable - toggleWidth * 2f - 4f, headerY + 4f, toggleWidth, 18f),
                "ALLIED  —", "btn",
                () => { cocShowHostile = false; nextRefresh = 0f; },
                AvButtonStyle.Toggle)
                .WithTooltip("Show the allied chain of command.");
            cocAlliedToggle.SetLatched(true);

            SectionHeader(cocRoot, x, headerY, usable - toggleWidth * 2f - 12f, "CHAIN OF COMMAND", null,
                          band: false);
            // The reading gets its own line rather than the half-header beside the title: a
            // tally that gets ellipsised is a tally the panel did not give.
            cocTreeNote = AvStyled.Label(
                cocRoot, new Rect(x, headerY - 20f, usable, 13f), "", "section-title-note");

            float y = headerY - CocHeaderHeight;
            cocRosterTop = y;
            for (int i = 0; i < CocRosterRows; i++)
            {
                cocRows[i] = new CommanderRow(
                    cocRoot, x, y - i * cocRowPitch, CocTreeWidth, cocRowHeight, cocRowPitch, SelectCoc);
            }

            BuildCocLogBlock(x, y - CocRosterRows * cocRowPitch, CocTreeWidth);
            BuildCocDossierBlock(detailX, headerY, detailWidth);
        }

        /// <summary>The staff log: the sentences the status strip shows, kept and dated.</summary>
        private void BuildCocLogBlock(float x, float y, float width)
        {
            var root = new GameObject("CocLogBlock", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(cocRoot, false);
            AvKit.Place(rect, new Rect(x, y, width, CocLogBlock));
            cocLogBlock = rect;

            cocLogNote = CocSection(rect, 0f, 0f, width, "STAFF LOG", "NO TRAFFIC");
            for (int i = 0; i < cocLogRows.Length; i++)
                cocLogRows[i] = new CocLogRow(rect, 0f, -CocLogHeader - i * CocLogPitch, width);
        }

        /// <summary>A staff section title with a live right-hand count, tied to the spine.</summary>
        private static TMP_Text CocSection(
            RectTransform parent, float x, float y, float width, string title, string note)
        {
            AvStyled.SpineTick(parent, x - AvScreen.SpineInset + 3f, y - 7f);
            AvStyled.Label(parent, new Rect(x, y, width * 0.55f, 14f), title, "section-title");
            return AvStyled.Label(parent, new Rect(x + width * 0.55f, y, width * 0.45f, 14f),
                note, "metric-cap");
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
            cocDossierTicks = CocTicks(rect, card, AvTheme.Hairline);
            cocDossierRail = AvStyled.Rail(rect, new Rect(5f, -10f, 3f, CocDetailHeight - 20f), "locked");

            // ---- the form's own header ---------------------------------------------------
            AvStyled.Label(rect, new Rect(CocDetailPad, -10f, inner * 0.6f, 12f), "PERSONNEL FILE",
                           "file-form");
            cocFileNo = AvStyled.Label(rect, new Rect(CocDetailPad, -10f, inner, 12f), "", "file-meta",
                align: TextAlignmentOptions.MidlineRight);
            Divider(rect, CocDetailPad, -24f, inner);

            // ---- photo and identity -------------------------------------------------------
            float portraitX = CocDetailPad + (inner - CocPortraitWidth) * 0.5f;
            Rect frame = new Rect(portraitX, -30f, CocPortraitWidth, CocPortraitHeight);
            AvKit.Panel(rect, frame, CocPortraitBack);
            AvKit.Outline(rect, frame, AvTheme.Frame);
            AvKit.CornerTicks(rect, frame, AvTheme.Hairline.WithAlpha(0.5f));

            cocPortraitFallback = AvStyled.Label(rect,
                new Rect(frame.x + 2f, frame.y - 34f, frame.width - 4f, 32f), "NO\nVISUAL", "row-sub",
                align: TextAlignmentOptions.Center);
            cocPortrait = AvKit.Panel(rect,
                new Rect(frame.x + 1f, frame.y - 1f, frame.width - 2f, frame.height - 2f), Color.white);
            cocPortrait.type = Image.Type.Simple;
            cocPortrait.preserveAspect = true;
            cocPortrait.raycastTarget = false;
            cocPortrait.enabled = false;

            cocPhotoRef = AvStyled.Label(rect, new Rect(CocDetailPad, -136f, inner, 12f), "", "photo-cap",
                align: TextAlignmentOptions.Center);
            cocName = AvStyled.Label(rect, new Rect(CocDetailPad, -152f, inner, 18f), "", "file-title",
                align: TextAlignmentOptions.Center);
            cocName.fontSize = 15f;
            cocRole = AvStyled.Label(rect, new Rect(CocDetailPad, -172f, inner, 13f), "", "section-title-note");
            cocDossierPips = new CocPips(rect, 0f, 0f, AvTheme.Accent);

            // ---- the record below: laid out on every refresh ------------------------------
            cocRankRow = new CocFormRow(rect);
            cocStationRow = new CocFormRow(rect);
            cocShareRow = new CocFormRow(rect);
            cocShareTrack = AvStyled.Box(rect, new Rect(0f, 0f, 0f, 4f), "bar");
            cocShareBar = AvKit.Panel(rect, new Rect(0f, 0f, 0f, 2f), AvTheme.Accent);

            cocDispositionNote = CocSectionLabel(rect);
            cocBonusNote = CocSectionLabel(rect);
            cocRecordNote = CocSectionLabel(rect);

            // A stamp is ink on the file, not a control: built once, re-inked per state.
            var stamp = new GameObject("CocStamp", typeof(RectTransform));
            cocStampRoot = (RectTransform)stamp.transform;
            cocStampRoot.SetParent(rect, false);
            cocStampRoot.localRotation = Quaternion.Euler(0f, 0f, -3.5f);
            cocStampFill = AvKit.Panel(cocStampRoot, new Rect(0f, 0f, inner, CocStampHeight), Color.clear,
                                       AvSprites.Control);
            cocStampBorder = AvKit.Outline(cocStampRoot, new Rect(0f, 0f, inner, CocStampHeight), AvTheme.Frame);
            cocDossierStamp = AvStyled.Label(cocStampRoot, new Rect(0f, 0f, inner, CocStampHeight), "",
                "stamp", align: TextAlignmentOptions.Center);

            for (int i = 0; i < cocBonusEntries.Length; i++) cocBonusEntries[i] = new CocBonusEntry(rect);
            for (int i = 0; i < cocRedactions.Length; i++)
                cocRedactions[i] = AvStyled.Box(rect, new Rect(0f, 0f, 10f, 8f), "redact");

            cocBio = AvStyled.Label(rect, new Rect(0f, 0f, 10f, CocFormLine), "", "row-sub");
            cocEndNote = AvStyled.Label(rect, new Rect(0f, 0f, 10f, 12f), "END OF RECORD", "file-meta",
                align: TextAlignmentOptions.Center);

            cocRules = new Image[5];
            for (int i = 0; i < cocRules.Length; i++) cocRules[i] = CocClause(rect);
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
            y = cocStationRow.Bind(x, y, inner, "STATION", view?.Location, value);
            y = CocClauseAt(cocRules[CocRuleFields], x, y - 8f, inner);

            y = CocSectionHeading(cocDispositionNote, "DISPOSITION", x, y - 4f, inner);
            y = PlaceStamp(x, y, inner, view);
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
            AvKit.Place(cocEndNote.rectTransform, new Rect(x, y - 14f, inner, 12f));
            return -y + CocDetailPad + 16f;
        }

        private void SelectCoc(int id)
        {
            cocSelectedId = id;
            cocPortraitSeed = int.MinValue;
            nextRefresh = 0f;
        }

        private void RefreshCoc()
        {
            if (cocTreeNote == null) return;

            bool available = highCommand != null && highCommand.Available;
            if (!available)
            {
                PlaceCocLog(0);
                cocDossierBlock.gameObject.SetActive(false);
                cocTreeNote.text = "";
                cocLogNote.text = "";
                BindCocSideToggles("—", "—");
                for (int i = 0; i < cocRows.Length; i++) cocRows[i].Hide();
                for (int i = 0; i < cocLogRows.Length; i++) cocLogRows[i].Hide();
                BindDossier(null);
                return;
            }
            if (!cocDossierBlock.gameObject.activeSelf) cocDossierBlock.gameObject.SetActive(true);

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
            BindDossier(selectedOnActiveSide ? selected : null);
        }

        /// <summary>Hang the log under the last post the tree filled.</summary>
        private void PlaceCocLog(int posts)
        {
            if (cocLogBlock == null) return;

            float y = cocRosterTop - posts * cocRowPitch;
            AvKit.Place(cocLogBlock,
                new Rect(cocBody.x + AvScreen.SpineInset, y,
                         CocTreeWidth, CocLogBlock));

            // The build sized the scroll area for a full roster so a longer one can be
            // reached; a six-post staff then hands back the rows it did not use, and the
            // dossier reports the height its own record needs, so the page cannot scroll
            // into blank space under either column.
            if (!cocScrolled || cocRoot == null) return;
            float depth = cocBody.y - (y - CocLogBlock);
            float height = Mathf.Max(cocDossierHeight + 8f, depth + 8f);
            if (Mathf.Abs(cocRoot.sizeDelta.y - height) > 0.5f)
                cocRoot.sizeDelta = new Vector2(cocRoot.sizeDelta.x, height);
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
                cocLogRows[shown].Bind(line, SelectCoc, LogTargetOnVisibleSide(line.TargetId));
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
                cocFileNo.text = "—";
                cocPhotoRef.text = "NO FILE OPEN";
                cocName.text = "NO POST SELECTED";
                cocName.color = AvTheme.Dim;
                cocDossierPips.Bind(2, AvTheme.Dim);
                SetPortrait(null, int.MinValue);
                cocDossierRail.color = AvTheme.RailInert;
            }
            else
            {
                cocFileNo.text = "FORM CC-" + (view.Tier + 1);
                cocPhotoRef.text = "ID PHOTO  ·  REF " + (view.PortraitSeed & 0xFFFF).ToString("X4");
                cocName.text = view.Name;
                cocName.color = view.IsKia ? AvTheme.Disabled : AvTheme.TextPrimary;
                cocDossierPips.Bind(view.Tier, view.IsFriendly ? AvTheme.Accent : AvTheme.Warning);
                SetPortrait(view.Portrait, view.PortraitSeed);
                cocPortrait.color = view.IsKia || (!view.IsFriendly && !view.IsKnown)
                    ? new Color(1f, 1f, 1f, 0.45f)
                    : Color.white;
                cocDossierRail.color = StatusColor(view);
            }

            PlaceCocRole(view);
            ResizeCocDossier(LayoutCocDossier(view));
            PlaceCocLog(cocFilledRows);
        }

        /// <summary>The office line, centred under the name with its tier pips as one group.</summary>
        private void PlaceCocRole(CommanderView view)
        {
            float inner = cocDossierWidth - CocDetailPad * 2f;
            string role = view == null ? "" : view.Role;
            cocRole.text = role;
            float roleWidth = Mathf.Min(inner - 20f, Mathf.Ceil(cocRole.GetPreferredValues(role).x) + 2f);
            float groupX = CocDetailPad + Mathf.Max(0f, (inner - roleWidth - 20f) * 0.5f);
            AvKit.Place(cocDossierPips.Rect, new Rect(groupX, -178f, 12f, 8f));
            AvKit.Place(cocRole.rectTransform, new Rect(groupX + 20f, -172f, roleWidth, 13f));
        }

        private static string Percent(CommanderView view) =>
            view == null ? "—" : Mathf.RoundToInt(Mathf.Clamp01(view.Weight) * 100f) + "%";

        /// <summary>Stamp the post's disposition on the file, in the state's own ink.</summary>
        private float PlaceStamp(float x, float y, float width, CommanderView view)
        {
            float top = y - CocStampHeight;
            AvKit.Place(cocStampRoot, new Rect(x, top, width, CocStampHeight));

            string state = view == null ? null : StampStateOf(view);
            AvStyle style = AvStyleHost.Style(string.IsNullOrEmpty(state) ? "stamp" : "stamp " + state);
            cocDossierStamp.text = view == null ? "NO FILE" : StatusOf(view);
            cocDossierStamp.color = AvStyleHost.Resolve(style.Color, AvTheme.Dim);
            if (cocStampFill != null)
                cocStampFill.color = AvStyleHost.Resolve(style.Background, Color.clear);
            if (cocStampBorder != null)
                foreach (Image line in cocStampBorder)
                    line.color = AvStyleHost.Resolve(style.Border, AvTheme.Frame);
            return top - 14f;
        }

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

            AvKit.Place((RectTransform)cocDossierFill.transform,
                        new Rect(0f, 0f, cocDossierWidth, cocDossierHeight));
            AvKit.Place((RectTransform)cocDossierBorder[0].transform, new Rect(0f, 0f, cocDossierWidth, 1f));
            AvKit.Place((RectTransform)cocDossierBorder[1].transform,
                        new Rect(0f, -cocDossierHeight + 1f, cocDossierWidth, 1f));
            AvKit.Place((RectTransform)cocDossierBorder[2].transform, new Rect(0f, 0f, 1f, cocDossierHeight));
            AvKit.Place((RectTransform)cocDossierBorder[3].transform,
                        new Rect(cocDossierWidth - 1f, 0f, 1f, cocDossierHeight));

            const float len = 6f;
            float bottom = -cocDossierHeight;
            AvKit.Place((RectTransform)cocDossierTicks[0].transform, new Rect(0f, 0f, len, 1f));
            AvKit.Place((RectTransform)cocDossierTicks[1].transform, new Rect(0f, 0f, 1f, len));
            AvKit.Place((RectTransform)cocDossierTicks[2].transform,
                        new Rect(cocDossierWidth - len, 0f, len, 1f));
            AvKit.Place((RectTransform)cocDossierTicks[3].transform,
                        new Rect(cocDossierWidth - 1f, 0f, 1f, len));
            AvKit.Place((RectTransform)cocDossierTicks[4].transform, new Rect(0f, bottom + 1f, len, 1f));
            AvKit.Place((RectTransform)cocDossierTicks[5].transform, new Rect(0f, bottom + len, 1f, len));
            AvKit.Place((RectTransform)cocDossierTicks[6].transform,
                        new Rect(cocDossierWidth - len, bottom + 1f, len, 1f));
            AvKit.Place((RectTransform)cocDossierTicks[7].transform,
                        new Rect(cocDossierWidth - 1f, bottom + len, 1f, len));

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

        /// <summary>The card's corner brackets, kept so the frame can follow the card's height.</summary>
        private static Image[] CocTicks(RectTransform parent, Rect area, Color color, float len = 6f)
        {
            return new[]
            {
                AvKit.Rule(parent, new Rect(area.x, area.y, len, 1f), color),
                AvKit.Rule(parent, new Rect(area.x, area.y, 1f, len), color),
                AvKit.Rule(parent, new Rect(area.x + area.width - len, area.y, len, 1f), color),
                AvKit.Rule(parent, new Rect(area.x + area.width - 1f, area.y, 1f, len), color),
                AvKit.Rule(parent, new Rect(area.x, area.y - area.height + 1f, len, 1f), color),
                AvKit.Rule(parent, new Rect(area.x, area.y - area.height + len, 1f, len), color),
                AvKit.Rule(parent, new Rect(area.x + area.width - len, area.y - area.height + 1f, len, 1f), color),
                AvKit.Rule(parent, new Rect(area.x + area.width - 1f, area.y - area.height + len, 1f, len), color),
            };
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

        /// <summary>The stamp's ink: the sheet carries ok, warn and bad, and nothing else.</summary>
        private static string StampStateOf(CommanderView view)
        {
            switch (ChipStateOf(view))
            {
                case "live": return "ok";
                case "warn": return "warn";
                case "danger": return "bad";
                default: return null;
            }
        }

        private void SetPortrait(Sprite sprite, int seed)
        {
            if (cocPortrait == null || seed == cocPortraitSeed) return;
            cocPortraitSeed = seed;
            bool has = sprite != null;
            cocPortrait.enabled = has;
            cocPortrait.sprite = sprite;
            if (cocPortraitFallback != null) cocPortraitFallback.gameObject.SetActive(!has);
        }

        private static string StatusOf(CommanderView view)
        {
            if (view.IsKia) return "KIA";
            if (!view.IsFriendly && !view.IsKnown) return "UNCONFIRMED";
            if (view.Alert) return "UNDER FIRE";
            if (view.InTransit) return "EN ROUTE";
            if (view.Disrupted) return "SUCCESSION";
            if (!view.IsFriendly && view.IntelAge >= 0f) return "SEEN " + Mathf.RoundToInt(view.IntelAge) + "S AGO";
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
        private static string LogRail(CommanderLogTone tone)
        {
            switch (tone)
            {
                case CommanderLogTone.Economy: return "ready";
                case CommanderLogTone.Order: return "info";
                case CommanderLogTone.Contact: return "armed";
                case CommanderLogTone.Loss: return "hostile";
                case CommanderLogTone.Alert: return "danger";
                default: return "locked";
            }
        }

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
                value.overflowMode = TextOverflowModes.Truncate;
            }

            /// <summary>Lay the field at y; returns the y the next field should use.</summary>
            public float Bind(float x, float y, float width, string keyText, string valueText, Color color)
            {
                key.text = keyText ?? "";
                float keyWidth = Mathf.Min(Mathf.Ceil(key.GetPreferredValues(key.text).x) + 6f, width * 0.5f);
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
                pay.enableWordWrapping = true;
                pay.overflowMode = TextOverflowModes.Truncate;
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

            public CocLogRow(RectTransform parent, float x, float y, float width)
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

                age = AvStyled.Label(rect, new Rect(12f, 0f, AgeWidth, CocLogPitch - 2f), "", "metric-cap");
                text = AvStyled.Label(rect,
                    new Rect(12f + AgeWidth, 0f, width - AgeWidth - 16f, CocLogPitch - 2f), "", "row-sub");
                text.fontSize = AvTokens.FontSmall;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;

                hit = AvKit.HitButton(rect, new Rect(0f, 0f, width, CocLogPitch - 2f), null);
                hit.SetEnabled(false);
                root.SetActive(false);
            }

            public void Bind(CommanderLogLine line, Action<int> select, bool clickable)
            {
                id = line.TargetId;
                Color tone = LogColor(line.Tone);
                dot.SetKind("dot", tone);
                age.text = TheaterReadout.Age(line.Age);
                text.text = line.Text;
                text.color = tone;

                hit.SetAction(clickable ? (Action)(() => select(id)) : null);
                hit.SetEnabled(clickable);
                hit.WithTooltip(clickable ? "Open this post's card." : null);
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

                Rect frame = new Rect(0f, -3f, PortraitWidth, portraitHeight);
                AvKit.Panel(rect, frame, AvTheme.SurfaceInert);
                AvKit.Outline(rect, frame, AvTheme.Frame.WithAlpha(0.6f));
                portrait = AvKit.Panel(rect,
                    new Rect(frame.x + 1f, frame.y - 1f, frame.width - 2f, frame.height - 2f), Color.white);
                portrait.type = Image.Type.Simple;
                portrait.preserveAspect = true;
                portrait.raycastTarget = false;
                portrait.enabled = false;

                // No portrait is a Wing Command gap, not a blank: the row's own person glyph
                // holds the frame so the row keeps its shape.
                var glyphObject = new GameObject("PortraitGlyph", typeof(RectTransform), typeof(MfdGlyph));
                var glyphRect = (RectTransform)glyphObject.transform;
                glyphRect.SetParent(rect, false);
                portraitGlyph = glyphObject;
                MfdGlyph glyph = glyphObject.GetComponent<MfdGlyph>();
                glyph.raycastTarget = false;
                glyph.SetKind("person", AvTheme.Dim);
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
                weight = AvKit.ProgressBar(rect, new Rect(width - trail, 0f, trail, 3f), 0f,
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
                AvKit.Place((RectTransform)portrait.transform,
                    new Rect(indent + 3f, top - 1f, PortraitWidth - 2f, portraitHeight - 2f));
                AvKit.Place((RectTransform)portraitGlyph.transform,
                    new Rect(indent + (PortraitWidth - 12f) * 0.5f + 1f,
                             top - (portraitHeight - 12f) * 0.5f, 12f, 12f));
                AvKit.Place(pips.Rect, new Rect(textX, top - 17f, 12f, 12f));
                AvKit.Place(rank.rectTransform, new Rect(textX + 15f, top - 17f, 54f, 12f));
                AvKit.Place(name.rectTransform, new Rect(textX, top - 1f, textWidth, 14f));
                AvKit.Place(role.rectTransform, new Rect(textX, top - 30f, textWidth, 12f));
                AvKit.Place(weight.rectTransform, new Rect(width - trail, -(height - 9f), trail, 3f));

                if (view.PortraitSeed != portraitSeed)
                {
                    portraitSeed = view.PortraitSeed;
                    bool has = view.Portrait != null;
                    portrait.enabled = has;
                    portrait.sprite = view.Portrait;
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

                // The office, then what the post is worth: the row states the bonus's first
                // entry, because that is the number a pilot hunting this commander wants.
                string bonus = !view.IsFriendly && !view.IsKnown ? "" : FirstBonus(view.Bonus);
                role.text = view.Role + (string.IsNullOrEmpty(bonus) ? "" : "  ·  " + bonus);

                status.text = StatusOf(view);
                status.color = selected ? AvTheme.Accent
                    : view.IsKia || (!view.IsFriendly && !view.IsKnown) ? AvTheme.Disabled
                    : state;

                weight.fillAmount = Mathf.Clamp01(view.Weight);
                weight.color = view.IsKia || (!view.IsFriendly && !view.IsKnown) ? AvTheme.RailInert
                    : view.IsFriendly ? AvTheme.Accent
                    : AvTheme.Warning;

                Color rest = selected
                    ? AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha))
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

                hit.WithTooltip((view.IsFriendly
                    ? "Open the card for " + view.Name + "  ·  " + view.Role
                    : view.IsKnown
                        ? "Confirmed contact: " + view.Name + "  ·  " + view.Role
                        : "Unconfirmed post: " + view.Name + " — no local intel.") +
                    (string.IsNullOrEmpty(bonus) ? "" : "  ·  " + bonus));
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
