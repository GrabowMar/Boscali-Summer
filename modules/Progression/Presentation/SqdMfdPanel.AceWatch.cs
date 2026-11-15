using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>The enemy ace watch: the recognition board of known hostile ace wings, a section at the foot of the PILOT page.</summary>
    internal sealed partial class SqdMfdPanel
    {
        private AvSection hostileSection;
        private SqdEmptyCard hostileEmpty;
        private AvButtons wingsPager;
        private AvControl previousWings;
        private AvControl nextWings;
        private int wingPage;
        private readonly List<HostileWingCard> wingRows = new List<HostileWingCard>(WingRowsPerPage);
        private readonly string[] wingPortraitKeys = new string[WingRowsPerPage];
        private int aceSig;
        private bool aceSigSet;

        private void ResetWingsPage()
        {
            hostileSection = null;
            hostileEmpty = null;
            wingsPager = null;
            previousWings = nextWings = null;
            wingPage = 0;
            wingRows.Clear();
            Array.Clear(wingPortraitKeys, 0, wingPortraitKeys.Length);
            aceSigSet = false;
        }

        // ---- ACE WATCH section --------------------------------------------------------------

        private void BuildAceWatch(AvFlow p)
        {
            hostileSection = p.Section(AvIcon.Skull, "ACE WATCH", null);
            hostileEmpty = p.Add(new SqdEmptyCard(p.Content, AvIcon.Radar2, "NO ACE WINGS ON RECORD",
                "Aces are filed here as you meet them. An ace kill earns +1 skill point."));
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
                }, AvButtonStyle.Quiet, AvIcon.ChevronRight, true));
            previousWings = wingsPager.Controls[0];
            nextWings = wingsPager.Controls[1];
            previousWings.Help = "Show the previous two hostile wings.";
            nextWings.Help = "Show the next two hostile wings, including previous encounters.";
        }

        private void RefreshAceWatch()
        {
            if (hostileSection == null) return;
            bool hunted = squad != null && squad.HuntActive;
            int count = Math.Max(0, squad != null ? squad.EnemyWingCount : 0);
            wingPage = Math.Min(wingPage, Math.Max(0, (count - 1) / WingRowsPerPage));
            int first = wingPage * WingRowsPerPage;
            int last = Math.Min(first + WingRowsPerPage, count);

            // Nothing painted unless a number or a name behind the board moved.
            int sig = Mix(Mix(Mix(17, count), wingPage), hunted ? 1 : 0);
            for (int i = first; i < last; i++)
            {
                EnemyWingView w = squad.GetEnemyWing(i);
                sig = Mix(Mix(Mix(Mix(sig, w.Tier), w.MembersAlive), w.MemberCount), w.AbilityMask);
                sig = Mix(Mix(Mix(Mix(sig, w.Returns), w.Skill), w.Symbol), w.WingName);
                sig = Mix(Mix(Mix(sig, w.AceName), w.TargetName), w.Status);
            }
            if (aceSigSet && sig == aceSig) return;
            aceSigSet = true;
            aceSig = sig;

            hostileSection.SetCaption((hunted ? "HUNT ACTIVE · " : "") + (count == 0 ? "NO ACES FILED"
                : count == 1 ? "1 WING"
                : AvNum.Thousands(first + 1) + "–" + AvNum.Thousands(last) + " OF " + AvNum.Thousands(count) + " WINGS"));
            hostileEmpty.SetShown(count == 0);
            wingsPager.SetShown(count > WingRowsPerPage);
            previousWings.Interactable = wingPage > 0;
            nextWings.Interactable = first + WingRowsPerPage < count;

            int faction = PortraitFactions.OpposingLocal;
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

                string portraitKey = wing.AceName + "|" + faction;
                if (!string.Equals(wingPortraitKeys[i], portraitKey, StringComparison.Ordinal))
                {
                    wingPortraitKeys[i] = portraitKey;
                    string ace = wing.AceName ?? string.Empty;
                    int separator = ace.IndexOf(" / ", StringComparison.Ordinal);
                    string name = separator < 0 ? ace : ace.Substring(0, separator);
                    string handle = separator < 0 ? string.Empty : ace.Substring(separator + 3);
                    card.SetPortrait(WingLink.PersonnelPortrait(name, handle, PortraitRole.Pilot, faction));
                }
            }
        }

        /// <summary>
        /// One hostile ace wing as an intel card: crest + ace portrait, wing and ace identity, a
        /// threat meter (tier), the wing's remaining strength, active skill badges and the target.
        /// Every text has a fixed slot, so nothing can float or overlap.
        /// </summary>
        private sealed class HostileWingCard : AvPart
        {
            private const float CardH = 122f, LeftW = 54f, TextX = 72f, Pad = 10f;
            private const int ThreatPips = 5, TallyMax = 12;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly AvPortrait crest, portrait;
            private readonly TMP_Text symbol, wing, ace, skill, status, threatWord, members, target, noSkills;
            private readonly Image[] pips = new Image[ThreatPips];
            private readonly Image[] tally = new Image[TallyMax];
            private readonly SqdBar aliveBar;
            private int aliveCount, totalCount;
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
                threatWord = AvText.Make(Rect, "ThreatWord", AvTextRole.DataSmall);
                AvText.Fit(threatWord, false);
                for (int i = 0; i < pips.Length; i++) pips[i] = AvLay.Solid(Rect, "Pip" + i, Color.clear);
                members = AvText.Make(Rect, "Members", AvTextRole.DataSmall);
                AvText.Fit(members, false);
                aliveBar = new SqdBar(Rect, "Alive");
                for (int i = 0; i < tally.Length; i++) tally[i] = AvLay.Solid(Rect, "Tally" + i, Color.clear);
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
                aliveCount = Mathf.Max(0, alive);
                totalCount = Mathf.Max(0, total);
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
                crest.Place(new AvSlot(Pad + 2f, 8f, LeftW - 2f, 40f));
                portrait.Place(new AvSlot(Pad + 2f, 52f, LeftW - 2f, 62f));

                AvLay.Place(symbol.rectTransform, TextX, 6f, 18f, 20f);
                AvLay.Place(wing.rectTransform, TextX + 22f, 6f, tw - 22f - 120f, 20f);
                AvLay.Place(status.rectTransform, s.W - Pad - 116f, 6f, 116f, 20f);
                AvLay.Place(ace.rectTransform, TextX, 27f, tw, 16f);
                AvLay.Place(skill.rectTransform, TextX, 44f, tw, 14f);

                // Threat pips and the wing's strength share one line: pips + word at the left, count + bar at the right.
                const float rowY = 61f;
                for (int i = 0; i < pips.Length; i++)
                    AvLay.Place(pips[i].rectTransform, TextX + i * 17f, rowY + 5f, 14f, 6f);
                AvLay.Place(threatWord.rectTransform, TextX + pips.Length * 17f + 6f, rowY, 84f, 16f);
                float memberX = TextX + pips.Length * 17f + 96f;
                AvLay.Place(members.rectTransform, memberX, rowY, 92f, 16f);
                float stripX = memberX + 98f, stripW = Mathf.Max(20f, s.W - Pad - stripX);
                aliveBar.Place(stripX, rowY + 6f, stripW, 4f);
                aliveBar.Set(aliveFraction, SqdTone.Rail(state));
                PlaceTally(stripX, rowY, stripW);

                float badgeW = 54f; // 4 codes (TOUGH/NOTCH/GHOST/...) at Micro caps need ~42 px
                int shown = 0;
                for (int i = 0; i < badgeText.Length; i++)
                {
                    float x = TextX + shown * (badgeW + 4f);
                    AvLay.Place(badgeFrames[i].rectTransform, x, 80f, badgeW, 20f);
                    AvLay.Place(badgeText[i].rectTransform, x, 80f, badgeW, 20f);
                    if (AceSkillCatalog.Has(mask, i)) shown++;
                }
                AvLay.Place(noSkills.rectTransform, TextX, 80f, tw, 20f);
                AvLay.Place(target.rectTransform, TextX, 104f, tw, 14f);
            }

            /// <summary>
            /// The wing's muster as painted tally marks, one per member: a tall mark is still flying, a stub is gone.
            /// Past <see cref="TallyMax"/> members the strength bar says it instead.
            /// </summary>
            private void PlaceTally(float x, float y, float w)
            {
                bool marks = totalCount > 0 && totalCount <= TallyMax;
                aliveBar.SetShown(!marks);
                float pitch = marks ? Mathf.Min(14f, w / totalCount) : 0f, markW = Mathf.Max(3f, pitch - 3f);
                for (int i = 0; i < tally.Length; i++)
                {
                    bool shown = marks && i < totalCount;
                    tally[i].gameObject.SetActive(shown);
                    if (!shown) continue;
                    bool flying = i < aliveCount;
                    AvLay.Place(tally[i].rectTransform, x + i * pitch, y + (flying ? 3f : 10f), markW, flying ? 10f : 3f);
                }
            }

            public override void Restyle()
            {
                frame.PaintCard("card inert", AvTheme.SurfaceInert, AvTheme.Hairline);
                Color tone = SqdTone.Rail(state);
                for (int i = 0; i < tally.Length; i++) tally[i].color = i < aliveCount ? tone : AvTheme.Hairline;
                rail.color = tone;
                wing.color = AvInk.Ink;
                symbol.color = tone;
                ace.color = AvInk.Ink;
                skill.color = AvInk.Dim;
                status.color = SqdTone.Text(state);
                threatWord.color = SqdTone.Text(state);
                for (int i = 0; i < pips.Length; i++)
                    pips[i].color = i < threat && state != AvState.Inert ? tone : AvTheme.Hairline;
                members.color = AvInk.Ink;
                aliveBar.Restyle();
                aliveBar.Set(aliveFraction, tone);
                target.color = state == AvState.Danger ? SqdTone.Text(AvState.Danger) : AvInk.Dim;
                noSkills.color = AvInk.Muted;
                for (int i = 0; i < badgeText.Length; i++)
                {
                    badgeText[i].color = AvInk.Ink;
                    badgeFrames[i].Paint(tone.WithAlpha(.14f), tone.WithAlpha(.6f));
                }
                crest.Restyle();
                portrait.Restyle();
            }
        }
    }
}
