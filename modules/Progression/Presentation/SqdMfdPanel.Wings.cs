using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private Image huntRail;
        private TMP_Text huntTitle;
        private TMP_Text huntDetails;
        private TMP_Text rosterPage;
        private AvButton previousWings;
        private AvButton nextWings;
        private int wingPage;
        private readonly List<WingRow> wingRows = new List<WingRow>(WingRowsPerPage);
        private readonly string[] wingPortraitKeys = new string[WingRowsPerPage];

        private void ResetWingsPage()
        {
            huntRail = null;
            huntTitle = huntDetails = rosterPage = null;
            previousWings = nextWings = null;
            wingPage = 0;
            wingRows.Clear();
            Array.Clear(wingPortraitKeys, 0, wingPortraitKeys.Length);
        }

        // ---- WINGS page ------------------------------------------------------------------

        private void BuildWingsPage(RectTransform parent, Rect body)
        {
            parent = AvScreen.Scroll(parent, body, 570f, out body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            AvStyled.Box(parent, new Rect(x, y, width, 78f), "section band");
            huntRail = AvStyled.Rail(parent, new Rect(x + 6f, y - 8f, 3f, 60f), "ready");
            huntTitle = PlainLabel(parent, new Rect(x + 18f, y - 8f, width - 28f, 18f),
                "ACE HUNT STANDBY", "section-title");
            huntDetails = PlainLabel(parent, new Rect(x + 18f, y - 30f, width - 28f, 40f),
                "Awaiting enemy wing reports.", "row-sub");
            y -= 86f;

            previousWings = AvStyled.Button(parent, new Rect(x, y, 78f, 28f), "< PREV", "btn", () =>
            {
                wingPage = Math.Max(0, wingPage - 1);
                nextRefresh = 0f;
            }, AvButtonStyle.Quiet);
            previousWings.WithTooltip("Show the previous two enemy wings.");
            rosterPage = PlainLabel(parent, new Rect(x + 84f, y, width - 168f, 28f), "NO WINGS", "kv-value");
            rosterPage.alignment = TextAlignmentOptions.Center;
            nextWings = AvStyled.Button(parent, new Rect(x + width - 78f, y, 78f, 28f), "NEXT >", "btn", () =>
            {
                int count = squad != null ? squad.EnemyWingCount : 0;
                if ((wingPage + 1) * WingRowsPerPage < count) wingPage++;
                nextRefresh = 0f;
            }, AvButtonStyle.Quiet);
            nextWings.WithTooltip("Show the next two enemy wings, including previous encounters.");
            y -= 36f;

            for (int i = 0; i < WingRowsPerPage; i++)
            {
                var rowObject = new GameObject("EnemyWing_" + i, typeof(RectTransform));
                var root = (RectTransform)rowObject.transform;
                root.SetParent(parent, false);
                AvKit.Place(root, new Rect(x, y, width, 148f));
                AvStyled.Box(root, new Rect(0f, 0f, width, 144f), "section");

                var row = new WingRow
                {
                    Root = root,
                    Rail = AvStyled.Rail(root, new Rect(4f, -8f, 3f, 126f), "locked"),
                };

                Rect portraitFrame = new Rect(14f, -8f, 44f, 62f);
                AvKit.Panel(root, portraitFrame, new Color32(18, 22, 26, 255));
                AvKit.Outline(root, portraitFrame, AvTheme.Frame.WithAlpha(0.6f));
                row.PortraitFallback = PlainLabel(root,
                    new Rect(portraitFrame.x + 2f, portraitFrame.y - 24f, portraitFrame.width - 4f, 20f),
                    "NO\nVISUAL", "row-sub");
                row.PortraitFallback.alignment = TextAlignmentOptions.Center;
                row.Portrait = AvKit.Panel(root,
                    new Rect(portraitFrame.x + 1f, portraitFrame.y - 1f, portraitFrame.width - 2f, portraitFrame.height - 2f),
                    Color.white);
                row.Portrait.type = Image.Type.Simple;
                row.Portrait.preserveAspect = true;
                row.Portrait.raycastTarget = false;

                row.Symbol = PlainLabel(root, new Rect(66f, -6f, 44f, 26f), "", "section-title");
                row.Wing = PlainLabel(root, new Rect(112f, -6f, width - 124f, 18f), "", "row-name");
                row.Ace = PlainLabel(root, new Rect(112f, -27f, width - 124f, 16f), "", "kv-value");
                row.Skill = PlainLabel(root, new Rect(66f, -49f, width - 78f, 15f), "", "row-sub");

                for (int badge = 0; badge < row.Badges.Length; badge++)
                {
                    RectTransform slot = AvKit.Panel(root, new Rect(66f + badge * 30f, -67f, 26f, 34f),
                        new Color32(22, 26, 30, 255)).rectTransform;
                    row.Badges[badge] = slot.gameObject;
                    AvKit.Outline(slot, new Rect(0f, 0f, 26f, 34f), AvTheme.RailCaution.WithAlpha(0.35f));
                    Glyph(slot, new Rect(4f, -2f, 18f, 18f),
                        HuntMark.Toughness + badge, AvTheme.RailCaution);
                    TMP_Text caption = PlainLabel(slot, new Rect(0f, -20f, 26f, 12f),
                        AceSkillCatalog.All[badge].Code, "section-title-note");
                    caption.alignment = TextAlignmentOptions.Center;
                }
                row.NoSkills = PlainLabel(root, new Rect(66f, -102f, width - 78f, 14f),
                    "NO ACTIVE THREAT SKILLS", "row-sub");

                row.Status = PlainLabel(root, new Rect(66f, -116f, width * 0.55f, 15f), "", "kv-value");
                row.Members = PlainLabel(root, new Rect(width * 0.60f, -116f, width * 0.40f - 14f, 15f), "", "kv-value");
                row.Members.alignment = TextAlignmentOptions.MidlineRight;
                row.Target = PlainLabel(root, new Rect(14f, -130f, width - 28f, 14f), "", "row-sub");

                wingRows.Add(row);
                y -= 154f;
            }

            AvStyled.Label(parent, new Rect(x, y - 4f, width, 48f),
                "ACE KILL: +1 SKILL POINT. Downed aces may return stronger.\nFriendly wings and friendly aces: manage the recruited squadron in WMC.", "row-sub");
        }

        // ---- WINGS refresh ---------------------------------------------------------------

        private void RefreshWingsPage()
        {
            if (huntTitle == null) return;
            bool hunted = squad != null && squad.HuntActive;
            huntTitle.text = hunted ? "ACE HUNT ACTIVE — YOU ARE THE TARGET" : "ACE HUNT STANDBY";
            huntTitle.color = huntRail.color = hunted ? AvTheme.Alert : AvTheme.RailInfo;
            huntDetails.text = squad != null ? squad.Status : "Enemy wing reports are unavailable.";
            int count = Math.Max(0, squad != null ? squad.EnemyWingCount : 0);
            wingPage = Math.Min(wingPage, Math.Max(0, (count - 1) / WingRowsPerPage));
            int first = wingPage * WingRowsPerPage;
            rosterPage.text = count == 0 ? "NO ENEMY WINGS ENCOUNTERED" :
                (first + 1) + "–" + Math.Min(first + WingRowsPerPage, count) + " OF " + count + " WINGS";
            previousWings.SetEnabled(wingPage > 0);
            nextWings.SetEnabled(first + WingRowsPerPage < count);

            for (int i = 0; i < wingRows.Count; i++)
            {
                WingRow row = wingRows[i];
                bool visible = first + i < count;
                row.Root.gameObject.SetActive(visible);
                if (!visible) continue;
                EnemyWingView wing = squad.GetEnemyWing(first + i);
                row.Symbol.text = wing.Symbol;
                row.Wing.text = wing.WingName;
                row.Ace.text = "ACE: " + wing.AceName;
                row.Skill.text = "TIER " + wing.Tier + " · SKILL " + wing.Skill +
                    (wing.Returns > 0 ? " · RETURN #" + wing.Returns : " · FIRST ENCOUNTER");
                row.Status.text = wing.Status;
                row.Members.text = wing.MembersAlive + " / " + wing.MemberCount + " ALIVE";
                row.Target.text = wing.MembersAlive <= 0 ? "WING NO LONGER ACTIVE" :
                    string.IsNullOrEmpty(wing.TargetName) ? "TARGET: NORMAL MISSION ORDERS" : "TARGET: " + wing.TargetName;
                Color color = wing.MembersAlive <= 0 ? AvTheme.Dim :
                    !string.IsNullOrEmpty(wing.TargetName) ? AvTheme.Alert : AvTheme.RailInfo;
                row.Status.color = row.Rail.color = row.Symbol.color = color;

                int mask = wing.AbilityMask & AceSkillCatalog.MaskWindow;
                bool anySkill = false;
                for (int badge = 0; badge < row.Badges.Length; badge++)
                {
                    bool active = AceSkillCatalog.Has(mask, badge);
                    row.Badges[badge].SetActive(active);
                    anySkill |= active;
                }
                row.NoSkills.gameObject.SetActive(!anySkill);

                if (!string.Equals(wingPortraitKeys[i], wing.AceName, StringComparison.Ordinal))
                {
                    wingPortraitKeys[i] = wing.AceName;
                    string ace = wing.AceName ?? string.Empty;
                    int separator = ace.IndexOf(" / ", StringComparison.Ordinal);
                    string name = separator < 0 ? ace : ace.Substring(0, separator);
                    string handle = separator < 0 ? string.Empty : ace.Substring(separator + 3);
                    Sprite sprite = WingLink.PilotPortrait(name, handle);
                    row.Portrait.sprite = sprite;
                    row.Portrait.enabled = sprite != null;
                    row.PortraitFallback.gameObject.SetActive(sprite == null);
                }
            }
        }
    }
}
