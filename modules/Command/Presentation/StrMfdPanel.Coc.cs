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
    /// COC — the faction's chain of command. A generated staff tree with a dossier for the
    /// selected post: portrait, traits, bio, kill-list and relocation orders. Enemy posts
    /// appear only while confirmed by local intel; everything else reads as no contact.
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        private const int CocOwnRows = 6;
        private const int CocEnemyRows = 4;
        private const int CocRowCount = CocOwnRows + CocEnemyRows;
        private const float CocContentHeight = 660f;
        private const float CocRowPitch = 36f;

        private readonly CommanderRow[] cocRows = new CommanderRow[CocRowCount];
        private RectTransform cocRoot;
        private TMP_Text cocSummary, cocSignal, cocOwnLabel, cocEnemyLabel, cocDossierHint;
        private TMP_Text cocName, cocRole, cocMeta, cocTraits, cocLocation, cocDecor, cocBio;
        private Image cocPortrait;
        private TMP_Text cocPortraitFallback;
        private AvButton cocCommend, cocRelocate, cocBounty;
        private int cocSelectedId = -1;
        private string cocPortraitKey;

        private static readonly Color CocPortraitBack = new Color32(10, 18, 13, 255);
        private static readonly Color CocHover = new Color(1f, 1f, 1f, 0.06f);

        private void ResetCoc()
        {
            Array.Clear(cocRows, 0, cocRows.Length);
            cocRoot = null;
            cocSummary = cocSignal = cocOwnLabel = cocEnemyLabel = cocDossierHint = null;
            cocName = cocRole = cocMeta = cocTraits = cocLocation = cocDecor = cocBio = null;
            cocPortrait = null;
            cocPortraitFallback = null;
            cocCommend = cocRelocate = cocBounty = null;
            cocSelectedId = -1;
            cocPortraitKey = null;
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

            cocSummary = AvStyled.Label(cocRoot, new Rect(x, y, width, 16f), "", "row-name");
            y -= 18f;
            cocSignal = AvStyled.Label(cocRoot, new Rect(x, y, width, 30f), "", "row-sub");
            y -= 34f;

            cocOwnLabel = AvStyled.Label(cocRoot, new Rect(x, y, width, 14f), "YOUR STAFF", "section-title-note");
            y -= 16f;
            for (int i = 0; i < CocOwnRows; i++)
            {
                cocRows[i] = new CommanderRow(cocRoot, x, y - i * CocRowPitch, width, SelectCoc);
            }
            y -= CocOwnRows * CocRowPitch + 6f;

            cocEnemyLabel = AvStyled.Label(cocRoot, new Rect(x, y, width, 14f), "ENEMY STAFF · CONFIRMED CONTACTS", "section-title-note");
            y -= 16f;
            for (int i = 0; i < CocEnemyRows; i++)
            {
                cocRows[CocOwnRows + i] = new CommanderRow(cocRoot, x, y - i * CocRowPitch, width, SelectCoc);
            }
            y -= CocEnemyRows * CocRowPitch + 10f;

            y = SectionHeader(cocRoot, x, y, width, "DOSSIER", "SELECTED POST", band: true);

            cocDossierHint = AvStyled.Label(cocRoot, new Rect(x, y, width, 16f),
                "SELECT A POST FOR THE DOSSIER.", "row-sub");

            const float portraitWidth = 56f;
            const float portraitHeight = 70f;
            Rect frame = new Rect(x, y - portraitHeight - 4f, portraitWidth, portraitHeight);
            AvKit.Panel(cocRoot, frame, CocPortraitBack);
            AvKit.Outline(cocRoot, frame, AvTheme.Frame);

            cocPortraitFallback = AvStyled.Label(cocRoot, new Rect(x + 2f, y - portraitHeight + 8f, portraitWidth - 4f, 40f),
                "NO\nVISUAL", "row-sub", align: TextAlignmentOptions.Center);
            cocPortrait = AvKit.Panel(cocRoot, new Rect(x + 3f, y - portraitHeight - 1f, portraitWidth - 6f, portraitHeight - 6f), Color.white);
            cocPortrait.type = Image.Type.Simple;
            cocPortrait.preserveAspect = true;
            cocPortrait.raycastTarget = false;
            cocPortrait.enabled = false;

            float textX = x + portraitWidth + 10f;
            float textWidth = width - portraitWidth - 10f;
            cocName = AvStyled.Label(cocRoot, new Rect(textX, y - 4f, textWidth, 16f), "", "row-name");
            cocRole = AvStyled.Label(cocRoot, new Rect(textX, y - 22f, textWidth, 16f), "", "kv-key");
            cocMeta = AvStyled.Label(cocRoot, new Rect(textX, y - 40f, textWidth, 16f), "", "row-sub");
            cocTraits = AvStyled.Label(cocRoot, new Rect(textX, y - 58f, textWidth, 16f), "", "row-sub");
            cocLocation = AvStyled.Label(cocRoot, new Rect(textX, y - 76f, textWidth, 16f), "", "kv-value");
            cocDecor = AvStyled.Label(cocRoot, new Rect(x, y - portraitHeight - 8f, width, 16f), "", "row-sub");
            cocBio = AvStyled.Label(cocRoot, new Rect(x, y - portraitHeight - 26f, width, 44f), "", "row-sub");

            float buttonWidth = (width - 12f) / 3f;
            float buttonY = y - portraitHeight - 78f;
            cocCommend = AvStyled.Button(cocRoot, new Rect(x, buttonY, buttonWidth, 26f), "COMMEND", "btn",
                () => { if (cocSelectedId >= 0) highCommand?.RequestCommend(cocSelectedId); nextRefresh = 0f; });
            cocRelocate = AvStyled.Button(cocRoot, new Rect(x + buttonWidth + 6f, buttonY, buttonWidth, 26f), "RELOCATE", "btn",
                () => { if (cocSelectedId >= 0) highCommand?.RequestRelocate(cocSelectedId); nextRefresh = 0f; });
            cocBounty = AvStyled.Button(cocRoot, new Rect(x + (buttonWidth + 6f) * 2f, buttonY, buttonWidth, 26f), "MARK BOUNTY", "btn",
                () => { if (cocSelectedId >= 0) highCommand?.RequestBounty(cocSelectedId); nextRefresh = 0f; });
        }

        private void SelectCoc(int id)
        {
            cocSelectedId = id;
            cocPortraitKey = null;
            nextRefresh = 0f;
        }

        private void RefreshCoc()
        {
            if (cocSummary == null) return;

            bool available = highCommand != null && highCommand.Available;
            if (!available)
            {
                cocSummary.text = highCommand == null
                    ? "CHAIN OF COMMAND IS NOT RUNNING ON THIS HOST."
                    : (highCommand.Status ?? "CHAIN OF COMMAND IS FORMING.");
                cocSummary.color = AvTheme.Dim;
                cocSignal.text = "";
                cocOwnLabel.text = "";
                cocEnemyLabel.text = "";
                for (int i = 0; i < cocRows.Length; i++) cocRows[i].Hide();
                BindDossier(null);
                return;
            }

            float cohesion = Mathf.Clamp01(highCommand.FriendlyCohesion);
            cocSummary.text = "COHESION " + Mathf.RoundToInt(cohesion * 100f) + "%  ·  " +
                              highCommand.FriendlyActive + " ACTIVE  ·  " + highCommand.FriendlyKia + " KIA  ·  " +
                              "COMMAND POINTS " + highCommand.CommandPoints;
            cocSummary.color = cohesion >= 0.6f ? AvTheme.TextPrimary : AvTheme.RailCaution;

            cocSignal.text = highCommand.Signal ?? "";
            cocSignal.color = AvTheme.Dim;

            IReadOnlyList<CommanderView> list = highCommand.Commanders;
            int ownCount = 0, enemyCount = 0;
            CommanderView selected = null;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    CommanderView view = list[i];
                    if (view.IsFriendly)
                    {
                        if (ownCount < CocOwnRows) cocRows[ownCount++].Bind(view, view.Id == cocSelectedId, list, i, true);
                    }
                    else if (enemyCount < CocEnemyRows)
                    {
                        cocRows[CocOwnRows + enemyCount++].Bind(view, view.Id == cocSelectedId, list, i, false);
                    }
                    if (view.Id == cocSelectedId) selected = view;
                }
            }

            for (int i = ownCount; i < CocOwnRows; i++) cocRows[i].Hide();
            for (int i = enemyCount; i < CocEnemyRows; i++) cocRows[CocOwnRows + i].Hide();

            cocOwnLabel.text = ownCount == 0 ? "YOUR STAFF · AWAITING POST REPORTS" : "YOUR STAFF";
            cocEnemyLabel.text = enemyCount == 0
                ? "ENEMY STAFF · NO CONFIRMED CONTACTS"
                : "ENEMY STAFF · " + enemyCount + " CONFIRMED CONTACT" + (enemyCount == 1 ? "" : "S");

            if (selected == null) cocSelectedId = -1;
            BindDossier(selected);
        }

        private void BindDossier(CommanderView view)
        {
            if (cocDossierHint == null) return;

            if (view == null)
            {
                cocDossierHint.text = "SELECT A POST FOR THE DOSSIER.";
                cocName.text = cocRole.text = cocMeta.text = cocTraits.text = "";
                cocLocation.text = cocDecor.text = cocBio.text = "";
                SetPortrait(null, -1);
                cocCommend.SetEnabled(false);
                cocRelocate.SetEnabled(false);
                cocBounty.SetEnabled(false);
                return;
            }

            cocDossierHint.text = "";
            cocName.text = view.IsKia ? view.Name + "  [KIA]" : view.Name;
            cocName.color = view.IsKia ? AvTheme.Disabled : AvTheme.TextPrimary;
            cocRole.text = view.Rank + " · " + view.Role;
            cocMeta.text = "SHARE " + Mathf.RoundToInt(view.Weight * 100f) + "% · " + StatusOf(view);
            cocTraits.text = view.Traits;
            cocLocation.text = view.Location;
            cocDecor.text = view.Decoration;
            cocBio.text = view.Bio;

            SetPortrait(view.Portrait, view.PortraitSeed);

            cocCommend.SetEnabled(view.CanCommend);
            cocCommend.WithTooltip("Spend 1 command point: a decoration for " + view.Name +
                                   " and a short cohesion boost for your faction.");
            cocRelocate.SetEnabled(view.CanRelocate);
            cocRelocate.WithTooltip("Spend 1 command point: move " + view.Name +
                                    " to another friendly base in a vulnerable convoy. Interception kills.");
            cocBounty.SetEnabled(view.CanBounty);
            cocBounty.SetText(view.BountyMarked ? "CLEAR MARK" : "MARK BOUNTY");
            cocBounty.WithTooltip(view.BountyMarked
                ? "Remove the kill-list bonus on " + view.Name + "."
                : "Place " + view.Name + " on the kill list: marked targets pay a larger bounty.");
        }

        private void SetPortrait(Sprite sprite, int key)
        {
            if (cocPortrait == null) return;
            string identity = key.ToString();
            if (identity == cocPortraitKey) return;
            cocPortraitKey = identity;
            bool has = sprite != null;
            cocPortrait.enabled = has;
            cocPortrait.sprite = sprite;
            if (cocPortraitFallback != null) cocPortraitFallback.gameObject.SetActive(!has);
        }

        private static string StatusOf(CommanderView view)
        {
            if (view.IsKia) return "KIA";
            if (view.InTransit) return "EN ROUTE";
            if (view.Disrupted) return "DISRUPTED";
            if (!view.IsFriendly && view.IntelAge >= 0f) return "SEEN " + Mathf.RoundToInt(view.IntelAge) + "S AGO";
            return "ACTIVE";
        }

        private sealed class CommanderRow
        {
            private readonly GameObject root;
            private readonly Image background;
            private readonly Image rail;
            private readonly Image[] guides = new Image[2];
            private readonly Image tick;
            private readonly TMP_Text rank, name, status, role;
            private readonly Image bar;
            private readonly AvButton hit;
            private readonly float width;
            private int id = -1;

            public CommanderRow(RectTransform parent, float x, float y, float width, Action<int> select)
            {
                this.width = width;
                root = new GameObject("CommanderRow", typeof(RectTransform));
                var rect = root.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y - 4f, width, 32f));

                background = AvKit.Panel(rect, new Rect(0f, 0f, width, 32f), Color.clear);
                rail = AvStyled.Rail(rect, new Rect(0f, 0f, 3f, 30f), "locked");
                for (int i = 0; i < guides.Length; i++)
                {
                    guides[i] = AvKit.Rule(rect, new Rect(0f, 0f, 1f, 1f), AvTheme.Hairline.WithAlpha(0.35f));
                }
                tick = AvKit.Rule(rect, new Rect(0f, 0f, 6f, 1f), AvTheme.Hairline.WithAlpha(0.35f));

                rank = AvStyled.Label(rect, new Rect(12f, 0f, 34f, 14f), "", "row-sub");
                name = AvStyled.Label(rect, new Rect(48f, 0f, width - 48f - 92f, 14f), "", "row-name");
                status = AvStyled.Label(rect, new Rect(width - 92f, 0f, 92f, 14f), "", "row-sub",
                    align: TextAlignmentOptions.MidlineRight);
                role = AvStyled.Label(rect, new Rect(48f, -14f, width - 48f - 92f, 14f), "", "row-sub");
                bar = AvKit.ProgressBar(rect, new Rect(width - 88f, -20f, 84f, 4f), 0f, AvTheme.RailInert);

                hit = AvKit.HitButton(rect, new Rect(0f, 0f, width, 32f), () =>
                {
                    if (id >= 0) select(id);
                });
                hit.SetRowHighlight(background, Color.clear, CocHover);
                root.SetActive(false);
            }

            public void Bind(CommanderView view, bool selected, IReadOnlyList<CommanderView> group, int index, bool friendly)
            {
                id = view.Id;
                string state = view.IsKia ? "locked"
                    : friendly ? (view.InTransit ? "info" : view.Disrupted ? "cooling" : "ready")
                    : (view.InTransit ? "contested" : "hostile");

                rail.color = AvStyleHost.Resolve(AvStyleHost.Style("rail " + state).Background, AvTheme.RailInert);
                name.text = view.IsKia ? view.Name + "  [KIA]" : view.Name;
                name.color = view.IsKia ? AvTheme.Disabled : friendly ? AvTheme.TextPrimary : AvTheme.Warning;
                if (selected) name.color = AvTheme.Accent;

                rank.text = view.Rank;
                status.text = StatusOf(view);
                status.color = view.IsKia ? AvTheme.Disabled : view.InTransit ? AvTheme.RailInfo : AvTheme.Dim;

                role.text = view.Role + (string.IsNullOrEmpty(view.Location) ? "" : " · " + view.Location);
                bar.fillAmount = Mathf.Clamp01(view.Weight);
                bar.color = friendly ? AvTheme.RailReady : AvTheme.RailCaution;

                float indent = 12f + view.Tier * 12f;
                float textWidth = width - indent - 36f - 92f;
                AvKit.Place(rank.rectTransform, new Rect(indent, 0f, 34f, 14f));
                AvKit.Place(name.rectTransform, new Rect(indent + 36f, 0f, textWidth, 14f));
                AvKit.Place(role.rectTransform, new Rect(indent + 36f, -14f, textWidth, 14f));

                for (int depth = 0; depth < guides.Length; depth++)
                {
                    bool active = depth < view.Tier && group != null;
                    guides[depth].gameObject.SetActive(active);
                    if (!active) continue;
                    float gx = 6f + depth * 12f;
                    bool continues = HasLaterBranch(group, index, depth, view);
                    AvKit.Place(guides[depth].rectTransform, new Rect(gx, 0f, 1f, continues ? 30f : 16f));
                }
                tick.gameObject.SetActive(view.Tier > 0);
                if (view.Tier > 0)
                {
                    float gx = 6f + (view.Tier - 1) * 12f;
                    AvKit.Place(tick.rectTransform, new Rect(gx, -15f, 7f, 1f));
                }

                hit.WithTooltip(view.IsFriendly
                    ? "Open the dossier for " + view.Name + " · " + view.Role
                    : "Confirmed contact: " + view.Name + " · " + view.Role);
                if (!root.activeSelf) root.SetActive(true);
            }

            public void Hide()
            {
                id = -1;
                if (root.activeSelf) root.SetActive(false);
            }

            /// <summary>True when another later row still hangs off the same ancestor at this depth.</summary>
            private static bool HasLaterBranch(IReadOnlyList<CommanderView> group, int index, int depth, CommanderView node)
            {
                int ancestor = AncestorAt(group, node, depth);
                if (ancestor < 0) return false;
                for (int i = index + 1; i < group.Count; i++)
                {
                    if (group[i].Tier > depth && AncestorAt(group, group[i], depth) == ancestor) return true;
                }
                return false;
            }

            private static int AncestorAt(IReadOnlyList<CommanderView> group, CommanderView node, int depth)
            {
                CommanderView cursor = node;
                int guard = 0;
                while (cursor != null && cursor.Tier > depth && guard++ < 8)
                    cursor = Find(group, cursor.ParentId);
                return cursor != null && cursor.Tier == depth ? cursor.Id : -1;
            }

            private static CommanderView Find(IReadOnlyList<CommanderView> group, int id)
            {
                for (int i = 0; i < group.Count; i++)
                    if (group[i].Id == id) return group[i];
                return null;
            }
        }
    }
}
