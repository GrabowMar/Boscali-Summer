using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// COC — both factions' chains of command on one page. Each row is a post: rank, name,
    /// office and state, indented by tier with a quiet elbow; the dossier carries the selected
    /// commander's generated portrait (the same one every ace and wingman wears), traits,
    /// service bio and staff orders. Enemy posts are listed by identity; until local intel
    /// confirms one its disposition reads unconfirmed and it cannot be put on the kill list.
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        private const int CocOwnRows = 6;
        private const int CocEnemyRows = 4;
        private const int CocRowCount = CocOwnRows + CocEnemyRows;
        private const float CocRowPitch = 36f;
        private const float CocRowHeight = 30f;
        private const float CocDossierHeight = 142f;
        private const float CocContentHeight = 646f;

        private readonly CommanderRow[] cocRows = new CommanderRow[CocRowCount];
        private RectTransform cocRoot;
        private GameObject cocDossier;
        private TMP_Text cocSummary, cocSummaryNote, cocOwnNote, cocEnemyNote, cocDossierNote;
        private TMP_Text cocName, cocRole, cocMeta, cocTraits, cocBio;
        private Image cocPortrait, cocDossierRail;
        private TMP_Text cocPortraitFallback;
        private AvButton cocCommend, cocRelocate, cocBounty;
        private int cocSelectedId = -1;
        private int cocPortraitSeed = int.MinValue;

        private static readonly Color CocPortraitBack = new Color32(10, 18, 13, 255);
        private static readonly Color CocHover = new Color(1f, 1f, 1f, 0.06f);

        private void ResetCoc()
        {
            Array.Clear(cocRows, 0, cocRows.Length);
            cocRoot = null;
            cocDossier = null;
            cocSummary = cocSummaryNote = cocOwnNote = cocEnemyNote = cocDossierNote = null;
            cocName = cocRole = cocMeta = cocTraits = cocBio = null;
            cocPortrait = cocDossierRail = null;
            cocPortraitFallback = null;
            cocCommend = cocRelocate = cocBounty = null;
            cocSelectedId = -1;
            cocPortraitSeed = int.MinValue;
        }

        private void BuildCocPage(GameObject page)
        {
            Rect body = shell.Body;
            cocRoot = AvScreen.Scroll((RectTransform)page.transform, body, CocContentHeight, out body);
            float x = body.x + AvScreen.SpineInset;
            float width = body.width - AvScreen.SpineInset;
            float y = body.y;

            AvStyled.Spine(cocRoot, new Rect(body.x, body.y, 3f, body.height));

            y = SectionHeader(cocRoot, x, y, width, "CHAIN OF COMMAND", "FACTION BATTLE STAFF", band: false);

            cocSummary = AvStyled.Label(cocRoot, new Rect(x, y, width * 0.55f, 16f), "", "row-name");
            cocSummaryNote = AvStyled.Label(cocRoot, new Rect(x + width * 0.55f, y, width * 0.45f, 16f),
                "", "metric-cap");
            y -= 20f;

            cocOwnNote = CocSection(cocRoot, x, y, width, "ALLIED STAFF", "");
            y -= 18f;
            for (int i = 0; i < CocOwnRows; i++)
            {
                cocRows[i] = new CommanderRow(cocRoot, x, y - i * CocRowPitch, width, SelectCoc);
            }
            y -= CocOwnRows * CocRowPitch + 6f;

            cocEnemyNote = CocSection(cocRoot, x, y, width, "HOSTILE STAFF", "");
            y -= 18f;
            for (int i = 0; i < CocEnemyRows; i++)
            {
                cocRows[CocOwnRows + i] = new CommanderRow(cocRoot, x, y - i * CocRowPitch, width, SelectCoc);
            }
            y -= CocEnemyRows * CocRowPitch + 6f;

            cocDossierNote = CocDossierSection(cocRoot, x, y, width, "DOSSIER");
            BuildDossier(x, y - 20f, width);
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

        /// <summary>The banded dossier header; its note carries the selected post's state.</summary>
        private static TMP_Text CocDossierSection(
            RectTransform parent, float x, float y, float width, string title)
        {
            AvStyled.Box(parent, new Rect(x - 6f, y + 4f, width + 12f, 22f), "section band");
            AvStyled.SpineTick(parent, x - AvScreen.SpineInset + 3f, y - 7f);
            AvStyled.Label(parent, new Rect(x, y, width * 0.5f, 14f), title, "section-title");
            return AvStyled.Label(parent, new Rect(x + width * 0.5f, y, width * 0.5f, 14f), "",
                "metric-cap");
        }

        private void BuildDossier(float x, float y, float width)
        {
            var root = new GameObject("CocDossier", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(cocRoot, false);
            AvKit.Place(rootRect, new Rect(x, y, width, CocDossierHeight + 34f));
            cocDossier = root;

            Rect card = new Rect(0f, 0f, width, CocDossierHeight);
            AvStyled.Box(rootRect, card, "card");
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

            float textX = frame.x + portraitWidth + 12f;
            float textWidth = width - textX - 14f;
            cocName = AvStyled.Label(rootRect, new Rect(textX, -12f, textWidth, 17f), "", "row-name");
            cocName.fontSize = 14f;
            cocRole = AvStyled.Label(rootRect, new Rect(textX, -31f, textWidth, 12f), "", "section-title");
            cocMeta = AvStyled.Label(rootRect, new Rect(textX, -45f, textWidth, 12f), "", "section-title-note");
            cocTraits = AvStyled.Label(rootRect, new Rect(textX, -59f, textWidth, 24f), "", "row-sub");
            cocBio = AvStyled.Label(rootRect, new Rect(textX, -85f, textWidth, 48f), "", "row-sub");

            float buttonWidth = (width - 12f) / 3f;
            float buttonY = -(CocDossierHeight + 6f);
            cocCommend = AvStyled.Button(rootRect, new Rect(0f, buttonY, buttonWidth, 28f), "COMMEND", "btn",
                () => { if (cocSelectedId >= 0) highCommand?.RequestCommend(cocSelectedId); nextRefresh = 0f; });
            cocRelocate = AvStyled.Button(rootRect, new Rect(buttonWidth + 6f, buttonY, buttonWidth, 28f), "RELOCATE", "btn",
                () => { if (cocSelectedId >= 0) highCommand?.RequestRelocate(cocSelectedId); nextRefresh = 0f; });
            cocBounty = AvStyled.Button(rootRect, new Rect((buttonWidth + 6f) * 2f, buttonY, buttonWidth, 28f),
                "MARK BOUNTY", "btn",
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
            if (cocSummary == null) return;

            bool available = highCommand != null && highCommand.Available;
            cocDossier.SetActive(available);

            if (!available)
            {
                cocSummary.text = highCommand == null
                    ? "CHAIN OF COMMAND IS NOT RUNNING ON THIS HOST."
                    : (highCommand.Status ?? "CHAIN OF COMMAND IS FORMING.");
                cocSummary.color = AvTheme.Dim;
                cocSummaryNote.text = "";
                cocDossierNote.text = "";
                cocOwnNote.text = "";
                cocEnemyNote.text = "";
                for (int i = 0; i < cocRows.Length; i++) cocRows[i].Hide();
                BindDossier(null);
                return;
            }

            float cohesion = Mathf.Clamp01(highCommand.FriendlyCohesion);
            cocSummary.text = "COHESION " + Mathf.RoundToInt(cohesion * 100f) + "%";
            cocSummary.color = cohesion >= 0.6f ? AvTheme.TextPrimary : AvTheme.RailCaution;
            cocSummaryNote.text = highCommand.FriendlyActive + " ACTIVE  ·  " + highCommand.FriendlyKia +
                                  " KIA  ·  " + highCommand.CommandPoints + " CP";

            IReadOnlyList<CommanderView> list = highCommand.Commanders;
            int ownCount = 0, enemyCount = 0, enemyKnown = 0, enemyTotal = 0;
            CommanderView selected = null;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    CommanderView view = list[i];
                    bool shown = view.IsFriendly ? ownCount < CocOwnRows : enemyCount < CocEnemyRows;
                    if (view.IsFriendly)
                    {
                        if (shown) cocRows[ownCount++].Bind(view, view.Id == cocSelectedId);
                    }
                    else
                    {
                        enemyTotal++;
                        if (shown)
                        {
                            if (view.IsKnown) enemyKnown++;
                            cocRows[CocOwnRows + enemyCount++].Bind(view, view.Id == cocSelectedId);
                        }
                    }
                    if (shown && view.Id == cocSelectedId) selected = view;
                }
            }

            for (int i = ownCount; i < CocOwnRows; i++) cocRows[i].Hide();
            for (int i = enemyCount; i < CocEnemyRows; i++) cocRows[CocOwnRows + i].Hide();

            cocOwnNote.text = ownCount == 0 ? "NO POSTS REPORTED"
                : ownCount + (ownCount == 1 ? " POST" : " POSTS");
            cocEnemyNote.text = enemyTotal == 0 ? "NO POSTS REPORTED"
                : enemyTotal > enemyCount
                    ? enemyCount + " OF " + enemyTotal + " POSTS" + (enemyKnown > 0 ? "  ·  " + enemyKnown + " SEEN" : "")
                    : enemyKnown == 0 ? "NO CONFIRMED CONTACTS"
                    : enemyKnown + " OF " + enemyCount + " CONFIRMED";

            if (selected == null) cocSelectedId = -1;
            BindDossier(selected);
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
            cocMeta.text = view.Location + "  ·  " + StatusOf(view) +
                           (string.IsNullOrEmpty(view.Decoration) ? "" : "  ·  " + view.Decoration);
            cocTraits.text = view.Traits;
            cocBio.text = view.Bio;
            SetPortrait(view.Portrait, view.PortraitSeed);
            cocPortrait.color = view.IsKia ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
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

        private sealed class CommanderRow
        {
            private const float RankWidth = 46f;
            private const float StatusWidth = 92f;
            private const float TierStep = 14f;

            private readonly GameObject root;
            private readonly Image background, rail, guide, tick;
            private readonly TMP_Text rank, name, role, status, meta;
            private readonly AvButton hit;
            private readonly float width;
            private int id = -1;

            public CommanderRow(RectTransform parent, float x, float y, float width, Action<int> select)
            {
                this.width = width;
                root = new GameObject("CommanderRow", typeof(RectTransform));
                var rect = root.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, CocRowHeight));

                background = AvKit.Panel(rect, new Rect(0f, 0f, width, CocRowHeight), Color.clear);
                rail = AvStyled.Rail(rect, new Rect(0f, 0f, 3f, CocRowHeight - 2f), "locked");
                guide = AvKit.Rule(rect, new Rect(0f, 0f, 1f, 15f), AvTheme.Hairline.WithAlpha(0.22f));
                tick = AvKit.Rule(rect, new Rect(0f, 0f, 14f, 1f), AvTheme.Hairline.WithAlpha(0.22f));

                rank = AvStyled.Label(rect, new Rect(0f, -1f, RankWidth, 13f), "", "section-title");
                rank.characterSpacing = 4f;
                name = AvStyled.Label(rect, new Rect(0f, -1f, 10f, 13f), "", "row-name");
                status = AvStyled.Label(rect, new Rect(width - StatusWidth, -1f, StatusWidth, 13f), "", "metric-cap");
                role = AvStyled.Label(rect, new Rect(0f, -14f, 10f, 12f), "", "section-title-note");
                meta = AvStyled.Label(rect, new Rect(width - StatusWidth, -14f, StatusWidth, 12f), "", "metric-cap");

                hit = AvKit.HitButton(rect, new Rect(0f, 0f, width, CocRowHeight), () =>
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
                float textX = indent + RankWidth + 4f;
                float textWidth = Mathf.Max(40f, width - textX - StatusWidth);

                AvKit.Place(rank.rectTransform, new Rect(indent, -1f, RankWidth, 13f));
                AvKit.Place(name.rectTransform, new Rect(textX, -1f, textWidth, 13f));
                AvKit.Place(role.rectTransform, new Rect(textX, -14f, textWidth, 12f));

                rank.text = view.Rank;
                rank.color = AvTheme.Dim;
                name.text = view.IsKia ? view.Name + "  [KIA]" : view.Name;
                name.color = selected ? AvTheme.Accent
                    : view.IsKia ? AvTheme.Disabled
                    : !view.IsFriendly && !view.IsKnown ? AvTheme.Disabled
                    : view.IsFriendly ? AvTheme.TextPrimary
                    : AvTheme.Warning;

                role.text = view.Role;

                status.text = StatusOf(view);
                status.color = selected ? AvTheme.Accent
                    : view.IsKia ? AvTheme.Disabled
                    : !view.IsFriendly && !view.IsKnown ? AvTheme.Dim
                    : view.InTransit ? AvTheme.RailInfo
                    : view.Disrupted ? AvTheme.RailCaution
                    : view.BountyMarked ? AvTheme.RailDanger
                    : AvTheme.Dim;
                meta.text = view.Decoration ?? "";

                Color rest = selected
                    ? AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha))
                    : Color.clear;
                hit.SetRowHighlight(background, rest, CocHover);
                rail.color = selected ? AvTheme.Accent
                    : AvStyleHost.Resolve(AvStyleHost.Style("rail " + StateOf(view)).Background, AvTheme.RailInert);

                // A quiet elbow: the trunk sits under the parent's own indent, nothing spans rows.
                bool branch = view.Tier > 0;
                guide.gameObject.SetActive(branch);
                tick.gameObject.SetActive(branch);
                if (branch)
                {
                    float gx = indent - TierStep;
                    AvKit.Place(guide.rectTransform, new Rect(gx, 0f, 1f, 15f));
                    AvKit.Place(tick.rectTransform, new Rect(gx, -14f, TierStep, 1f));
                }

                hit.WithTooltip(view.IsFriendly
                    ? "Open the dossier for " + view.Name + "  ·  " + view.Role
                    : view.IsKnown
                        ? "Confirmed contact: " + view.Name + "  ·  " + view.Role
                        : "Unconfirmed post: " + view.Name + " — no local intel.");
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
