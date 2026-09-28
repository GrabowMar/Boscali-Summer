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

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private const int WingmanRows = 4;

        private AvRow huntRow;
        private AvRow wingLeadRow;
        private AvRow wingCountRow;
        private AvTextBlock wingTeamNote;
        private readonly AvRow[] wingmanSlots = new AvRow[WingmanRows];
        private readonly List<Aircraft> friendlyWing = new List<Aircraft>(WingmanRows);

        private AvTextBlock rosterPage;
        private AvControl previousWings;
        private AvControl nextWings;
        private int wingPage;
        private readonly List<HostileWingCard> wingRows = new List<HostileWingCard>(WingRowsPerPage);
        private readonly string[] wingPortraitKeys = new string[WingRowsPerPage];

        private void ResetWingsPage()
        {
            huntRow = wingLeadRow = wingCountRow = null;
            wingTeamNote = null;
            Array.Clear(wingmanSlots, 0, wingmanSlots.Length);
            friendlyWing.Clear();
            rosterPage = null;
            previousWings = nextWings = null;
            wingPage = 0;
            wingRows.Clear();
            Array.Clear(wingPortraitKeys, 0, wingPortraitKeys.Length);
        }

        // ---- WINGS page --------------------------------------------------------------------

        private void BuildWingsPage(AvFlow p)
        {
            p.Section(AvIcon.Skull, "ACE INTELLIGENCE", "LIVE + ENCOUNTER DATA");

            huntRow = p.Add(new AvRow(p.Content));
            huntRow.Set("ACE HUNT STANDBY", "Awaiting enemy wing reports.", null, AvState.Info);

            p.Section(AvIcon.UsersGroup, "FRIENDLY FLIGHT", "MANAGED IN WMC");
            wingLeadRow = p.Add(new AvRow(p.Content));
            wingLeadRow.Set("PILOT RECORD PENDING", null, null, AvState.Info);
            wingCountRow = p.Add(new AvRow(p.Content));
            wingCountRow.Set("WING STRENGTH", null, null, AvState.Info);
            wingTeamNote = p.Add(new AvTextBlock(p.Content, AvTextRole.ProseSmall));
            wingTeamNote.Set("NO RECRUITED WINGMEN — RECRUIT AND TASK THEM IN WMC.");
            for (int i = 0; i < wingmanSlots.Length; i++)
                wingmanSlots[i] = p.Add(new AvRow(p.Content));

            p.Section(AvIcon.Skull, "HOSTILE ACE INTELLIGENCE", "MOST RECENT CONTACTS");
            AvButtons pager = p.Buttons(
                new AvControl.Spec("PREVIOUS", () => { wingPage = Math.Max(0, wingPage - 1); nextRefresh = 0f; },
                    AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec("NEXT", () =>
                {
                    int count = squad != null ? squad.EnemyWingCount : 0;
                    if ((wingPage + 1) * WingRowsPerPage < count) wingPage++;
                    nextRefresh = 0f;
                }, AvButtonStyle.Quiet, AvIcon.ChevronRight));
            previousWings = pager.Controls[0];
            nextWings = pager.Controls[1];
            rosterPage = p.Add(new AvTextBlock(p.Content, AvTextRole.Label));
            rosterPage.Set("NO CONTACTS");

            for (int i = 0; i < WingRowsPerPage; i++)
                wingRows.Add(p.Add(new HostileWingCard(p.Content)));

            AvTextBlock footer = p.Add(new AvTextBlock(p.Content, AvTextRole.ProseSmall));
            footer.Set("ACE KILL: +1 SKILL POINT. Downed aces may return stronger. " +
                "Friendly wing recruiting and orders remain in WMC.");
        }

        // ---- WINGS refresh -------------------------------------------------------------------

        private void RefreshWingsPage()
        {
            if (huntRow == null) return;
            bool hunted = squad != null && squad.HuntActive;
            huntRow.Set(hunted ? "ACE HUNT ACTIVE — YOU ARE THE TARGET" : "ACE HUNT STANDBY",
                squad != null ? squad.Status : "Enemy wing reports are unavailable.", null,
                hunted ? AvState.Danger : AvState.Info);

            RefreshFriendlyWing();

            int count = Math.Max(0, squad != null ? squad.EnemyWingCount : 0);
            wingPage = Math.Min(wingPage, Math.Max(0, (count - 1) / WingRowsPerPage));
            int first = wingPage * WingRowsPerPage;
            int last = Math.Min(first + WingRowsPerPage, count);
            rosterPage.Set(count == 0 ? "NO HOSTILE WINGS ENCOUNTERED"
                : count == 1 ? "1 OF 1 WING"
                : AvNum.Thousands(first + 1) + "–" + AvNum.Thousands(last) + " OF " + AvNum.Thousands(count) + " WINGS");
            previousWings.Interactable = wingPage > 0;
            nextWings.Interactable = first + WingRowsPerPage < count;
            previousWings.Help = wingPage > 0
                ? "Show the previous two hostile wings." : "Already on the first page.";
            nextWings.Help = first + WingRowsPerPage < count
                ? "Show the next two hostile wings, including previous encounters."
                : "Already on the last page.";

            for (int i = 0; i < wingRows.Count; i++)
            {
                HostileWingCard row = wingRows[i];
                bool visible = first + i < count;
                row.Rect.gameObject.SetActive(visible);
                if (!visible) continue;

                EnemyWingView wing = squad.GetEnemyWing(first + i);
                row.Symbol.text = wing.Symbol;
                row.Wing.text = wing.WingName;
                row.Ace.text = "ACE  " + wing.AceName;
                row.Skill.text = "TIER " + AvNum.Thousands(wing.Tier) + " · SKILL " + wing.Skill +
                    (wing.Returns > 0 ? " · RETURN #" + AvNum.Thousands(wing.Returns) : " · FIRST ENCOUNTER");
                row.Status.text = wing.Status;
                row.Members.text = AvNum.Thousands(wing.MembersAlive) + " / " + AvNum.Thousands(wing.MemberCount) + " ALIVE";
                row.Target.text = wing.MembersAlive <= 0 ? "WING NO LONGER ACTIVE" :
                    string.IsNullOrEmpty(wing.TargetName) ? "TARGET: NORMAL OPERATIONS" : "TARGET: " + wing.TargetName;
                Color color = wing.MembersAlive <= 0 ? AvTheme.Dim :
                    !string.IsNullOrEmpty(wing.TargetName) ? AvTheme.Alert : AvTheme.RailInfo;
                row.Status.color = row.Symbol.color = color;
                row.Members.color = wing.MembersAlive <= 0 ? AvTheme.Dim : AvTheme.RailReady;
                row.Target.color = wing.MembersAlive <= 0 ? AvTheme.Dim : AvTheme.TextPrimary;

                int mask = wing.AbilityMask & AceSkillCatalog.MaskWindow;
                bool anySkill = false;
                for (int badge = 0; badge < row.Badges.Length; badge++)
                {
                    bool active = AceSkillCatalog.Has(mask, badge);
                    row.Badges[badge].gameObject.SetActive(active);
                    anySkill |= active;
                }
                row.NoSkills.gameObject.SetActive(!anySkill);

                string crestKey = (wing.Symbol ?? string.Empty) + "|" + (wing.WingName ?? string.Empty);
                if (row.CrestKey != crestKey)
                {
                    row.CrestKey = crestKey;
                    row.SetCrest(EmblemRenderer.Procedural(EmblemDesign.Hostile(crestKey)));
                }

                if (!string.Equals(wingPortraitKeys[i], wing.AceName, StringComparison.Ordinal))
                {
                    wingPortraitKeys[i] = wing.AceName;
                    string ace = wing.AceName ?? string.Empty;
                    int separator = ace.IndexOf(" / ", StringComparison.Ordinal);
                    string name = separator < 0 ? ace : ace.Substring(0, separator);
                    string handle = separator < 0 ? string.Empty : ace.Substring(separator + 3);
                    row.SetPortrait(WingLink.PilotPortrait(name, handle));
                }
            }
        }

        private void RefreshFriendlyWing()
        {
            if (wingLeadRow == null) return;
            PilotView pilot = squad != null ? squad.Pilot : default;
            bool profile = hasLocalProfile && !string.IsNullOrEmpty(localProfile.Callsign);
            string callsign = profile ? localProfile.Callsign : pilot.Callsign;
            string name = profile ? localProfile.Name : pilot.Name;

            Aircraft lead = null;
            if (GameManager.GetLocalPlayer<Player>(out Player local) && local != null) lead = local.Aircraft;
            wingLeadRow.Set(string.IsNullOrEmpty(callsign) ? "PILOT RECORD PENDING" : callsign,
                (string.IsNullOrEmpty(name) ? "—" : name) + "   ·   FLIGHT LEAD",
                lead == null ? "NO AIRCRAFT" : AirframeOf(lead) + " · " + AircraftStatus(lead),
                lead == null ? AvState.Inert : AvState.Ready);

            friendlyWing.Clear();
            int total = WingLink.WingCount;
            if (total > 0) WingLink.ResolveWingAircraft(friendlyWing);
            int flying = 0;
            for (int i = 0; i < friendlyWing.Count; i++)
            {
                Aircraft member = friendlyWing[i];
                if (member != null && !member.disabled && !member.HasEjected()) flying++;
            }

            wingCountRow.Set(total > 0 ? AvNum.Thousands(total) + (total == 1 ? " WINGMAN" : " WINGMEN") : "NO RECRUITED WING",
                total > 0 ? AvNum.Thousands(flying) + " AIRBORNE" : WingLink.Available ? "AWAITING RECRUITS" : "WMC OFFLINE",
                null, total > 0 ? AvState.Ready : AvState.Inert);
            wingTeamNote.Rect.gameObject.SetActive(total == 0);

            for (int i = 0; i < wingmanSlots.Length; i++)
            {
                AvRow slot = wingmanSlots[i];
                bool visible = i < friendlyWing.Count;
                slot.Rect.gameObject.SetActive(visible);
                if (!visible) continue;
                Aircraft aircraft = friendlyWing[i];
                AvState state = aircraft == null ? AvState.Inert
                    : aircraft.disabled || aircraft.HasEjected() ? AvState.Danger
                    : aircraft.IsLanded() ? AvState.Caution : AvState.Ready;
                slot.Set("W" + AvNum.Thousands(i + 1), AirframeOf(aircraft), AircraftStatus(aircraft), state);
            }
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

        /// <summary>One hostile ace wing: crest + ace portrait, identity, skill badges, target line.</summary>
        private sealed class HostileWingCard : AvPart
        {
            private readonly AvFrame frame;
            private readonly AvPortrait crest, portrait;
            public readonly TMP_Text Symbol, Wing, Ace, Skill, Status, Members, Target, NoSkills;
            public readonly TMP_Text[] Badges = new TMP_Text[AceSkillCatalog.MaximumSkills];
            public string CrestKey;

            public HostileWingCard(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "HostileWing");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                crest = new AvPortrait(Rect, "Crest", "");
                portrait = new AvPortrait(Rect, "Ace");
                Symbol = AvText.Make(Rect, "Symbol", AvTextRole.Head, "", TextAlignmentOptions.Center);
                Wing = AvText.Make(Rect, "Wing", AvTextRole.Label);
                Ace = AvText.Make(Rect, "Ace", AvTextRole.ProseSmall);
                Skill = AvText.Make(Rect, "Skill", AvTextRole.Micro);
                Status = AvText.Make(Rect, "Status", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                Members = AvText.Make(Rect, "Members", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                Target = AvText.Make(Rect, "Target", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
                NoSkills = AvText.Make(Rect, "NoSkills", AvTextRole.Micro, "NO ACTIVE THREAT SKILLS");
                for (int i = 0; i < Badges.Length; i++)
                    Badges[i] = AvText.Make(Rect, "Badge" + i, AvTextRole.Micro,
                        AceSkillCatalog.All[i].Code, TextAlignmentOptions.Center);
                Restyle();
            }

            public void SetCrest(Sprite sprite) => crest.Set(sprite);
            public void SetPortrait(Sprite sprite) => portrait.Set(sprite);

            public override float Measure(float width) => 124f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                crest.Place(new AvSlot(8f, 6f, 48f, 48f));
                portrait.Place(new AvSlot(60f, 6f, 36f, 48f));
                AvLay.Place(Symbol.rectTransform, 102f, 2f, 24f, 18f);
                AvLay.Place(Wing.rectTransform, 130f, 2f, s.W - 292f, 18f);
                AvLay.Place(Status.rectTransform, s.W - 156f, 2f, 148f, 16f);
                AvLay.Place(Ace.rectTransform, 102f, 22f, s.W - 258f, 15f);
                AvLay.Place(Members.rectTransform, s.W - 156f, 20f, 148f, 14f);
                AvLay.Place(Skill.rectTransform, 102f, 40f, s.W - 112f, 14f);
                float badgeW = 48f; // 4 codes (TOUGH/NOTCH/GHOST/...) at Micro caps need ~42 px
                for (int i = 0; i < Badges.Length; i++)
                    AvLay.Place(Badges[i].rectTransform, 102f + i * (badgeW + 4f), 58f, badgeW, 24f);
                AvLay.Place(NoSkills.rectTransform, 102f, 58f, s.W - 112f, 20f);
                AvLay.Place(Target.rectTransform, 102f, s.H - 20f, s.W - 112f, 16f);
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card inert");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                Wing.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
                Ace.color = Skill.color = NoSkills.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                foreach (TMP_Text badge in Badges)
                    badge.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                crest.Restyle();
                portrait.Restyle();
            }
        }
    }
}
