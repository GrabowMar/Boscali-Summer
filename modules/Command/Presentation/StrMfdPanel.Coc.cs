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
    /// <para>The page is one instrument: the roster. Every post is a portrait, its rank, the
    /// commander's name and office, the disposition the faction can stand behind, and a track
    /// for the post's share of the staff — the weight the survival stipend is paid on. Posts
    /// are ordered parents-first and hang off the trunk line of the post they report to, so a
    /// base commander is drawn under the commander it answers to rather than under whichever
    /// post the host happened to list above it.</para>
    ///
    /// <para>The dossier below carries the selected commander: generated portrait (the same
    /// one every ace and wingman wears), rank and office, post site and any decoration,
    /// traits, service bio, and the staff orders with their reasons. Enemy posts are listed by
    /// identity; until local intel confirms one, its disposition reads unconfirmed, its
    /// portrait and rail stay inert, and it cannot be put on the kill list.</para>
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        /// <summary>
        /// Rows the roster can hold. HighCommand's wire ceiling is eight posts per faction;
        /// six are fielded today and the pitch is chosen so six plus the dossier fill the page.
        /// </summary>
        private const int CocRosterRows = 8;

        /// <summary>Posts a faction fields. Used only to size the row pitch to the page.</summary>
        private const float CocFieldedPosts = 6f;

        /// <summary>Page header, side switch and roster heading, down to the first row.</summary>
        private const float CocRosterTop = 80f;
        private const float CocRowGap = 6f;
        private const float CocRowMinHeight = 46f;
        private const float CocRowMaxHeight = 68f;

        /// <summary>The name line's midline: where a branch's elbow meets its trunk.</summary>
        private const float CocElbow = 9f;

        private const float CocDossierHeight = 146f;
        private const float CocDossierTop = 20f;
        private const float CocOrdersHeight = 28f;
        private const float CocDossierGap = 6f;
        private const float CocBlockHeight =
            CocDossierTop + CocDossierHeight + CocDossierGap + CocOrdersHeight;

        private readonly CommanderRow[] cocRows = new CommanderRow[CocRosterRows];
        private readonly CommanderView[] cocVisible = new CommanderView[CocRosterRows];
        private readonly int[] cocIds = new int[CocRosterRows];
        private readonly int[] cocParents = new int[CocRosterRows];
        private readonly int[] cocOrder = new int[CocRosterRows];
        private readonly bool[] cocOrdered = new bool[CocRosterRows];
        private RectTransform cocRoot, cocDossierBlock;
        private Rect cocBody;
        private bool cocScrolled;
        private float cocRosterTop, cocRowPitch, cocRowHeight;
        private GameObject cocDossier;
        private TMP_Text cocOwnNote, cocDossierNote;
        private TMP_Text cocName, cocRole, cocMeta, cocTraits, cocBio, cocDossierChip;
        private Image cocPortrait, cocDossierRail, cocDossierChipBox;
        private TMP_Text cocPortraitFallback;
        private AvButton cocCommend, cocRelocate, cocBounty;
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
            cocRoot = cocDossierBlock = null;
            cocBody = default(Rect);
            cocScrolled = false;
            cocRosterTop = cocRowPitch = cocRowHeight = 0f;
            cocDossier = null;
            cocOwnNote = cocDossierNote = null;
            cocName = cocRole = cocMeta = cocTraits = cocBio = cocDossierChip = null;
            cocPortrait = cocDossierRail = cocDossierChipBox = null;
            cocPortraitFallback = null;
            cocCommend = cocRelocate = cocBounty = null;
            cocAlliedToggle = cocHostileToggle = null;
            cocShowHostile = false;
            cocSelectedId = -1;
            cocPortraitSeed = int.MinValue;
        }

        private void BuildCocPage(GameObject page)
        {
            Rect view = shell.Body;
            float width = view.width - AvScreen.SpineInset;

            // Six posts are fielded. Sizing the rows to the page rather than the page to the
            // rows is what stops a six-post staff leaving a hole above the status strip; a
            // roster longer than the page scrolls instead.
            float slack = view.height - (CocRosterTop + CocBlockHeight + 8f);
            cocRowPitch = Mathf.Clamp(slack / CocFieldedPosts, CocRowMinHeight + CocRowGap,
                                      CocRowMaxHeight + CocRowGap);
            cocRowHeight = cocRowPitch - CocRowGap;
            float contentHeight = CocRosterTop + CocRosterRows * cocRowPitch + CocBlockHeight;

            Rect body;
            cocRoot = AvScreen.Scroll((RectTransform)page.transform, view, contentHeight, out body);
            cocScrolled = !ReferenceEquals(cocRoot, (RectTransform)page.transform);
            cocBody = body;

            float x = body.x + AvScreen.SpineInset;
            float y = body.y;

            AvStyled.Spine(cocRoot, new Rect(body.x, body.y, 3f, body.height));

            y = SectionHeader(cocRoot, x, y, width, "CHAIN OF COMMAND", "FACTION ROSTER", band: false);

            float toggleWidth = (width - 6f) * 0.5f;
            cocAlliedToggle = AvStyled.Button(
                cocRoot, new Rect(x, y, toggleWidth, 26f), "ALLIED  —", "btn",
                () => { cocShowHostile = false; nextRefresh = 0f; },
                AvButtonStyle.Toggle)
                .WithTooltip("Show the allied chain of command.");
            cocHostileToggle = AvStyled.Button(
                cocRoot, new Rect(x + toggleWidth + 6f, y, toggleWidth, 26f), "HOSTILE  —", "btn",
                () => { cocShowHostile = true; nextRefresh = 0f; },
                AvButtonStyle.Toggle)
                .WithTooltip("Show the opposing chain of command. Unconfirmed posts cannot be marked.");
            cocAlliedToggle.SetLatched(true);
            y -= 38f;

            cocOwnNote = CocSection(cocRoot, x, y, width, "BATTLE STAFF", "", out _);
            y -= 20f;
            cocRosterTop = y;
            for (int i = 0; i < CocRosterRows; i++)
            {
                cocRows[i] = new CommanderRow(
                    cocRoot, x, y - i * cocRowPitch, width, cocRowHeight, cocRowPitch, SelectCoc);
            }

            BuildCocDossierBlock(x, y - CocRosterRows * cocRowPitch, width);
        }

        /// <summary>A staff section title with a live right-hand count, tied to the spine.</summary>
        private static TMP_Text CocSection(
            RectTransform parent, float x, float y, float width, string title, string note,
            out TMP_Text heading)
        {
            AvStyled.SpineTick(parent, x - AvScreen.SpineInset + 3f, y - 7f);
            heading = AvStyled.Label(parent, new Rect(x, y, width * 0.55f, 14f), title, "section-title");
            return AvStyled.Label(parent, new Rect(x + width * 0.55f, y, width * 0.45f, 14f),
                note, "metric-cap");
        }

        /// <summary>
        /// The dossier header, card and orders as one block, so the dossier follows the last
        /// post the roster filled instead of a fixed row.
        /// </summary>
        private void BuildCocDossierBlock(float x, float y, float width)
        {
            var root = new GameObject("CocDossierBlock", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(cocRoot, false);
            AvKit.Place(rect, new Rect(x, y, width, CocBlockHeight));
            cocDossierBlock = rect;

            AvStyled.Box(rect, new Rect(-6f, 4f, width + 12f, 22f), "section band");
            AvStyled.SpineTick(rect, -AvScreen.SpineInset + 3f, -7f);
            AvStyled.Label(rect, new Rect(0f, 0f, width * 0.5f, 14f), "DOSSIER", "section-title");
            cocDossierNote = AvStyled.Label(rect, new Rect(width * 0.5f, 0f, width * 0.5f, 14f),
                "", "metric-cap");

            BuildDossier(rect, 0f, -CocDossierTop, width);
        }

        private void BuildDossier(RectTransform parent, float x, float y, float width)
        {
            var root = new GameObject("CocDossier", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            AvKit.Place(rootRect, new Rect(x, y, width, CocDossierHeight + CocDossierGap + CocOrdersHeight));
            cocDossier = root;

            Rect card = new Rect(0f, 0f, width, CocDossierHeight);
            AvStyled.Box(rootRect, card, "card focused");
            cocDossierRail = AvStyled.Rail(rootRect, new Rect(5f, -8f, 3f, CocDossierHeight - 16f), "locked");

            const float portraitWidth = 64f;
            const float portraitHeight = 96f;
            Rect frame = new Rect(14f, -12f, portraitWidth, portraitHeight);
            AvKit.Panel(rootRect, frame, CocPortraitBack);
            AvKit.Outline(rootRect, frame, AvTheme.Frame);
            AvKit.CornerTicks(rootRect, frame, AvTheme.Hairline.WithAlpha(0.5f));

            cocPortraitFallback = AvStyled.Label(rootRect,
                new Rect(frame.x + 2f, frame.y - 32f, frame.width - 4f, 30f), "NO\nVISUAL", "row-sub",
                align: TextAlignmentOptions.Center);
            cocPortrait = AvKit.Panel(rootRect,
                new Rect(frame.x + 1f, frame.y - 1f, frame.width - 2f, frame.height - 2f), Color.white);
            cocPortrait.type = Image.Type.Simple;
            cocPortrait.preserveAspect = true;
            cocPortrait.raycastTarget = false;
            cocPortrait.enabled = false;

            const float chipWidth = 108f;
            Rect chip = new Rect(width - chipWidth - 14f, -14f, chipWidth, 18f);
            cocDossierChipBox = AvStyled.Box(rootRect, chip, "chip");
            cocDossierChip = AvStyled.Label(rootRect, chip, "", "chip");

            float textX = frame.x + portraitWidth + 12f;
            float textWidth = width - textX - 14f;
            cocName = AvStyled.Label(rootRect,
                new Rect(textX, -12f, textWidth - chipWidth - 10f, 17f), "", "row-name");
            cocName.fontSize = 14f;
            cocRole = AvStyled.Label(rootRect, new Rect(textX, -31f, textWidth, 12f), "", "section-title");
            cocMeta = AvStyled.Label(rootRect, new Rect(textX, -45f, textWidth, 12f), "", "section-title-note");
            cocTraits = AvStyled.Label(rootRect, new Rect(textX, -60f, textWidth, 12f), "", "row-sub");
            cocBio = AvStyled.Label(rootRect, new Rect(textX, -76f, textWidth, 58f), "", "row-sub");

            float buttonWidth = (width - 12f) / 3f;
            float buttonY = -(CocDossierHeight + CocDossierGap);
            cocCommend = AvStyled.Button(rootRect, new Rect(0f, buttonY, buttonWidth, CocOrdersHeight),
                "COMMEND", "btn",
                () => { if (cocSelectedId >= 0) highCommand?.RequestCommend(cocSelectedId); nextRefresh = 0f; });
            cocRelocate = AvStyled.Button(rootRect,
                new Rect(buttonWidth + 6f, buttonY, buttonWidth, CocOrdersHeight), "RELOCATE", "btn",
                () => { if (cocSelectedId >= 0) highCommand?.RequestRelocate(cocSelectedId); nextRefresh = 0f; });
            cocBounty = AvStyled.Button(rootRect,
                new Rect((buttonWidth + 6f) * 2f, buttonY, buttonWidth, CocOrdersHeight), "MARK BOUNTY", "btn",
                () => { if (cocSelectedId >= 0) highCommand?.RequestBounty(cocSelectedId); nextRefresh = 0f; },
                AvButtonStyle.Danger);
        }

        private void SelectCoc(int id)
        {
            cocSelectedId = id;
            cocPortraitSeed = int.MinValue;
            nextRefresh = 0f;
        }

        private void RefreshCoc()
        {
            if (cocOwnNote == null) return;

            bool available = highCommand != null && highCommand.Available;
            if (!available)
            {
                PlaceCocDossier(0);
                cocDossier.SetActive(false);
                cocOwnNote.text = "STAFF NOT RUNNING";
                cocDossierNote.text = "";
                BindCocSideToggles("—", "—");
                for (int i = 0; i < cocRows.Length; i++) cocRows[i].Hide();
                BindDossier(null);
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

            PlaceCocDossier(ordered);
            BindCocSideToggles(ownCount.ToString(), enemyTotal.ToString());

            int points = highCommand.CommandPoints;
            if (!cocShowHostile)
            {
                cocOwnNote.text = ownCount == 0
                    ? "NO POSTS REPORTED"
                    : ownCount + (ownCount == 1 ? " POST" : " POSTS") + "  ·  " + points + " CP";
            }
            else
            {
                cocOwnNote.text = enemyTotal == 0 ? "NO POSTS REPORTED"
                    : enemyKnown == 0 ? "NO CONFIRMED CONTACTS  ·  " + points + " CP"
                    : enemyKnown + " OF " + enemyTotal + " CONFIRMED  ·  " + points + " CP";
            }

            if (selected == null) cocSelectedId = -1;
            BindDossier(selectedOnActiveSide ? selected : null);
        }

        /// <summary>Hang the dossier under the last post the roster filled.</summary>
        private void PlaceCocDossier(int posts)
        {
            if (cocDossierBlock == null) return;

            float y = cocRosterTop - posts * cocRowPitch;
            AvKit.Place(cocDossierBlock,
                new Rect(cocBody.x + AvScreen.SpineInset, y,
                         cocBody.width - AvScreen.SpineInset, CocBlockHeight));

            // The build sized the scroll area for a full roster so a longer one can be
            // reached; a six-post staff then hands back the rows it did not use, so the page
            // cannot scroll into blank space under the dossier.
            if (!cocScrolled || cocRoot == null) return;
            float height = Mathf.Max(CocRosterTop, cocBody.y - (y - CocBlockHeight) + 8f);
            if (Mathf.Abs(cocRoot.sizeDelta.y - height) > 0.5f)
                cocRoot.sizeDelta = new Vector2(cocRoot.sizeDelta.x, height);
        }

        private void BindCocSideToggles(string alliedCount, string hostileCount)
        {
            cocAlliedToggle.SetText("ALLIED  " + alliedCount);
            cocHostileToggle.SetText("HOSTILE  " + hostileCount);
            cocAlliedToggle.SetLatched(!cocShowHostile);
            cocHostileToggle.SetLatched(cocShowHostile);
        }

        private void BindDossier(CommanderView view)
        {
            if (cocName == null) return;

            if (view == null)
            {
                cocDossierNote.text = "SELECT A POST";
                cocName.text = "NO POST SELECTED";
                cocName.color = AvTheme.Dim;
                cocRole.text = cocMeta.text = cocTraits.text = cocBio.text = "";
                SetDossierChip(null);
                SetPortrait(null, int.MinValue);
                cocDossierRail.color = AvTheme.RailInert;
                cocCommend.SetEnabled(false);
                cocRelocate.SetEnabled(false);
                cocBounty.SetEnabled(false);
                cocBounty.SetText("MARK BOUNTY");
                return;
            }

            cocDossierNote.text = view.IsFriendly ? "ALLIED POST" : view.IsKnown ? "CONFIRMED CONTACT" : "UNCONFIRMED POST";
            cocName.text = view.IsKia ? view.Name + "  [KIA]" : view.Name;
            cocName.color = view.IsKia ? AvTheme.Disabled : AvTheme.TextPrimary;
            cocRole.text = view.Rank + "  ·  " + view.Role;
            cocMeta.text = view.Location +
                           (string.IsNullOrEmpty(view.Decoration) ? "" : "  ·  " + view.Decoration);
            cocTraits.text = view.Traits;
            cocBio.text = view.Bio;
            SetDossierChip(view);
            SetPortrait(view.Portrait, view.PortraitSeed);
            cocPortrait.color = view.IsKia || (!view.IsFriendly && !view.IsKnown)
                ? new Color(1f, 1f, 1f, 0.45f)
                : Color.white;
            cocDossierRail.color = AvStyleHost.Resolve(
                AvStyleHost.Style("rail " + StateOf(view)).Background, AvTheme.RailInert);

            cocCommend.SetEnabled(view.CanCommend);
            cocCommend.WithTooltip(view.CanCommend
                ? "Spend 1 command point: a decoration for " + view.Name +
                  " and a short cohesion boost for your faction."
                : "A commendation needs a command point and a post below maximum honours.");
            cocRelocate.SetEnabled(view.CanRelocate);
            cocRelocate.WithTooltip(view.CanRelocate
                ? "Spend 1 command point: move " + view.Name +
                  " to another friendly base in a vulnerable convoy. Interception kills."
                : "A relocation needs a command point and a second friendly base.");
            cocBounty.SetEnabled(view.CanBounty);
            cocBounty.SetText(view.BountyMarked ? "CLEAR MARK" : "MARK BOUNTY");
            cocBounty.WithTooltip(!view.IsFriendly && !view.IsKnown
                ? "No confirmed contact — local intel must spot this post before it can be marked."
                : view.BountyMarked
                    ? "Remove the kill-list bonus on " + view.Name + "."
                    : "Place " + view.Name + " on the kill list: marked targets pay a larger bounty.");
        }

        /// <summary>The post's disposition as a chip, so the dossier states how it stands
        /// without a second text line repeating the roster.</summary>
        private void SetDossierChip(CommanderView view)
        {
            if (cocDossierChip == null) return;

            string state = view == null ? "inert" : ChipStateOf(view);
            AvStyle style = AvStyleHost.Style("chip " + state);
            cocDossierChip.text = view == null ? "NO POST" : StatusOf(view);
            cocDossierChip.color = AvStyleHost.Resolve(style.Color, AvTheme.Dim);
            if (cocDossierChipBox != null)
                cocDossierChipBox.color = AvStyleHost.Resolve(style.Background, AvTheme.SurfaceInert);
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
            if (view.InTransit) return "EN ROUTE";
            if (view.Disrupted) return "DISRUPTED";
            if (view.BountyMarked) return "MARKED";
            if (!view.IsFriendly && view.IntelAge >= 0f) return "SEEN " + Mathf.RoundToInt(view.IntelAge) + "S AGO";
            return "ACTIVE";
        }

        private static string StateOf(CommanderView view)
        {
            if (view.IsKia) return "locked";
            if (!view.IsFriendly && !view.IsKnown) return "locked";
            if (view.InTransit) return "info";
            if (view.Disrupted) return "cooling";
            if (view.BountyMarked) return "hostile";
            return view.IsFriendly ? "ready" : "hostile";
        }

        private static string ChipStateOf(CommanderView view)
        {
            if (view.IsKia) return "danger";
            if (!view.IsFriendly && !view.IsKnown) return "inert";
            if (view.BountyMarked) return "danger";
            if (view.Disrupted) return "warn";
            if (view.InTransit) return "info";
            return view.IsFriendly ? "live" : "warn";
        }

        /// <summary>
        /// One post, read as a row of the roster: a state rail, the commander's portrait, rank,
        /// name and office, the disposition, and a track for the post's share of the staff. A
        /// post below the theater commander is indented under a trunk that runs from the post
        /// it reports to, and every row is closed by a hairline.
        /// </summary>
        private sealed class CommanderRow
        {
            private const float RankWidth = 46f;
            private const float TrailWidth = 100f;
            private const float TierStep = 14f;
            private const float PortraitWidth = 34f;
            private const float PortraitHeight = 42f;

            /// <summary>The row height every offset in this row is written against.</summary>
            private const float DesignHeight = 48f;

            private readonly GameObject root;
            private readonly Image background, rail, guide, tick, weight, portrait;
            private readonly GameObject portraitGlyph;
            private readonly TMP_Text rank, name, role, status;
            private readonly AvButton hit;
            private readonly float width, height, pitch, portraitHeight;
            private int id = -1;
            private int portraitSeed = int.MinValue;
            private bool portraitDim;

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

                // No portrait is a Wing Command gap, not a blank: the rail's own person glyph
                // holds the frame so the row keeps its shape.
                var glyphObject = new GameObject("PortraitGlyph", typeof(RectTransform), typeof(MfdGlyph));
                var glyphRect = (RectTransform)glyphObject.transform;
                glyphRect.SetParent(rect, false);
                portraitGlyph = glyphObject;
                MfdGlyph glyph = glyphObject.GetComponent<MfdGlyph>();
                glyph.raycastTarget = false;
                glyph.SetKind("person", AvTheme.Dim);
                glyphObject.SetActive(false);

                rank = AvStyled.Label(rect, new Rect(0f, -3f, RankWidth, 13f), "", "section-title");
                rank.characterSpacing = 4f;
                name = AvStyled.Label(rect, new Rect(0f, -3f, 10f, 14f), "", "row-name");
                status = AvStyled.Label(rect, new Rect(width - TrailWidth, -3f, TrailWidth, 13f), "", "metric-cap");
                role = AvStyled.Label(rect, new Rect(0f, -20f, 10f, 12f), "", "section-title-note");
                weight = AvKit.ProgressBar(rect, new Rect(width - TrailWidth, 0f, TrailWidth, 6f), 0f,
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
                float indent = 10f + view.Tier * TierStep;
                float textX = indent + PortraitWidth + 8f;
                float nameX = textX + RankWidth + 4f;
                float trail = width - TrailWidth;

                // Rows take the pitch the page could afford, so the name block is centred in
                // whatever height it was given rather than pinned to a design size.
                float top = -3f - (height - DesignHeight) * 0.5f;
                AvKit.Place((RectTransform)portrait.transform, new Rect(indent + 1f, top - 1f,
                    PortraitWidth - 2f, portraitHeight - 2f));
                AvKit.Place((RectTransform)portraitGlyph.transform,
                    new Rect(indent + (PortraitWidth - 14f) * 0.5f, top - (portraitHeight - 14f) * 0.5f,
                             14f, 14f));
                AvKit.Place(rank.rectTransform, new Rect(textX, top, RankWidth, 13f));
                AvKit.Place(name.rectTransform, new Rect(nameX, top,
                    Mathf.Max(40f, trail - nameX - 8f), 14f));
                AvKit.Place(role.rectTransform, new Rect(textX, top - 17f,
                    Mathf.Max(40f, trail - textX - 8f), 12f));
                AvKit.Place(weight.rectTransform, new Rect(trail, -(height - 11f), TrailWidth, 6f));

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

                rank.text = view.Rank;
                rank.color = AvTheme.Dim;
                name.text = view.IsKia ? view.Name + "  [KIA]" : view.Name;
                name.color = selected ? AvTheme.Accent
                    : view.IsKia ? AvTheme.Disabled
                    : !view.IsFriendly && !view.IsKnown ? AvTheme.Disabled
                    : view.IsFriendly ? AvTheme.TextPrimary
                    : AvTheme.Warning;

                role.text = view.Role + (string.IsNullOrEmpty(view.Decoration) ? "" : "  ·  " + view.Decoration);

                status.text = StatusOf(view);
                status.color = selected ? AvTheme.Accent
                    : view.IsKia ? AvTheme.Disabled
                    : !view.IsFriendly && !view.IsKnown ? AvTheme.Disabled
                    : view.InTransit ? AvTheme.RailInfo
                    : view.Disrupted ? AvTheme.RailCaution
                    : view.BountyMarked ? AvTheme.RailDanger
                    : AvTheme.Dim;

                weight.fillAmount = Mathf.Clamp01(view.Weight);
                weight.color = view.IsKia || (!view.IsFriendly && !view.IsKnown) ? AvTheme.RailInert
                    : view.IsFriendly ? AvTheme.Accent
                    : AvTheme.Warning;

                Color rest = selected
                    ? AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha))
                    : Color.clear;
                hit.SetRowHighlight(background, rest, CocHover);
                rail.color = selected ? AvTheme.Accent
                    : AvStyleHost.Resolve(AvStyleHost.Style("rail " + StateOf(view)).Background, AvTheme.RailInert);

                // The trunk: one pitch-long segment per post at the parent's indent, tiling with
                // the post above into a single line down the branch, with the elbow marking
                // which row it belongs to. A level's line ends where that level's posts end.
                bool branch = view.Tier > 0;
                guide.gameObject.SetActive(branch);
                tick.gameObject.SetActive(branch);
                if (branch)
                {
                    float gx = indent - TierStep;
                    AvKit.Place(guide.rectTransform, new Rect(gx, pitch - height, 1f, pitch));
                    AvKit.Place(tick.rectTransform, new Rect(gx, -CocElbow, TierStep, 1f));
                }

                hit.WithTooltip((view.IsFriendly
                    ? "Open the dossier for " + view.Name + "  ·  " + view.Role
                    : view.IsKnown
                        ? "Confirmed contact: " + view.Name + "  ·  " + view.Role
                        : "Unconfirmed post: " + view.Name + " — no local intel.") +
                    "  ·  " + Mathf.RoundToInt(Mathf.Clamp01(view.Weight) * 100f) + "% OF STAFF");
                if (!root.activeSelf) root.SetActive(true);
            }

            public void Hide()
            {
                id = -1;
                if (root.activeSelf) root.SetActive(false);
            }
        }
    }
}
