using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>A state word in a 1 px outline over a faint wash of the state colour (the roster row's and the dossier's status word).
    /// Built from kit primitives; <see cref="Set"/> repaints only on change. (Kit gap: <see cref="AvChip"/> is header-only.)</summary>
    internal sealed class WingBadge
    {
        private readonly Image wash;
        private readonly AvFrame edge;
        private readonly TMP_Text text;
        private string word, rail = "inert";

        public WingBadge(RectTransform parent)
        {
            wash = AvLay.Solid(parent, "BadgeWash", Color.clear);
            edge = AvFrame.Add(parent, "BadgeEdge", default(AvChamfer));
            edge.Fill = false;
            text = AvText.Make(parent, "Badge", AvTextRole.Micro, "", TextAlignmentOptions.Center);
            AvText.Fit(text, false);
            Paint();
        }

        public string Word => word;

        public void Place(float x, float y, float w, float h)
        {
            AvLay.Place(wash.rectTransform, x, y, w, h);
            AvLay.Place(edge.rectTransform, x, y, w, h);
            AvLay.Place(text.rectTransform, x + 2f, y, w - 4f, h);
        }

        public void Set(string newWord, string railClass)
        {
            if (newWord == word && railClass == rail) return;
            word = newWord;
            rail = railClass ?? "inert";
            text.text = newWord ?? "";
            Paint();
        }

        public void Restyle() => Paint();

        private void Paint()
        {
            Color c = WmcState.Color(rail);
            text.color = c;
            wash.color = c.WithAlpha(0.08f);
            edge.Paint(Color.clear, c.WithAlpha(0.7f));
        }
    }

    /// <summary>WING's rank letters in the 0.9 colours; the words, not the colour, carry the state.</summary>
    internal static class WingRankColor
    {
        public static Color Of(WingRank r) =>
            r == WingRank.Legend ? WmcState.Color("warn") : r == WingRank.Ace ? WmcState.Color("live")
            : r == WingRank.Veteran ? WmcState.Color("info") : r == WingRank.Wingman ? Ink : Dim;

        public static Color Ink => AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);

        public static Color Dim => AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
    }

    /// <summary>One pilot on the roster table: rank insignia (drawn chevrons), callsign, name, kills, sorties and the status tag (word and
    /// colour); the whole row is a click target that opens the personnel file. The selected row wears the kit's armed row style and the
    /// state rail.</summary>
    internal sealed class WingPilotRow : AvPart
    {
        public const float H = 28f;
        private const float PadX = 8f, TagW = 118f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly AvVector insignia;
        private readonly TMP_Text callsign, name, kills, sorties;
        private readonly WingBadge state;
        private bool hover, selected;
        private string railClass = "inert";
        private WingRank rank;
        private float height = H;

        public WingPilot Pilot;

        public WingPilotRow(RectTransform parent, int index, Action<int> click)
        {
            Rect = AvLay.Child(parent, "PilotRow" + index);
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            AvHit hit = AvHit.On(frame);
            hit.Hover = h => { hover = h; Restyle(); };
            hit.Click = e => { if (e.button == PointerEventData.InputButton.Left) click(index); };
            AvHelpTip.Attach(frame.gameObject, SquadronWords.RowTip);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            insignia = AvVector.Create(Rect, "Rank", 16);
            callsign = AvText.Make(Rect, "Callsign", AvTextRole.Label);
            AvText.Fit(callsign, false);
            name = AvText.Make(Rect, "Name", AvTextRole.ProseSmall);
            AvText.Fit(name, false);
            kills = AvText.Make(Rect, "Kills", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            AvText.Fit(kills, false);
            sorties = AvText.Make(Rect, "Sorties", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            AvText.Fit(sorties, false);
            state = new WingBadge(Rect);
            Restyle();
        }

        public WingRank Rank => rank;

        public string KillsText => kills.text;

        public string SortiesText => sorties.text;

        public void SetIdentity(WingRank r, string callsignText, string nameText, int killCount, int sortieCount)
        {
            rank = r;
            WmcKit.Set(callsign, callsignText);
            WmcKit.Set(name, nameText);
            WmcKit.Set(kills, AvNum.Fixed(killCount, 0));
            WmcKit.Set(sorties, AvNum.Fixed(sortieCount, 0));
            DrawRank();
        }

        public void SetState(string word, string railClassName)
        {
            state.Set(word, railClassName);
            if (railClass != railClassName)
            {
                railClass = railClassName;
                Restyle();
            }
        }

        public void SetSelected(bool on)
        {
            if (selected == on) return;
            selected = on;
            Restyle();
        }

        public override float Measure(float width) => H;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            height = s.H;
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(callsign.rectTransform, PadX + 30f, 0f, 108f, s.H);
            AvLay.Place(name.rectTransform, PadX + 142f, 0f, 118f, s.H);
            AvLay.Place(kills.rectTransform, PadX + 262f, 0f, 24f, s.H);
            AvLay.Place(sorties.rectTransform, PadX + 290f, 0f, 24f, s.H);
            state.Place(s.W - TagW - 6f, (s.H - 18f) * 0.5f, TagW, 18f);
            DrawRank();
        }

        private void DrawRank()
        {
            insignia.Buffer.Clear();
            new WingInk(insignia.Buffer, height).Rank(PadX + 12f, height * 0.5f, 16f, rank, WingRankColor.Of(rank));
            insignia.Commit();
        }

        public override void Restyle()
        {
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(WmcState.Of(railClass)), selected ? "armed" : hover ? "hover" : null);
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            rail.color = WmcState.Color(railClass);
            callsign.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            name.color = WingRankColor.Dim;
            kills.color = WingRankColor.Ink;
            sorties.color = WingRankColor.Dim;
            state.Restyle();
            DrawRank();
        }
    }

    /// <summary>The roster table: a column header line, a page of pilot rows, or the empty card; only what shows takes room.</summary>
    internal sealed class WingPilotList : AvPart
    {
        private const float Gap = 2f, HeadH = 16f, PadX = 8f;
        private readonly WingPilotRow[] rows;
        private readonly AvRow empty;
        private readonly TMP_Text[] heads = new TMP_Text[5];
        private int shown;
        private bool none;

        public WingPilotList(RectTransform parent, int perPage, Action<int> click, WmcControls ids)
        {
            Rect = AvLay.Child(parent, "PilotList");
            string[] labels = { "CALLSIGN", "NAME", "K", "SRT", "STATUS" };
            for (int i = 0; i < heads.Length; i++)
            {
                heads[i] = AvText.Make(Rect, "Head" + labels[i], AvTextRole.Micro, labels[i], i == 2 || i == 3 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
                AvText.Fit(heads[i], false);
            }
            rows = new WingPilotRow[perPage];
            for (int i = 0; i < perPage; i++)
            {
                rows[i] = new WingPilotRow(Rect, i, click);
                rows[i].Rect.gameObject.SetActive(false);
                ids.Add("wing.pilot" + i, rows[i]);
            }
            empty = new AvRow(Rect);
            empty.Set("", "", "", AvState.Inert);
            empty.Rect.gameObject.SetActive(false);
            Restyle();
        }

        public int Count => rows.Length;

        public WingPilotRow this[int i] => rows[i];

        public bool Empty => none;

        /// <summary>The first <paramref name="n"/> rows show (or the empty card, with <paramref name="text"/>). True when that changed.</summary>
        public bool Show(int n, bool emptyCard, string text)
        {
            bool changed = n != shown || emptyCard != none;
            shown = emptyCard ? 0 : Mathf.Clamp(n, 0, rows.Length);
            none = emptyCard;
            for (int i = 0; i < rows.Length; i++)
            {
                bool on = i < shown;
                if (rows[i].Rect.gameObject.activeSelf != on) rows[i].Rect.gameObject.SetActive(on);
                if (!on) rows[i].Pilot = null;
            }
            foreach (TMP_Text h in heads)
                if (h.gameObject.activeSelf == none) h.gameObject.SetActive(!none);
            if (empty.Rect.gameObject.activeSelf != none) empty.Rect.gameObject.SetActive(none);
            if (none) empty.Set(text, "", "", AvState.Inert);
            return changed;
        }

        public override float Measure(float width)
        {
            if (none) return empty.Measure(width);
            float h = HeadH + shown * (WingPilotRow.H + Gap) - Gap;
            return Mathf.Max(0f, h);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (none)
            {
                empty.Place(new AvSlot(0f, 0f, s.W, s.H));
                return;
            }
            AvLay.Place(heads[0].rectTransform, PadX + 30f, 0f, 108f, HeadH);
            AvLay.Place(heads[1].rectTransform, PadX + 142f, 0f, 118f, HeadH);
            AvLay.Place(heads[2].rectTransform, PadX + 262f, 0f, 24f, HeadH);
            AvLay.Place(heads[3].rectTransform, PadX + 290f, 0f, 24f, HeadH);
            AvLay.Place(heads[4].rectTransform, s.W - 118f - 6f, 0f, 118f, HeadH);
            for (int i = 0; i < shown; i++) rows[i].Place(new AvSlot(0f, HeadH + i * (WingPilotRow.H + Gap), s.W, WingPilotRow.H));
        }

        public override void Restyle()
        {
            foreach (TMP_Text h in heads) h.color = WingRankColor.Dim;
            foreach (WingPilotRow r in rows) r.Restyle();
            empty.Restyle();
        }
    }

    /// <summary>‹ 1 / 2 › and RECRUIT and STUDIO › on one line under the roster.</summary>
    internal sealed class WingRosterFoot : AvPart
    {
        private const float ArrowW = 36f, PagerW = 210f, RecruitW = 116f;
        private readonly AvControl prev, next;
        private readonly TMP_Text label;
        private int page = -1, pages = -1;
        private bool enabled = true;
        private string why;

        public readonly AvControl Recruit, Studio;

        public WingRosterFoot(RectTransform parent, WmcControls ids, Action<int> turn, Action recruit, Action studio)
        {
            Rect = AvLay.Child(parent, "RosterFoot");
            prev = AvControl.Make(Rect, new AvControl.Spec("", () => turn(-1), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            next = AvControl.Make(Rect, new AvControl.Spec("", () => turn(1), AvButtonStyle.Quiet, AvIcon.ChevronRight));
            label = AvText.Make(Rect, "Pages", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
            Recruit = AvControl.Make(Rect, new AvControl.Spec("RECRUIT", recruit, AvButtonStyle.Default, AvIcon.Plus));
            Studio = AvControl.Make(Rect, new AvControl.Spec("STUDIO", studio, AvButtonStyle.Quiet, AvIcon.Pencil));
            ids.Add("wing.pilots.prev", prev);
            ids.Add("wing.pilots.next", next);
            ids.Add("wing.recruit", Recruit);
            ids.Add("wing.studio", Studio);
            Recruit.Help = SquadronWords.RecruitTip;
            Studio.Help = SquadronWords.StudioTip;
            Set(0, 1);
            Restyle();
        }

        public void Set(int newPage, int newPages)
        {
            if (newPage == page && newPages == pages) return;
            page = newPage;
            pages = newPages;
            WmcKit.Set(label, Pages.Label(page, pages));
            Apply();
        }

        /// <summary>Disables both arrows with <paramref name="reason"/> until enabled again (page changes keep it).</summary>
        public void SetEnabled(bool on, string reason)
        {
            if (on == enabled && reason == why) return;
            enabled = on;
            why = reason;
            Apply();
        }

        private void Apply()
        {
            prev.Interactable = enabled && page > 0;
            next.Interactable = enabled && page < pages - 1;
            prev.Help = enabled ? "Previous page" : why;
            next.Help = enabled ? "Next page" : why;
        }

        public override float Measure(float width) => AvGridTokens.Row;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(prev.Rect, 0f, 0f, ArrowW, s.H);
            AvLay.Place(label.rectTransform, ArrowW + 2f, 0f, PagerW - 2f * ArrowW - 4f, s.H);
            AvLay.Place(next.Rect, PagerW - ArrowW, 0f, ArrowW, s.H);
            AvLay.Place(Recruit.Rect, PagerW + 6f, 0f, RecruitW, s.H);
            float sx = PagerW + 6f + RecruitW + AvGridTokens.Gap;
            AvLay.Place(Studio.Rect, sx, 0f, Mathf.Max(60f, s.W - sx), s.H);
        }

        public override void Restyle()
        {
            label.color = WingRankColor.Dim;
            prev.Restyle();
            next.Restyle();
            Recruit.Restyle();
            Studio.Restyle();
        }
    }

    /// <summary>One PERKS badge: a star (earned) or a lock (not yet), the perk's name, a short line, and for a perk the game does not wire in
    /// 1.0 its own warn-coloured word; hovering shows the whole description in the footer. Locked ones name the XP that earns them.</summary>
    internal sealed class WingPerkCard : AvPart
    {
        private const float PadX = 8f, PadY = 6f, MinH = 46f, IconW = 20f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text badge, title, line, mark;
        private bool locked, inactive;
        private string railClass = "inert";

        public WingPerkCard(RectTransform parent, int index)
        {
            Rect = AvLay.Child(parent, "Perk" + index);
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            frame.raycastTarget = true;
            AvHelpTip.Attach(frame.gameObject, "");
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            badge = AvIcons.Make(Rect, AvIcon.Lock, 14f, WingRankColor.Dim);
            title = AvText.Make(Rect, "Title", AvTextRole.Label);
            AvText.Fit(title, false);
            line = AvText.Make(Rect, "Line", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
            mark = AvText.Make(Rect, "Mark", AvTextRole.Micro);
            AvText.Fit(mark, false);
            Restyle();
        }

        public string TitleText => title.text;

        public string LineText => line.text;

        public string MarkText => mark.text;

        public bool IsLocked => locked;

        /// <summary>Paints the badge; true when its wrapped line changed (the caller relayouts).</summary>
        public bool Set(PerkCard card, bool off)
        {
            bool grew = line.text != card.Line;
            WmcKit.Set(title, card.Title);
            // The not-active word is the mark; an active perk keeps its line.
            WmcKit.Set(line, card.Inactive ? "" : PerkCards.Badge(card.Line, 46));
            WmcKit.Set(mark, card.Inactive ? "NOT ACTIVE IN 1.0" : off && card.Owned ? "PROGRESSION OFF" : "");
            AvIcons.Set(badge, card.Locked ? AvIcon.Lock : AvIcon.Star, 14f);
            locked = card.Locked;
            inactive = card.Inactive || off;
            railClass = card.Locked ? "inert" : inactive ? "warn" : "live";
            AvHelpTip.Attach(frame.gameObject, card.Tip);
            Restyle();
            return grew;
        }

        private float TextW(float width) => width - PadX - IconW - 6f;

        public override float Measure(float width) =>
            Mathf.Max(MinH, PadY + 16f + (line.text.Length > 0 ? AvText.Height(line, TextW(width)) + 1f : 0f) + (mark.text.Length > 0 ? 16f : 0f) + PadY);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = TextW(s.W);
            float y = PadY - 2f;
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(badge.rectTransform, PadX, PadY - 1f, 16f, 16f);
            AvLay.Place(title.rectTransform, PadX + IconW, y, w, 16f);
            y += 16f;
            if (line.text.Length > 0)
            {
                float lh = AvText.Height(line, w);
                AvLay.Place(line.rectTransform, PadX + IconW, y, w, lh);
                y += lh + 1f;
            }
            AvLay.Place(mark.rectTransform, PadX + IconW, y, w, 16f);
        }

        public override void Restyle()
        {
            AvStyle r = AvStyleHost.FuiStyle("row");
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            rail.color = WmcState.Color(railClass);
            badge.color = locked ? WingRankColor.Dim : inactive ? WmcState.Color("warn") : WmcState.Color("live");
            title.color = locked ? WingRankColor.Dim : WingRankColor.Ink;
            line.color = WingRankColor.Dim;
            mark.color = WmcState.Color("warn");
        }
    }

    /// <summary>ASSIGNED: where the file's pilot is (the airframe with its icon, the slot line that may wrap); the SAR and RELEASE buttons are
    /// the card's action row below, so this is words only.</summary>
    internal sealed class WingAssignLine : AvPart
    {
        private const float PadX = 8f, KeyW = 70f, MinH = 30f;
        private readonly AvFrame frame;
        private readonly Image rail, icon;
        private readonly TMP_Text key, name, slot;
        private string railClass = "inert";

        public WingAssignLine(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "AssignLine");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            icon = AvLay.Solid(Rect, "Icon", Color.white);
            icon.preserveAspect = true;
            icon.enabled = false;
            key = AvText.Make(Rect, "Key", AvTextRole.Micro, "ASSIGNED");
            AvText.Fit(key, false);
            name = AvText.Make(Rect, "Name", AvTextRole.Label);
            AvText.Fit(name, false);
            slot = AvText.Make(Rect, "Slot", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public string NameText => name.text;

        public string SlotText => slot.text;

        /// <summary>Sets the words; true when the slot line changed (the caller relayouts).</summary>
        public bool SetText(string nameText, string slotText)
        {
            bool grew = slot.text != slotText;
            WmcKit.Set(name, nameText);
            WmcKit.Set(slot, slotText);
            return grew;
        }

        public void SetRail(string railClassName)
        {
            if (railClass == railClassName) return;
            railClass = railClassName;
            rail.color = WmcState.Color(railClass);
        }

        public void SetIcon(Sprite sprite)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        private float TextW(float width) => width - PadX - KeyW - 6f;

        public override float Measure(float width) => Mathf.Max(MinH, 6f + 15f + AvText.Height(slot, TextW(width)) + 6f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = TextW(s.W);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(key.rectTransform, PadX, 5f, KeyW, 16f);
            AvLay.Place(icon.rectTransform, PadX, 20f, 22f, 22f);
            AvLay.Place(name.rectTransform, PadX + KeyW, 4f, w, 16f);
            AvLay.Place(slot.rectTransform, PadX + KeyW, 20f, w, AvText.Height(slot, w));
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            rail.color = WmcState.Color(railClass);
            key.color = WmcState.Color("info");
            name.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            slot.color = WingRankColor.Dim;
        }
    }

    /// <summary>INSPECT's member chips (one per aircraft, in slot order) and CENTER.</summary>
    internal sealed class InspectChips : AvPart
    {
        private const float ChipW = 40f, ChipH = 28f;
        private readonly AvControl[] chips;

        public readonly AvControl Center;

        public InspectChips(RectTransform parent, WmcControls ids, int count, Action<int> pick, Action center)
        {
            Rect = AvLay.Child(parent, "InspectChips");
            chips = new AvControl[count];
            for (int i = 0; i < count; i++)
            {
                int k = i;
                chips[i] = AvControl.Make(Rect, new AvControl.Spec("", () => pick(k)));
                chips[i].gameObject.SetActive(false);
                ids.Add("insp.m" + i, chips[i]);
            }
            Center = AvControl.Make(Rect, new AvControl.Spec("CENTER", center, AvButtonStyle.Default, AvIcon.CurrentLocation));
            Center.Help = "Centre the map on this aircraft (the view stays there until you move it or press FIT).";
            ids.Add("insp.center", Center);
            Restyle();
        }

        public AvControl this[int i] => chips[i];

        public int Count => chips.Length;

        public override float Measure(float width) => AvGridTokens.Row;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = (s.H - ChipH) * 0.5f;
            for (int i = 0; i < chips.Length; i++) AvLay.Place(chips[i].Rect, i * (ChipW + 3f), y, ChipW, ChipH);
            AvLay.Place(Center.Rect, s.W - 96f, y, 96f, ChipH);
        }

        public override void Restyle()
        {
            foreach (AvControl c in chips) c.Restyle();
            Center.Restyle();
        }
    }
}
