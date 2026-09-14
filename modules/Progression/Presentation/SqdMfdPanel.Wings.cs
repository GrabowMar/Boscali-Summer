using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private const int WingmanRows = 4;

        private sealed class FriendlySlot
        {
            public RectTransform Root;
            public TMP_Text Index;
            public TMP_Text Airframe;
            public TMP_Text Status;
        }

        private Image huntRail;
        private TMP_Text huntTitle;
        private TMP_Text huntDetails;
        private TMP_Text rosterPage;
        private AvButton previousWings;
        private AvButton nextWings;
        private int wingPage;
        private readonly List<WingRow> wingRows = new List<WingRow>(WingRowsPerPage);
        private readonly string[] wingPortraitKeys = new string[WingRowsPerPage];

        private Image wingPortrait;
        private TMP_Text wingPortraitFallback;
        private TMP_Text wingCallsign;
        private TMP_Text wingName;
        private TMP_Text wingAirframe;
        private TMP_Text wingCount;
        private TMP_Text wingCountNote;
        private TMP_Text wingTeamNote;
        private readonly List<Aircraft> friendlyWing = new List<Aircraft>(WingmanRows);
        private readonly FriendlySlot[] wingmanSlots = new FriendlySlot[WingmanRows];

        private void ResetWingsPage()
        {
            huntRail = null;
            huntTitle = huntDetails = rosterPage = null;
            previousWings = nextWings = null;
            wingPage = 0;
            wingRows.Clear();
            Array.Clear(wingPortraitKeys, 0, wingPortraitKeys.Length);
            wingPortrait = null;
            wingPortraitFallback = null;
            wingCallsign = wingName = wingAirframe = wingCount = wingCountNote = wingTeamNote = null;
            friendlyWing.Clear();
            Array.Clear(wingmanSlots, 0, wingmanSlots.Length);
        }

        // ---- WINGS page ------------------------------------------------------------------

        private void BuildWingsPage(RectTransform parent, Rect body)
        {
            parent = AvScreen.Scroll(parent, body, 608f, out body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            // ---- Hunt status -------------------------------------------------------------
            AvStyled.Box(parent, new Rect(x, y, width, 78f), "section band");
            huntRail = AvStyled.Rail(parent, new Rect(x + 6f, y - 8f, 3f, 60f), "ready");
            huntTitle = PlainLabel(parent, new Rect(x + 18f, y - 8f, width - 28f, 18f),
                "ACE HUNT STANDBY", "section-title");
            huntDetails = PlainLabel(parent, new Rect(x + 18f, y - 30f, width - 28f, 40f),
                "Awaiting enemy wing reports.", "row-sub");
            y -= 90f;

            // ---- Your wing ---------------------------------------------------------------
            y = DrawSectionTitle(parent, x, y, width, "YOUR WING", "RECRUITED SQUADRON · WMC", band: false);
            BuildFriendlyWing(parent, x, y, width);
            y -= 120f;

            // ---- Hostile wings -----------------------------------------------------------
            y = DrawSectionTitle(parent, x, y, width, "HOSTILE WINGS", "ACE ENCOUNTERS", band: false);

            previousWings = AvStyled.Button(parent, new Rect(x, y, 78f, 28f), "< PREV", "btn", () =>
            {
                wingPage = Math.Max(0, wingPage - 1);
                nextRefresh = 0f;
            }, AvButtonStyle.Quiet);
            previousWings.WithTooltip("Show the previous two hostile wings.");
            rosterPage = PlainLabel(parent, new Rect(x + 84f, y, width - 168f, 28f), "NO WINGS", "kv-value");
            rosterPage.alignment = TextAlignmentOptions.Center;
            nextWings = AvStyled.Button(parent, new Rect(x + width - 78f, y, 78f, 28f), "NEXT >", "btn", () =>
            {
                int count = squad != null ? squad.EnemyWingCount : 0;
                if ((wingPage + 1) * WingRowsPerPage < count) wingPage++;
                nextRefresh = 0f;
            }, AvButtonStyle.Quiet);
            nextWings.WithTooltip("Show the next two hostile wings, including previous encounters.");
            y -= 36f;

            for (int i = 0; i < WingRowsPerPage; i++)
            {
                var rowObject = new GameObject("EnemyWing_" + i, typeof(RectTransform));
                var root = (RectTransform)rowObject.transform;
                root.SetParent(parent, false);
                AvKit.Place(root, new Rect(x, y, width, 124f));
                AvStyled.Box(root, new Rect(0f, 0f, width, 124f), "card");
                AvKit.CornerTicks(root, new Rect(0f, 0f, width, 124f), AvTheme.Frame.WithAlpha(0.5f));

                var row = new WingRow
                {
                    Root = root,
                    Rail = AvStyled.Rail(root, new Rect(4f, -8f, 3f, 108f), "locked"),
                };

                var emptyObject = new GameObject("NoFurtherContacts_" + i, typeof(RectTransform));
                var empty = (RectTransform)emptyObject.transform;
                empty.SetParent(parent, false);
                AvKit.Place(empty, new Rect(x, y, width, 124f));
                AvStyled.Box(empty, new Rect(0f, 0f, width, 124f), "card inert");
                AvStyled.Rail(empty, new Rect(4f, -8f, 3f, 108f), "locked");
                TMP_Text emptyLabel = PlainLabel(empty, new Rect(14f, -52f, width - 28f, 20f),
                    "NO FURTHER HOSTILE CONTACTS ON RECORD", "row-sub");
                emptyLabel.alignment = TextAlignmentOptions.Center;
                emptyObject.SetActive(false);
                row.Empty = emptyObject;

                Rect crestFrame = new Rect(14f, -10f, 54f, 54f);
                AvKit.Panel(root, crestFrame, AvTheme.SurfaceInert);
                AvKit.Outline(root, crestFrame, AvTheme.Frame.WithAlpha(0.6f));
                row.Crest = AvKit.Panel(root,
                    new Rect(crestFrame.x + 1f, crestFrame.y - 1f, crestFrame.width - 2f, crestFrame.height - 2f),
                    Color.white);
                row.Crest.type = Image.Type.Simple;
                row.Crest.preserveAspect = true;
                row.Crest.raycastTarget = false;
                row.Crest.enabled = false;

                Rect portraitFrame = new Rect(74f, -10f, 42f, 54f);
                AvKit.Panel(root, portraitFrame, AvTheme.SurfaceInert);
                AvKit.Outline(root, portraitFrame, AvTheme.Frame.WithAlpha(0.6f));
                row.PortraitFallback = PlainLabel(root,
                    new Rect(portraitFrame.x + 2f, portraitFrame.y - 20f, portraitFrame.width - 4f, 20f),
                    "NO\nVISUAL", "row-sub");
                row.PortraitFallback.alignment = TextAlignmentOptions.Center;
                row.Portrait = AvKit.Panel(root,
                    new Rect(portraitFrame.x + 1f, portraitFrame.y - 1f, portraitFrame.width - 2f, portraitFrame.height - 2f),
                    Color.white);
                row.Portrait.type = Image.Type.Simple;
                row.Portrait.preserveAspect = true;
                row.Portrait.raycastTarget = false;

                row.Symbol = PlainLabel(root, new Rect(124f, -12f, 28f, 18f), "", "section-title");
                row.Wing = PlainLabel(root, new Rect(156f, -10f, 138f, 20f), "", "row-name");
                row.Ace = PlainLabel(root, new Rect(124f, -34f, 172f, 15f), "", "kv-value");
                row.Skill = PlainLabel(root, new Rect(124f, -52f, width - 136f, 14f), "", "row-sub");

                row.Status = PlainLabel(root, new Rect(width - 152f, -12f, 142f, 16f), "", "kv-value");
                row.Status.alignment = TextAlignmentOptions.MidlineRight;
                row.Members = PlainLabel(root, new Rect(width - 152f, -30f, 142f, 14f), "", "kv-value");
                row.Members.alignment = TextAlignmentOptions.MidlineRight;

                for (int badge = 0; badge < row.Badges.Length; badge++)
                {
                    RectTransform slot = AvKit.Panel(root, new Rect(124f + badge * 40f, -70f, 36f, 38f),
                        new Color32(22, 26, 30, 255)).rectTransform;
                    row.Badges[badge] = slot.gameObject;
                    AvKit.Outline(slot, new Rect(0f, 0f, 36f, 38f), AvTheme.RailCaution.WithAlpha(0.35f));
                    Glyph(slot, new Rect(9f, -4f, 18f, 18f),
                        HuntMark.Toughness + badge, AvTheme.RailCaution);
                    TMP_Text caption = PlainLabel(slot, new Rect(0f, -24f, 36f, 12f),
                        AceSkillCatalog.All[badge].Code, "section-title-note");
                    caption.fontSize = AvTokens.FontMicro;
                    caption.characterSpacing = 0f;
                    caption.alignment = TextAlignmentOptions.Center;
                }
                row.NoSkills = PlainLabel(root, new Rect(124f, -68f, 158f, 14f),
                    "NO ACTIVE THREAT SKILLS", "row-sub");

                row.Target = PlainLabel(root, new Rect(288f, -84f, width - 298f, 15f), "", "row-sub");
                row.Target.alignment = TextAlignmentOptions.MidlineRight;

                wingRows.Add(row);
                y -= 132f;
            }

            AvStyled.Label(parent, new Rect(x, y - 4f, width, 44f),
                "ACE KILL: +1 SKILL POINT. Downed aces may return stronger.\nFRIENDLY WING RECRUITING AND ORDERS REMAIN IN WMC.", "row-sub");
        }

        /// <summary>Your own flight, read-only: the career lead plus Wing Command's published
        /// wingmen. Recruiting, loadouts and orders stay in WMC; a slot with no live aircraft
        /// reads NO SIGNAL rather than guessing.</summary>
        private void BuildFriendlyWing(RectTransform parent, float x, float y, float width)
        {
            AvStyled.Box(parent, new Rect(x, y, width, 112f), "card");
            AvKit.CornerTicks(parent, new Rect(x, y, width, 112f), AvTheme.Frame.WithAlpha(0.5f));
            AvStyled.Rail(parent, new Rect(x + 4f, y - 8f, 3f, 96f), "ready");

            Rect portraitFrame = new Rect(x + 14f, y - 10f, 42f, 54f);
            AvKit.Panel(parent, portraitFrame, AvTheme.SurfaceInert);
            AvKit.Outline(parent, portraitFrame, AvTheme.Frame.WithAlpha(0.6f));
            wingPortraitFallback = PlainLabel(parent,
                new Rect(portraitFrame.x + 2f, portraitFrame.y - 20f, portraitFrame.width - 4f, 20f),
                "NO\nVISUAL", "row-sub");
            wingPortraitFallback.alignment = TextAlignmentOptions.Center;
            wingPortrait = AvKit.Panel(parent,
                new Rect(portraitFrame.x + 1f, portraitFrame.y - 1f, portraitFrame.width - 2f, portraitFrame.height - 2f),
                Color.white);
            wingPortrait.type = Image.Type.Simple;
            wingPortrait.preserveAspect = true;
            wingPortrait.raycastTarget = false;
            wingPortrait.enabled = false;

            float textX = portraitFrame.x + portraitFrame.width + 10f;
            float rightX = x + width - 156f;
            float textWidth = rightX - textX - 8f;
            wingCallsign = PlainLabel(parent, new Rect(textX, y - 8f, textWidth, 20f), "PILOT RECORD PENDING", "row-name");
            wingName = PlainLabel(parent, new Rect(textX, y - 30f, textWidth, 15f), "", "kv-value");
            wingAirframe = PlainLabel(parent, new Rect(textX, y - 48f, rightX - textX - 8f, 14f), "", "row-sub");

            wingCount = PlainLabel(parent, new Rect(rightX, y - 8f, 146f, 18f), "", "kv-value");
            wingCount.alignment = TextAlignmentOptions.MidlineRight;
            wingCountNote = PlainLabel(parent, new Rect(rightX, y - 28f, 146f, 14f), "", "section-title-note");
            wingCountNote.alignment = TextAlignmentOptions.MidlineRight;

            wingTeamNote = PlainLabel(parent, new Rect(textX, y - 68f, width - (textX - x) - 14f, 16f),
                "NO RECRUITED WINGMEN — RECRUIT AND TASK THEM IN WMC.", "row-sub");

            float slotWidth = (width - 36f) / 2f;
            for (int i = 0; i < wingmanSlots.Length; i++)
            {
                var slotObject = new GameObject("Wingman_" + i, typeof(RectTransform));
                var slotRect = (RectTransform)slotObject.transform;
                slotRect.SetParent(parent, false);
                AvKit.Place(slotRect, new Rect(
                    x + 14f + (i % 2) * (slotWidth + 8f), y - 66f - (i / 2) * 19f, slotWidth, 16f));

                var slot = new FriendlySlot { Root = slotRect };
                slot.Index = PlainLabel(slotRect, new Rect(0f, 0f, 22f, 16f), "", "section-title-note");
                slot.Airframe = PlainLabel(slotRect, new Rect(24f, 0f, slotWidth - 88f, 16f), "", "row-sub");
                slot.Status = PlainLabel(slotRect, new Rect(slotWidth - 62f, 0f, 62f, 16f), "", "kv-value");
                slot.Status.alignment = TextAlignmentOptions.MidlineRight;
                wingmanSlots[i] = slot;
            }
        }

        // ---- WINGS refresh ---------------------------------------------------------------

        private void RefreshWingsPage()
        {
            if (huntTitle == null) return;
            bool hunted = squad != null && squad.HuntActive;
            huntTitle.text = hunted ? "ACE HUNT ACTIVE — YOU ARE THE TARGET" : "ACE HUNT STANDBY";
            huntTitle.color = huntRail.color = hunted ? AvTheme.Alert : AvTheme.RailInfo;
            huntDetails.text = squad != null ? squad.Status : "Enemy wing reports are unavailable.";

            RefreshFriendlyWing();

            int count = Math.Max(0, squad != null ? squad.EnemyWingCount : 0);
            wingPage = Math.Min(wingPage, Math.Max(0, (count - 1) / WingRowsPerPage));
            int first = wingPage * WingRowsPerPage;
            int last = Math.Min(first + WingRowsPerPage, count);
            rosterPage.text = count == 0 ? "NO HOSTILE WINGS ENCOUNTERED"
                : count == 1 ? "1 OF 1 WING"
                : (first + 1) + "–" + last + " OF " + count + " WINGS";
            previousWings.SetEnabled(wingPage > 0);
            nextWings.SetEnabled(first + WingRowsPerPage < count);
            bool tailPage = (wingPage + 1) * WingRowsPerPage >= count;

            for (int i = 0; i < wingRows.Count; i++)
            {
                WingRow row = wingRows[i];
                bool visible = first + i < count;
                row.Root.gameObject.SetActive(visible);
                if (!visible)
                {
                    row.Empty.SetActive(count > 0 && tailPage && i == count - first);
                    continue;
                }
                row.Empty.SetActive(false);
                EnemyWingView wing = squad.GetEnemyWing(first + i);
                row.Symbol.text = wing.Symbol;
                row.Wing.text = wing.WingName;
                row.Ace.text = "ACE  " + wing.AceName;
                row.Skill.text = "TIER " + wing.Tier + " · SKILL " + wing.Skill +
                    (wing.Returns > 0 ? " · RETURN #" + wing.Returns : " · FIRST ENCOUNTER");
                row.Status.text = wing.Status;
                row.Members.text = wing.MembersAlive + " / " + wing.MemberCount + " ALIVE";
                row.Target.text = wing.MembersAlive <= 0 ? "WING NO LONGER ACTIVE" :
                    string.IsNullOrEmpty(wing.TargetName) ? "TARGET: NORMAL OPERATIONS" : "TARGET: " + wing.TargetName;
                Color color = wing.MembersAlive <= 0 ? AvTheme.Dim :
                    !string.IsNullOrEmpty(wing.TargetName) ? AvTheme.Alert : AvTheme.RailInfo;
                row.Status.color = row.Rail.color = row.Symbol.color = color;
                row.Members.color = wing.MembersAlive <= 0 ? AvTheme.Dim : AvTheme.RailReady;
                row.Target.color = wing.MembersAlive <= 0 ? AvTheme.Dim : AvTheme.TextPrimary;
                row.Crest.color = new Color(1f, 1f, 1f, wing.MembersAlive <= 0 ? 0.35f : 1f);

                int mask = wing.AbilityMask & AceSkillCatalog.MaskWindow;
                bool anySkill = false;
                for (int badge = 0; badge < row.Badges.Length; badge++)
                {
                    bool active = AceSkillCatalog.Has(mask, badge);
                    row.Badges[badge].SetActive(active);
                    anySkill |= active;
                }
                row.NoSkills.gameObject.SetActive(!anySkill);

                string crestKey = (wing.Symbol ?? string.Empty) + "|" + (wing.WingName ?? string.Empty);
                if (row.CrestKey != crestKey)
                {
                    row.CrestKey = crestKey;
                    row.Crest.sprite = EmblemRenderer.Procedural(EmblemDesign.Hostile(crestKey));
                    row.Crest.enabled = true;
                }

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

        private void RefreshFriendlyWing()
        {
            if (wingCallsign == null) return;
            PilotView pilot = squad != null ? squad.Pilot : default;
            bool profile = hasLocalProfile && !string.IsNullOrEmpty(localProfile.Callsign);
            string callsign = profile ? localProfile.Callsign : pilot.Callsign;
            string name = profile ? localProfile.Name : pilot.Name;
            wingCallsign.text = string.IsNullOrEmpty(callsign) ? "PILOT RECORD PENDING" : callsign;
            wingName.text = (string.IsNullOrEmpty(name) ? "—" : name) + "   ·   FLIGHT LEAD";
            SetPortrait(wingPortrait, wingPortraitFallback, PlayerPortrait(name, callsign));

            Aircraft lead = null;
            if (GameManager.GetLocalPlayer<Player>(out Player local) && local != null) lead = local.Aircraft;
            wingAirframe.text = lead == null ? "NO AIRCRAFT ASSIGNED" : AirframeOf(lead) + "   ·   " + AircraftStatus(lead);

            friendlyWing.Clear();
            int total = WingLink.WingCount;
            if (total > 0) WingLink.ResolveWingAircraft(friendlyWing);
            int flying = 0;
            for (int i = 0; i < friendlyWing.Count; i++)
            {
                Aircraft member = friendlyWing[i];
                if (member != null && !member.disabled && !member.HasEjected()) flying++;
            }
            for (int i = 0; i < wingmanSlots.Length; i++)
            {
                FriendlySlot slot = wingmanSlots[i];
                bool visible = i < friendlyWing.Count;
                slot.Root.gameObject.SetActive(visible);
                if (!visible) continue;
                Aircraft aircraft = friendlyWing[i];
                slot.Index.text = "W" + (i + 1);
                slot.Airframe.text = AirframeOf(aircraft);
                slot.Status.text = AircraftStatus(aircraft);
                slot.Status.color = aircraft == null ? AvTheme.Dim
                    : aircraft.disabled || aircraft.HasEjected() ? AvTheme.Alert
                    : aircraft.IsLanded() ? AvTheme.RailCaution : AvTheme.RailReady;
            }

            wingCount.text = total > 0 ? total + (total == 1 ? " WINGMAN" : " WINGMEN") : "NO RECRUITED WING";
            wingCount.color = total > 0 ? AvTheme.RailReady : AvTheme.Dim;
            wingCountNote.text = total > 0 ? flying + " AIRBORNE"
                : WingLink.Available ? "AWAITING RECRUITS" : "WMC OFFLINE";
            wingTeamNote.gameObject.SetActive(total == 0);
        }

        private static string AirframeOf(Aircraft aircraft)
        {
            if (aircraft == null) return "NO SIGNAL";
            string name = aircraft.definition != null ? aircraft.definition.unitName : aircraft.unitName;
            return string.IsNullOrEmpty(name) ? "UNKNOWN AIRFRAME" : name.ToUpperInvariant();
        }

        private static string AircraftStatus(Aircraft aircraft) => aircraft == null ? "NO SIGNAL"
            : aircraft.disabled ? "DISABLED"
            : aircraft.HasEjected() ? "EJECTED"
            : aircraft.IsLanded() ? "LANDED"
            : "AIRBORNE";
    }
}
