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
        private const int EnemyScanLimit = 64;

        private SqdRosterRow huntRow;
        private SqdRosterRow wingLeadRow;
        private SqdEmptyCard wingTeamNote;
        private SqdEmptyCard hostileEmpty;
        private AvSection friendlySection;
        private AvSection hostileSection;
        private AvStatTile wingsTotalTile, wingsActiveTile, wingsAliveTile;
        private readonly SqdRosterRow[] wingmanSlots = new SqdRosterRow[WingmanRows];
        private readonly List<Aircraft> friendlyWing = new List<Aircraft>(WingmanRows);

        private AvButtons wingsPager;
        private AvControl previousWings;
        private AvControl nextWings;
        private int wingPage;
        private readonly List<HostileWingCard> wingRows = new List<HostileWingCard>(WingRowsPerPage);
        private readonly string[] wingPortraitKeys = new string[WingRowsPerPage];

        private void ResetWingsPage()
        {
            huntRow = wingLeadRow = null;
            wingTeamNote = hostileEmpty = null;
            friendlySection = hostileSection = null;
            wingsTotalTile = wingsActiveTile = wingsAliveTile = null;
            Array.Clear(wingmanSlots, 0, wingmanSlots.Length);
            friendlyWing.Clear();
            wingsPager = null;
            previousWings = nextWings = null;
            wingPage = 0;
            wingRows.Clear();
            Array.Clear(wingPortraitKeys, 0, wingPortraitKeys.Length);
        }

        // ---- ACES page ---------------------------------------------------------------------

        private void BuildWingsPage(AvFlow p)
        {
            p.Section(AvIcon.Skull, "ACE INTELLIGENCE", "LIVE + ENCOUNTER DATA");

            huntRow = p.Add(new SqdRosterRow(p.Content));
            huntRow.Set("ACE HUNT STANDBY", "Awaiting enemy wing reports.", null, null, AvState.Info);

            wingsTotalTile = new AvStatTile(p.Content, "HOSTILE WINGS");
            wingsActiveTile = new AvStatTile(p.Content, "STILL ACTIVE");
            wingsAliveTile = new AvStatTile(p.Content, "ACES + ESCORTS");
            p.Row(wingsTotalTile, wingsActiveTile, wingsAliveTile);

            friendlySection = p.Section(AvIcon.UsersGroup, "FRIENDLY FLIGHT", "MANAGED IN WMC");
            wingLeadRow = p.Add(new SqdRosterRow(p.Content, true));
            wingLeadRow.Set("RECORD PENDING", null, null, null, AvState.Info);
            for (int i = 0; i < wingmanSlots.Length; i++)
                wingmanSlots[i] = p.Add(new SqdRosterRow(p.Content));
            wingTeamNote = p.Add(new SqdEmptyCard(p.Content, AvIcon.UsersGroup, "NO RECRUITED WINGMEN",
                "Recruit and task wingmen in Wing Command (WMC); they fly beside you and appear here."));

            hostileSection = p.Section(AvIcon.Skull, "HOSTILE ACES", "MOST RECENT CONTACTS");
            hostileEmpty = p.Add(new SqdEmptyCard(p.Content, AvIcon.Radar2, "NO HOSTILE ACES ON RECORD",
                "Damage hostile forces to attract an enemy ace wing. Each ace kill is worth +1 skill point, and downed aces may return stronger."));
            for (int i = 0; i < WingRowsPerPage; i++)
                wingRows.Add(p.Add(new HostileWingCard(p.Content)));

            wingsPager = p.Buttons(
                new AvControl.Spec("PREVIOUS", () => { wingPage = Math.Max(0, wingPage - 1); nextRefresh = 0f; },
                    AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec("NEXT", () =>
                {
                    int count = squad != null ? squad.EnemyWingCount : 0;
                    if ((wingPage + 1) * WingRowsPerPage < count) wingPage++;
                    nextRefresh = 0f;
                }, AvButtonStyle.Quiet, AvIcon.ChevronRight));
            previousWings = wingsPager.Controls[0];
            nextWings = wingsPager.Controls[1];
        }

        // ---- ACES refresh -------------------------------------------------------------------

        private void RefreshWingsPage()
        {
            if (huntRow == null) return;
            bool hunted = squad != null && squad.HuntActive;
            huntRow.Set(hunted ? "ACE HUNT ACTIVE — YOU ARE THE TARGET" : "ACE HUNT STANDBY",
                squad != null ? squad.Status : "Enemy wing reports are unavailable.", null, null,
                hunted ? AvState.Danger : AvState.Info);

            RefreshFriendlyWing();

            int count = Math.Max(0, squad != null ? squad.EnemyWingCount : 0);
            wingPage = Math.Min(wingPage, Math.Max(0, (count - 1) / WingRowsPerPage));
            int first = wingPage * WingRowsPerPage;
            int last = Math.Min(first + WingRowsPerPage, count);

            int active = 0, alive = 0;
            for (int i = 0, scan = Math.Min(count, EnemyScanLimit); i < scan; i++)
            {
                int members = squad.GetEnemyWing(i).MembersAlive;
                if (members <= 0) continue;
                active++;
                alive += members;
            }
            bool any = count > 0;
            wingsTotalTile.SetShown(any);
            wingsActiveTile.SetShown(any);
            wingsAliveTile.SetShown(any);
            wingsTotalTile.Set(AvNum.Thousands(count));
            wingsActiveTile.Set(AvNum.Thousands(active), active > 0 ? AvState.Caution : AvState.Inert);
            wingsAliveTile.Set(AvNum.Thousands(alive), alive > 0 ? AvState.Caution : AvState.Inert);

            hostileSection.SetCaption(count == 0 ? "NO CONTACTS"
                : count == 1 ? "1 OF 1 WING"
                : AvNum.Thousands(first + 1) + "–" + AvNum.Thousands(last) + " OF " + AvNum.Thousands(count) + " WINGS");
            hostileEmpty.SetShown(count == 0);
            wingsPager.SetShown(count > WingRowsPerPage);
            previousWings.Interactable = wingPage > 0;
            nextWings.Interactable = first + WingRowsPerPage < count;
            previousWings.Help = wingPage > 0
                ? "Show the previous two hostile wings." : "Already on the first page.";
            nextWings.Help = first + WingRowsPerPage < count
                ? "Show the next two hostile wings, including previous encounters."
                : "Already on the last page.";

            for (int i = 0; i < wingRows.Count; i++)
            {
                HostileWingCard card = wingRows[i];
                bool visible = first + i < count;
                card.SetShown(visible);
                if (!visible) continue;

                EnemyWingView wing = squad.GetEnemyWing(first + i);
                card.Set(wing.Symbol, wing.WingName, wing.AceName,
                    "TIER " + AvNum.Thousands(wing.Tier) + " · SKILL " + wing.Skill +
                        (wing.Returns > 0 ? " · RETURN #" + AvNum.Thousands(wing.Returns) : " · FIRST ENCOUNTER"),
                    wing.Status, wing.MembersAlive, wing.MemberCount, wing.TargetName, wing.Tier,
                    wing.AbilityMask & AceSkillCatalog.MaskWindow);

                string crestKey = (wing.Symbol ?? string.Empty) + "|" + (wing.WingName ?? string.Empty);
                if (card.CrestKey != crestKey)
                {
                    card.CrestKey = crestKey;
                    card.SetCrest(EmblemRenderer.Procedural(EmblemDesign.Hostile(crestKey)));
                }

                if (!string.Equals(wingPortraitKeys[i], wing.AceName, StringComparison.Ordinal))
                {
                    wingPortraitKeys[i] = wing.AceName;
                    string ace = wing.AceName ?? string.Empty;
                    int separator = ace.IndexOf(" / ", StringComparison.Ordinal);
                    string name = separator < 0 ? ace : ace.Substring(0, separator);
                    string handle = separator < 0 ? string.Empty : ace.Substring(separator + 3);
                    card.SetPortrait(WingLink.PilotPortrait(name, handle));
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
            wingLeadRow.Set(string.IsNullOrEmpty(callsign) ? "RECORD PENDING" : callsign,
                (string.IsNullOrEmpty(name) ? "—" : name) + "   ·   FLIGHT LEAD",
                lead == null ? null : AirframeOf(lead),
                lead == null ? "NO AIRCRAFT" : AircraftStatus(lead),
                lead == null ? AvState.Inert : AvState.Ready);
            wingLeadRow.SetThumb(PlayerPortrait(name, callsign));

            friendlyWing.Clear();
            int total = WingLink.WingCount;
            if (total > 0) WingLink.ResolveWingAircraft(friendlyWing);
            int flying = 0;
            for (int i = 0; i < friendlyWing.Count; i++)
            {
                Aircraft member = friendlyWing[i];
                if (member != null && !member.disabled && !member.HasEjected()) flying++;
            }

            friendlySection.SetCaption(total > 0
                ? AvNum.Thousands(total) + (total == 1 ? " WINGMAN" : " WINGMEN") + " · " + AvNum.Thousands(flying) + " AIRBORNE"
                : WingLink.Available ? "AWAITING RECRUITS" : "WMC OFFLINE");
            wingTeamNote.SetShown(total == 0);

            for (int i = 0; i < wingmanSlots.Length; i++)
            {
                SqdRosterRow slot = wingmanSlots[i];
                bool visible = i < friendlyWing.Count;
                slot.SetShown(visible);
                if (!visible) continue;
                Aircraft aircraft = friendlyWing[i];
                AvState state = aircraft == null ? AvState.Inert
                    : aircraft.disabled || aircraft.HasEjected() ? AvState.Danger
                    : aircraft.IsLanded() ? AvState.Caution : AvState.Ready;
                slot.Set("WINGMAN " + AvNum.Thousands(i + 1), AirframeOf(aircraft), AircraftStatus(aircraft), null, state);
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

        /// <summary>
        /// One hostile ace wing as an intel card: crest + ace portrait, wing and ace identity, a
        /// threat meter (tier), the wing's remaining strength, active skill badges and the target.
        /// Every text has a fixed slot, so nothing can float or overlap.
        /// </summary>
        private sealed class HostileWingCard : AvPart
        {
            private const float CardH = 156f, LeftW = 54f, TextX = 72f, Pad = 10f;
            private const int ThreatPips = 5;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly AvPortrait crest, portrait;
            private readonly TMP_Text symbol, wing, ace, skill, status, threatCaption, threatWord, members, target, noSkills;
            private readonly Image[] pips = new Image[ThreatPips];
            private readonly SqdBar aliveBar;
            private readonly AvFrame[] badgeFrames = new AvFrame[AceSkillCatalog.MaximumSkills];
            private readonly TMP_Text[] badgeText = new TMP_Text[AceSkillCatalog.MaximumSkills];
            private AvState state = AvState.Inert;
            private int threat, mask;
            private float aliveFraction;
            public string CrestKey;

            public HostileWingCard(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "HostileWing");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                crest = new AvPortrait(Rect, "Crest", "");
                portrait = new AvPortrait(Rect, "Ace");
                symbol = AvText.Make(Rect, "Symbol", AvTextRole.Head, "", TextAlignmentOptions.Center);
                wing = AvText.Make(Rect, "Wing", AvTextRole.Head);
                AvText.Fit(wing, false);
                ace = AvText.Make(Rect, "Ace", AvTextRole.ProseSmall);
                AvText.Fit(ace, false);
                skill = AvText.Make(Rect, "Skill", AvTextRole.Micro);
                AvText.Fit(skill, false);
                status = AvText.Make(Rect, "Status", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(status, false);
                threatCaption = AvText.Make(Rect, "ThreatCaption", AvTextRole.Micro, "THREAT");
                threatWord = AvText.Make(Rect, "ThreatWord", AvTextRole.DataSmall);
                AvText.Fit(threatWord, false);
                for (int i = 0; i < pips.Length; i++) pips[i] = AvLay.Solid(Rect, "Pip" + i, Color.clear);
                members = AvText.Make(Rect, "Members", AvTextRole.DataSmall);
                AvText.Fit(members, false);
                aliveBar = new SqdBar(Rect, "Alive");
                target = AvText.Make(Rect, "Target", AvTextRole.Micro);
                AvText.Fit(target, false);
                noSkills = AvText.Make(Rect, "NoSkills", AvTextRole.Micro, "NO ACTIVE THREAT SKILLS");
                AvText.Fit(noSkills, false);
                for (int i = 0; i < badgeText.Length; i++)
                {
                    badgeFrames[i] = AvFrame.Add(Rect, "BadgeFrame" + i, AvChamfer.Diagonal(3f));
                    badgeText[i] = AvText.Make(Rect, "Badge" + i, AvTextRole.Micro,
                        AceSkillCatalog.All[i].Code, TextAlignmentOptions.Center);
                    AvText.Fit(badgeText[i], false);
                }
                Restyle();
            }

            public void SetCrest(Sprite sprite) => crest.Set(sprite);
            public void SetPortrait(Sprite sprite) => portrait.Set(sprite);

            public void Set(string symbolText, string wingName, string aceName, string skillLine, string statusText,
                int alive, int total, string targetName, int tier, int abilityMask)
            {
                symbol.text = symbolText ?? "";
                wing.text = wingName ?? "";
                ace.text = "ACE  " + (aceName ?? "");
                skill.text = skillLine ?? "";
                status.text = statusText ?? "";
                members.text = AvNum.Thousands(alive) + " / " + AvNum.Thousands(total) + " ALIVE";
                aliveFraction = total <= 0 ? 0f : Mathf.Clamp01(alive / (float)total);
                target.text = alive <= 0 ? "WING NO LONGER ACTIVE"
                    : string.IsNullOrEmpty(targetName) ? "TARGET: NORMAL OPERATIONS" : "TARGET: " + targetName;
                threat = Mathf.Clamp(tier, 1, ThreatPips);
                threatWord.text = ThreatWordOf(threat);
                mask = abilityMask;
                bool anySkill = false;
                for (int i = 0; i < badgeText.Length; i++)
                {
                    bool active = AceSkillCatalog.Has(mask, i);
                    badgeText[i].gameObject.SetActive(active);
                    badgeFrames[i].gameObject.SetActive(active);
                    anySkill |= active;
                }
                noSkills.gameObject.SetActive(!anySkill);
                state = alive <= 0 ? AvState.Inert
                    : !string.IsNullOrEmpty(targetName) ? AvState.Danger : AvState.Caution;
                Restyle();
            }

            private static string ThreatWordOf(int level)
            {
                switch (level)
                {
                    case 1: return "LOW";
                    case 2: return "MODERATE";
                    case 3: return "HIGH";
                    case 4: return "SEVERE";
                    default: return "EXTREME";
                }
            }

            public override float Measure(float width) => CardH;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float tw = s.W - TextX - Pad;
                AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
                crest.Place(new AvSlot(Pad + 2f, Pad, LeftW - 2f, 44f));
                portrait.Place(new AvSlot(Pad + 2f, Pad + 50f, LeftW - 2f, 62f));

                AvLay.Place(symbol.rectTransform, TextX, 8f, 18f, 20f);
                AvLay.Place(wing.rectTransform, TextX + 22f, 8f, tw - 22f - 120f, 20f);
                AvLay.Place(status.rectTransform, s.W - Pad - 116f, 8f, 116f, 20f);
                AvLay.Place(ace.rectTransform, TextX, 30f, tw, 16f);
                AvLay.Place(skill.rectTransform, TextX, 48f, tw, 14f);

                AvLay.Place(threatCaption.rectTransform, TextX, 68f, 50f, 14f);
                for (int i = 0; i < pips.Length; i++)
                    AvLay.Place(pips[i].rectTransform, TextX + 54f + i * 17f, 72f, 14f, 6f);
                AvLay.Place(threatWord.rectTransform, TextX + 54f + pips.Length * 17f + 6f, 66f, 90f, 16f);

                AvLay.Place(members.rectTransform, TextX, 88f, 100f, 16f);
                aliveBar.Place(TextX + 108f, 94f, tw - 108f, 4f);
                aliveBar.Set(aliveFraction, SqdTone.Rail(state));

                float badgeW = 54f; // 4 codes (TOUGH/NOTCH/GHOST/...) at Micro caps need ~42 px
                int shown = 0;
                for (int i = 0; i < badgeText.Length; i++)
                {
                    float x = TextX + shown * (badgeW + 4f);
                    AvLay.Place(badgeFrames[i].rectTransform, x, 110f, badgeW, 20f);
                    AvLay.Place(badgeText[i].rectTransform, x, 110f, badgeW, 20f);
                    if (AceSkillCatalog.Has(mask, i)) shown++;
                }
                AvLay.Place(noSkills.rectTransform, TextX, 110f, tw, 20f);
                AvLay.Place(target.rectTransform, TextX, 134f, tw, 14f);
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card inert");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                Color tone = SqdTone.Rail(state);
                rail.color = tone;
                wing.color = SqdTone.Ink;
                symbol.color = tone;
                ace.color = SqdTone.Ink;
                skill.color = SqdTone.Dim;
                status.color = SqdTone.Text(state);
                threatCaption.color = SqdTone.Caption;
                threatWord.color = SqdTone.Text(state);
                for (int i = 0; i < pips.Length; i++)
                    pips[i].color = i < threat && state != AvState.Inert ? tone : AvTheme.Hairline;
                members.color = SqdTone.Ink;
                aliveBar.Restyle();
                aliveBar.Set(aliveFraction, tone);
                target.color = state == AvState.Danger ? SqdTone.Text(AvState.Danger) : SqdTone.Dim;
                noSkills.color = SqdTone.Caption;
                for (int i = 0; i < badgeText.Length; i++)
                {
                    badgeText[i].color = SqdTone.Ink;
                    badgeFrames[i].Paint(tone.WithAlpha(.14f), tone.WithAlpha(.6f));
                }
                crest.Restyle();
                portrait.Restyle();
            }
        }
    }
}
