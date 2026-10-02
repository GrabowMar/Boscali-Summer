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

    /// <summary>One pilot on the roster (rank letter, callsign, name, kills and the status word); the whole row is a click target that
    /// opens the dossier. The selected row wears the kit's armed row style and the state rail.</summary>
    internal sealed class WingPilotRow : AvPart
    {
        public const float H = 30f;
        private const float PadX = 8f, StateW = 96f;
        private readonly AvFrame frame;
        private readonly Image rail, rankBox;
        private readonly TMP_Text letter, callsign, name, kills;
        private readonly WingBadge state;
        private bool hover, selected;
        private string railClass = "inert";
        private Color rank = Color.white;

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
            rankBox = AvLay.Solid(Rect, "RankBox", Color.clear);
            letter = AvText.Make(Rect, "Letter", AvTextRole.Label, "", TextAlignmentOptions.Center);
            callsign = AvText.Make(Rect, "Callsign", AvTextRole.Label);
            AvText.Fit(callsign, false);
            name = AvText.Make(Rect, "Name", AvTextRole.ProseSmall);
            AvText.Fit(name, false);
            kills = AvText.Make(Rect, "Kills", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            state = new WingBadge(Rect);
            Restyle();
        }

        public void SetIdentity(string rankLetter, WingRank r, string callsignText, string nameText, string killsText)
        {
            WmcKit.Set(letter, rankLetter);
            rank = WingRankColor.Of(r);
            WmcKit.Set(callsign, callsignText);
            WmcKit.Set(name, nameText);
            WmcKit.Set(kills, killsText);
            letter.color = rank;
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
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(rankBox.rectTransform, PadX + 2f, 5f, 20f, 20f);
            AvLay.Place(letter.rectTransform, PadX + 2f, 5f, 20f, 20f);
            AvLay.Place(callsign.rectTransform, PadX + 30f, 0f, 132f, s.H);
            AvLay.Place(name.rectTransform, PadX + 166f, 0f, 78f, s.H);
            AvLay.Place(kills.rectTransform, PadX + 246f, 0f, 34f, s.H);
            state.Place(s.W - StateW - 6f, (s.H - 18f) * 0.5f, StateW, 18f);
        }

        public override void Restyle()
        {
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(WmcState.Of(railClass)), selected ? "armed" : hover ? "hover" : null);
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            rail.color = WmcState.Color(railClass);
            rankBox.color = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
            letter.color = rank;
            callsign.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            name.color = WingRankColor.Dim;
            kills.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Disabled);
            state.Restyle();
        }
    }

    /// <summary>The roster's pilot lines (a page of them), or the empty card; only what shows takes room.</summary>
    internal sealed class WingPilotList : AvPart
    {
        private const float Gap = 2f;
        private readonly WingPilotRow[] rows;
        private readonly AvRow empty;
        private int shown;
        private bool none;

        public WingPilotList(RectTransform parent, int perPage, Action<int> click, WmcControls ids)
        {
            Rect = AvLay.Child(parent, "PilotList");
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
            if (empty.Rect.gameObject.activeSelf != none) empty.Rect.gameObject.SetActive(none);
            if (none) empty.Set(text, "", "", AvState.Inert);
            return changed;
        }

        public override float Measure(float width)
        {
            if (none) return empty.Measure(width);
            float h = shown * (WingPilotRow.H + Gap) - Gap;
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
            for (int i = 0; i < shown; i++) rows[i].Place(new AvSlot(0f, i * (WingPilotRow.H + Gap), s.W, WingPilotRow.H));
        }

        public override void Restyle()
        {
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

    /// <summary>The dossier of one pilot, built into an <see cref="AvCard"/>'s flow: portrait (an image), identity and the status stamp,
    /// rank line, XP tape with its rank ticks and letters, record, radio and RELEASE.</summary>
    internal sealed class WingDossier : AvPart
    {
        public const float H = 100f;
        private const float PortraitW = 64f, PortraitH = 88f, TextX = 76f, StampW = 94f;
        private readonly Image rail, portrait, track, fill;
        private readonly AvFrame portraitBox;
        private readonly Image[] ticks = new Image[4];
        private readonly TMP_Text identity, rankLine, record, radio;
        private readonly TMP_Text[] letters = new TMP_Text[5];
        private readonly Color[] letterColors = new Color[5];
        private readonly bool[] letterLit = new bool[5];
        private readonly WingBadge stamp;
        private string railClass = "inert";
        private float fraction;
        private bool dim;
        private WingPilot shown;
        private int look = int.MinValue;
        private bool portraitSet;
        private float width = 300f;

        public readonly AvControl Release;

        public WingDossier(RectTransform parent, Action release)
        {
            Rect = AvLay.Child(parent, "Dossier");
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            portraitBox = AvFrame.Add(Rect, "PortraitBox", default(AvChamfer));
            portrait = AvLay.Solid(Rect, "Portrait", Color.white);
            portrait.preserveAspect = true;
            identity = AvText.Make(Rect, "Identity", AvTextRole.Label);
            AvText.Fit(identity, false);
            stamp = new WingBadge(Rect);
            rankLine = AvText.Make(Rect, "RankLine", AvTextRole.ProseSmall);
            AvText.Fit(rankLine, false);
            track = AvLay.Solid(Rect, "XpTrack", Color.clear);
            fill = AvLay.Solid(Rect, "XpFill", Color.clear);
            for (int i = 0; i < ticks.Length; i++) ticks[i] = AvLay.Solid(Rect, "Tick" + i, Color.clear);
            for (int i = 0; i < letters.Length; i++)
            {
                letters[i] = AvText.Make(Rect, "Rank" + i, AvTextRole.Micro, "", TextAlignmentOptions.Center);
                letterColors[i] = WingRankColor.Dim;
            }
            record = AvText.Make(Rect, "Record", AvTextRole.ProseSmall);
            AvText.Fit(record, false);
            radio = AvText.Make(Rect, "Radio", AvTextRole.ProseSmall);
            AvText.Fit(radio, false);
            Release = AvControl.Make(Rect, new AvControl.Spec("RELEASE", release));
            Release.Help = SquadronWords.ReleaseTip;
            Restyle();
        }

        public string StampWord => stamp.Word;

        public string RankLine => rankLine.text;

        public void SetPilot(WingPilot pilot)
        {
            if (portraitSet && ReferenceEquals(pilot, shown) && look == WingPilotRoster.LookVersion) return;
            portraitSet = true;
            shown = pilot;
            look = WingPilotRoster.LookVersion;
            portrait.sprite = PilotPortrait.For(pilot);
            portrait.enabled = portrait.sprite != null;
            portrait.color = pilot != null ? Color.white : Color.white.WithAlpha(0.3f);
        }

        public void SetIdentity(string text) => WmcKit.Set(identity, text);

        public void SetStamp(string word, string railClassName) => stamp.Set(word, railClassName);

        public void SetRail(string railClassName)
        {
            if (railClass == railClassName) return;
            railClass = railClassName;
            rail.color = WmcState.Color(railClass);
        }

        public void SetRankLine(string text) => WmcKit.Set(rankLine, text);

        public void SetXp(float xpFraction, bool dimFill)
        {
            fraction = Mathf.Clamp01(float.IsNaN(xpFraction) ? 0f : xpFraction);
            dim = dimFill;
            PlaceFill();
            fill.color = dim ? WingRankColor.Dim : AvStyleHost.FuiColor("friendly", AvTheme.Friendly);
        }

        public void SetLetter(int i, string text, WingRank rank, bool lit)
        {
            WmcKit.Set(letters[i], text);
            letterLit[i] = lit;
            letterColors[i] = lit ? WingRankColor.Of(rank) : WingRankColor.Dim;
            letters[i].color = letterColors[i];
        }

        public void SetRecord(string text) => WmcKit.Set(record, text);

        public void SetRadio(string text) => WmcKit.Set(radio, text);

        public override float Measure(float w) => H;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place(rail.rectTransform, -10f, 0f, 3f, s.H);
            AvLay.Place(portraitBox.rectTransform, 0f, 4f, PortraitW, PortraitH);
            AvLay.Place(portrait.rectTransform, 1f, 5f, PortraitW - 2f, PortraitH - 2f);
            float textW = s.W - TextX;
            AvLay.Place(identity.rectTransform, TextX, 2f, textW - StampW - 6f, 18f);
            stamp.Place(s.W - StampW, 2f, StampW, 18f);
            AvLay.Place(rankLine.rectTransform, TextX, 22f, textW, 16f);
            AvLay.Place(track.rectTransform, TextX, 44f, textW, 6f);
            for (int i = 0; i < ticks.Length; i++)
                AvLay.Place(ticks[i].rectTransform, TextX + textW * PilotXp.Tick(i + 1), 42f, 1f, 10f);
            float cell = textW / letters.Length;
            for (int i = 0; i < letters.Length; i++) AvLay.Place(letters[i].rectTransform, TextX + i * cell, 52f, cell, 14f);
            AvLay.Place(record.rectTransform, TextX, 68f, textW - StampW - 6f, 16f);
            AvLay.Place(radio.rectTransform, TextX, 84f, textW - StampW - 6f, 16f);
            AvLay.Place(Release.Rect, s.W - StampW, 68f, StampW, 26f);
            PlaceFill();
        }

        private void PlaceFill()
        {
            float textW = Mathf.Max(0f, width - TextX);
            AvLay.Place(fill.rectTransform, TextX + 1f, 45f, Mathf.Max(0f, (textW - 2f) * fraction), 4f);
        }

        public override void Restyle()
        {
            rail.color = WmcState.Color(railClass);
            portraitBox.Paint(AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert), AvStyleHost.FuiColor("frame", AvTheme.Frame));
            identity.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            rankLine.color = WingRankColor.Dim;
            record.color = WingRankColor.Dim;
            radio.color = WingRankColor.Dim;
            track.color = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
            fill.color = dim ? WingRankColor.Dim : AvStyleHost.FuiColor("friendly", AvTheme.Friendly);
            Color hairline = AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
            foreach (Image t in ticks) t.color = hairline;
            for (int i = 0; i < letters.Length; i++) letters[i].color = letterLit[i] ? letterColors[i] : WingRankColor.Dim;
            stamp.Restyle();
            Release.Restyle();
        }
    }

    /// <summary>One PERKS card: a state rail, the perk's name and a wrapped line; hovering shows the whole description in the footer.</summary>
    internal sealed class WingPerkCard : AvPart
    {
        private const float PadX = 10f, PadY = 6f, MinH = 48f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text title, line;
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
            title = AvText.Make(Rect, "Title", AvTextRole.Label);
            AvText.Fit(title, false);
            line = AvText.Make(Rect, "Line", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public string TitleText => title.text;

        /// <summary>Paints the card; true when its wrapped line changed (the caller relayouts).</summary>
        public bool Set(PerkCard card, bool off)
        {
            bool grew = line.text != card.Line;
            WmcKit.Set(title, card.Title);
            WmcKit.Set(line, card.Line);
            locked = card.Locked;
            inactive = card.Inactive;
            railClass = card.Locked ? "inert" : card.Inactive || off ? "warn" : "live";
            AvHelpTip.Attach(frame.gameObject, card.Tip);
            Restyle();
            return grew;
        }

        public override float Measure(float width) =>
            Mathf.Max(MinH, PadY + 16f + 2f + AvText.Height(line, width - PadX - 6f) + PadY);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(title.rectTransform, PadX, PadY - 2f, s.W - PadX - 6f, 16f);
            AvLay.Place(line.rectTransform, PadX, PadY + 16f, s.W - PadX - 6f, AvText.Height(line, s.W - PadX - 6f));
        }

        public override void Restyle()
        {
            AvStyle r = AvStyleHost.FuiStyle("row");
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            rail.color = WmcState.Color(railClass);
            title.color = locked ? WingRankColor.Dim : WingRankColor.Ink;
            line.color = inactive ? WmcState.Color("warn") : WingRankColor.Dim;
        }
    }

    /// <summary>AIRFRAME ASSIGNMENT: where the dossier's pilot is (the airframe with its icon, and a slot line that may wrap), with AIR SAR
    /// and LOCAL SAR always shown, disabled with their reason in the help.</summary>
    internal sealed class WingAssignBar : AvPart
    {
        private const float PadX = 10f, ButtonW = 104f, MinH = 72f;
        private readonly AvFrame frame;
        private readonly Image rail, icon;
        private readonly TMP_Text name, slot;
        private string railClass = "inert";

        public readonly AvControl AirSar, LocalSar;

        public WingAssignBar(RectTransform parent, Action air, Action local)
        {
            Rect = AvLay.Child(parent, "AssignBar");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            icon = AvLay.Solid(Rect, "Icon", Color.white);
            icon.preserveAspect = true;
            icon.enabled = false;
            name = AvText.Make(Rect, "Name", AvTextRole.Label);
            AvText.Fit(name, false);
            slot = AvText.Make(Rect, "Slot", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            AirSar = AvControl.Make(Rect, new AvControl.Spec("AIR SAR", air));
            LocalSar = AvControl.Make(Rect, new AvControl.Spec("LOCAL SAR", local));
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

        private float TextW(float width) => width - (PadX + 36f) - ButtonW - PadX - 6f;

        public override float Measure(float width) => Mathf.Max(MinH, 8f + 16f + 2f + AvText.Height(slot, TextW(width)) + 8f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float textW = TextW(s.W);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(icon.rectTransform, PadX, 8f, 24f, 24f);
            AvLay.Place(name.rectTransform, PadX + 36f, 6f, textW, 18f);
            AvLay.Place(slot.rectTransform, PadX + 36f, 26f, textW, AvText.Height(slot, textW));
            AvLay.Place(AirSar.Rect, s.W - PadX - ButtonW, 6f, ButtonW, 28f);
            AvLay.Place(LocalSar.Rect, s.W - PadX - ButtonW, 38f, ButtonW, 28f);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            rail.color = WmcState.Color(railClass);
            name.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            slot.color = WingRankColor.Dim;
            AirSar.Restyle();
            LocalSar.Restyle();
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

    /// <summary>A key and its value on one line (wraps), with an optional tape under the value (FUEL, HULL).</summary>
    internal sealed class InspectKv : AvPart
    {
        private const float KeyW = 52f, TapeH = 4f;
        private readonly TMP_Text key, value;
        private readonly Image track, fill;
        private readonly bool tape;
        private float fraction;
        private string railClass = "inert";
        private float width = 100f;

        public InspectKv(RectTransform parent, string keyText, bool withTape)
        {
            Rect = AvLay.Child(parent, "Kv " + keyText);
            tape = withTape;
            key = AvText.Make(Rect, "Key", AvTextRole.Label, keyText);
            AvText.Fit(key, false);
            value = AvText.Make(Rect, "Value", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
            if (tape)
            {
                track = AvLay.Solid(Rect, "Track", Color.clear);
                fill = AvLay.Solid(Rect, "Fill", Color.clear);
            }
            Restyle();
        }

        public string Text => value.text;

        public bool Set(string text)
        {
            if (value.text == text) return false;
            value.text = text;
            return true;
        }

        /// <summary>The tape's fraction (NaN: empty) and state class.</summary>
        public void SetTape(float f, string railClassName)
        {
            if (!tape) return;
            fraction = float.IsNaN(f) ? 0f : Mathf.Clamp01(f);
            railClass = railClassName;
            PlaceFill();
            fill.color = WmcState.Color(railClass);
        }

        private float ValueH(float w) => Mathf.Max(18f, AvText.Height(value, w - KeyW));

        public override float Measure(float w) => ValueH(w) + (tape ? TapeH + 3f : 0f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            float vh = ValueH(s.W);
            AvLay.Place(key.rectTransform, 0f, 0f, KeyW, 18f);
            AvLay.Place(value.rectTransform, KeyW, 0f, s.W - KeyW, vh);
            if (!tape) return;
            AvLay.Place(track.rectTransform, KeyW, vh + 2f, s.W - KeyW, TapeH);
            PlaceFill();
        }

        private void PlaceFill()
        {
            if (!tape) return;
            float vh = ValueH(width);
            AvLay.Place(fill.rectTransform, KeyW, vh + 2f, Mathf.Max(0f, (width - KeyW) * fraction), TapeH);
        }

        public override void Restyle()
        {
            key.color = WingRankColor.Dim;
            value.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            if (!tape) return;
            track.color = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
            fill.color = WmcState.Color(railClass);
        }
    }
}
