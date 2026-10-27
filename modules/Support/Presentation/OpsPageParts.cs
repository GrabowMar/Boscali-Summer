using System;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Fronts;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>How many lines a front card keeps. The panel picks one from its height: secondary lines go first, the OPEN button never shrinks.</summary>
    internal enum CardTier : byte { Core, Mid, Full }

    /// <summary>Absolute-placement helpers shared by the OPS page parts (kit text, pips and outlined tags only; colours come from <see cref="AvInk"/>).</summary>
    internal static class OpsParts
    {
        /// <summary>A mono box that wraps (two lines at 10 to 11 px need about 30 px).</summary>
        public static TMP_Text Wrapped(RectTransform p, string name, float x, float y, float w, float h, float size, bool strong = false)
        {
            TMP_Text t = FrontKit.Mono(p, name, x, y, w, h, size, TextAlignmentOptions.TopLeft, strong);
            t.enableWordWrapping = true;
            return t;
        }

        public static Image[] Pips(RectTransform p, string name, int count)
        {
            var pips = new Image[count];
            for (int i = 0; i < count; i++) pips[i] = FrontKit.Solid(p, name + i, 0f, 0f, 1f, 1f, Color.clear);
            return pips;
        }

        public static void PlacePips(Image[] pips, float x, float y, float w, float h, float step)
        {
            for (int i = 0; i < pips.Length; i++) AvLay.Place(pips[i].rectTransform, x + i * step, y, w, h);
        }

        /// <summary>The first <paramref name="filled"/> pips take <paramref name="on"/>, the rest <paramref name="off"/>: a count as a shape, not a colour.</summary>
        public static void Fill(Image[] pips, int filled, Color on, Color off)
        {
            for (int i = 0; i < pips.Length; i++)
            {
                Color c = i < filled ? on : off;
                if (pips[i].color != c) pips[i].color = c;
            }
        }
    }

    /// <summary>A small outlined stamp: a tinted frame with one word in it (status stamps, phase tags, brevity codes).</summary>
    internal sealed class OpsTag
    {
        private readonly AvFrame frame;
        private readonly TMP_Text text;

        public OpsTag(RectTransform parent, string name, float size, float tracking = 1f)
        {
            frame = FrontKit.Panel(parent, name + "Tag", 0f, 0f, 10f, 10f);
            text = FrontKit.Mono(parent, name + "Word", 0f, 0f, 10f, 10f, size, TextAlignmentOptions.Center, true, tracking);
        }

        /// <summary>Frame at (x, y, w, h); the word is centred in what is left after <paramref name="inset"/> px on the left (room for a glyph).</summary>
        public void Place(float x, float y, float w, float h, float inset = 0f)
        {
            AvLay.Place(frame.rectTransform, x, y, w, h);
            AvLay.Place(text.rectTransform, x + inset, y, w - inset, h);
        }

        public void Set(string word, Color ink, Color fill, Color edge)
        {
            FrontKit.Set(text, word, ink);
            frame.Paint(fill, edge);
        }

        public void Show(bool on)
        {
            if (frame.gameObject.activeSelf == on) return;
            frame.gameObject.SetActive(on);
            text.gameObject.SetActive(on);
        }

        public void Tone(string word, AvState state)
        {
            Color hue = AvInk.State(state);
            Set(word, FrontKit.Tone(state), hue.WithAlpha(0.14f), hue.WithAlpha(0.9f));
        }
    }

    /// <summary>
    /// One front on the OPS page, always in the same field order: domain and room name with the codename and a status stamp, the current blocker,
    /// readiness and superiority, your support, the programme (FUNDING and BUILDING are different phases), the queue and the directive
    /// (no event history: OPS shows state, not logs). A tier drops the secondary lines on a short panel; OPEN is always there. Absolute layout, no policy.
    /// </summary>
    internal sealed class FrontCardPart : AvPart
    {
        private const float PadX = 12f;
        private static readonly string[] Rooms = { "ORBIT", "NETWORK", "SHADOW" };
        private static readonly string[] ReadinessWords = BuildWords("R", "", FrontRules.MaxReadiness);
        private static readonly string[] QueueWords = BuildWords("", "/" + FrontRules.MaxQueue, FrontRules.MaxQueue);

        private static string[] BuildWords(string prefix, string suffix, int max)
        {
            var words = new string[max + 1];
            for (int i = 0; i < words.Length; i++) words[i] = prefix + i + suffix;
            return words;
        }

        public static float HeightOf(CardTier tier) => tier == CardTier.Full ? 150f : tier == CardTier.Mid ? 111f : 90f;

        private readonly FrontSkin skin = new FrontSkin();
        private readonly bool mid, full;
        private readonly float height;
        private readonly AvFrame back;
        private readonly Image rail;
        private readonly TMP_Text icon, name, codename, stampIcon, blocker, readiness, supLabel, supWord, supValue, support, funds;
        private readonly TMP_Text buildName, buildNote, queueLabel, queueCount, queueLine, dirLabel, dirValue;
        private readonly OpsTag stamp, phase;
        private readonly Image supWell, supFill, supNeedle;
        private readonly Image[] pips, queuePips;
        private readonly FrontBar buildBar;
        private readonly AvControl open;
        private float width = 458f, barX, barW;
        private FrontCardView shown;
        private bool painted;
        private FrontStatus lastStatus = (FrontStatus)255;

        public FrontCardPart(RectTransform parent, Front front, Action<Front> onOpen, CardTier tier)
        {
            mid = tier >= CardTier.Mid; full = tier >= CardTier.Full;
            height = HeightOf(tier);
            Rect = AvLay.Child(parent, "Front " + front);
            back = FrontKit.Panel(Rect, "Back", 0f, 0f, width, height, 6f, 7f);
            rail = FrontKit.Solid(Rect, "Rail", 0f, 0f, 3f, height, Color.clear);

            // Header: domain and room, codename, status stamp, OPEN.
            icon = AvIcons.Make(Rect, front == Front.Space ? AvIcon.Satellite : front == Front.Cyber ? AvIcon.Antenna : AvIcon.UsersGroup, 16f, AvInk.Ink);
            name = FrontKit.Cond(Rect, "Name", 34f, 5f, 124f, 26f, 13f, TextAlignmentOptions.MidlineLeft);
            name.characterSpacing = 2f;
            name.text = FrontRules.Name(front) + " · " + Rooms[(int)front];
            codename = FrontKit.Mono(Rect, "Codename", 160f, 5f, 66f, 26f, 10f, TextAlignmentOptions.MidlineLeft, false, 1.5f);
            codename.text = FrontWords.Callsign(front);
            stamp = new OpsTag(Rect, "Stamp", 10f);
            stampIcon = AvIcons.Make(Rect, AvIcon.Clock, 14f, AvInk.Dim);
            open = AvControl.Make(Rect, new AvControl.Spec("OPEN", () => onOpen?.Invoke(front), AvButtonStyle.Primary));
            open.SingleLine();
            open.Help = "Open the " + FrontRules.Name(front) + " front window: map, rail, programmes and perks.";

            blocker = FrontKit.Mono(Rect, "Blocker", PadX, 33f, 434f, 16f, 11f, TextAlignmentOptions.MidlineLeft, true);

            // Readiness pips with the rung, then the superiority bar with its word and value.
            pips = OpsParts.Pips(Rect, "Pip", FrontRules.MaxReadiness);
            readiness = FrontKit.Mono(Rect, "Readiness", 86f, 51f, 26f, 18f, 12f, TextAlignmentOptions.MidlineLeft, true);
            supLabel = FrontKit.Mono(Rect, "SupLabel", 114f, 51f, 70f, 18f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
            supLabel.text = "SUPERIORITY";
            supWell = FrontKit.Solid(Rect, "SupWell", 0f, 0f, 0f, 12f, Color.clear);
            supFill = FrontKit.Solid(Rect, "SupFill", 0f, 0f, 0f, 10f, Color.clear);
            supNeedle = FrontKit.Solid(Rect, "SupNeedle", 0f, 0f, 3f, 16f, Color.clear);
            supWord = FrontKit.Mono(Rect, "SupWord", 0f, 51f, 84f, 18f, 11f, TextAlignmentOptions.MidlineLeft, true, 0.5f);
            supValue = FrontKit.Mono(Rect, "SupValue", 0f, 51f, 42f, 18f, 11f, TextAlignmentOptions.MidlineRight, true);

            support = FrontKit.Mono(Rect, "Support", PadX, 71f, 280f, 16f, 11f, TextAlignmentOptions.MidlineLeft);
            funds = FrontKit.Mono(Rect, "Funds", 0f, 71f, 150f, 16f, 10.5f, TextAlignmentOptions.MidlineRight);

            if (mid)
            {
                phase = new OpsTag(Rect, "Phase", 10f);
                buildName = FrontKit.Mono(Rect, "BuildName", 80f, 89f, 150f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                buildBar = new FrontBar(Rect, skin, 236f, 93f, 100f, 10f);
                buildNote = FrontKit.Mono(Rect, "BuildNote", 0f, 89f, 104f, 18f, 10.5f, TextAlignmentOptions.MidlineRight);
            }
            if (full)
            {
                queueLabel = FrontKit.Mono(Rect, "QueueLabel", PadX, 109f, 44f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                queueLabel.text = "QUEUE";
                queuePips = OpsParts.Pips(Rect, "QueuePip", FrontRules.MaxQueue);
                queueCount = FrontKit.Mono(Rect, "QueueCount", 112f, 109f, 30f, 16f, 11f, TextAlignmentOptions.MidlineLeft, true);
                queueLine = FrontKit.Mono(Rect, "QueueLine", 146f, 109f, 300f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                dirLabel = FrontKit.Mono(Rect, "DirLabel", PadX, 127f, 72f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                dirLabel.text = "DIRECTIVE";
                dirValue = FrontKit.Mono(Rect, "DirValue", 86f, 127f, 360f, 16f, 11f, TextAlignmentOptions.MidlineLeft, true);
            }
            skin.Add(StyleStatic);
            LayoutFor(width);
        }

        public override float Measure(float w) => height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (Mathf.Abs(s.W - width) > 0.5f) LayoutFor(s.W);
        }

        private void LayoutFor(float w)
        {
            width = w;
            AvLay.Place(back.rectTransform, 0f, 0f, w, height);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, height);
            AvLay.Place(icon.rectTransform, 12f, 9f, 18f, 18f);
            stamp.Place(228f, 7f, 124f, 22f, 22f);
            AvLay.Place(stampIcon.rectTransform, 234f, 11f, 14f, 14f);
            AvLay.Place(open.Rect, w - 82f, 5f, 72f, 26f);
            AvLay.Place(blocker.rectTransform, PadX, 33f, w - 2f * PadX, 16f);
            OpsParts.PlacePips(pips, 12f, 54f, 11f, 11f, 14f);
            barX = 186f; barW = Mathf.Max(80f, w - barX - 150f);
            AvLay.Place(supWell.rectTransform, barX, 54f, barW, 12f);
            AvLay.Place(supWord.rectTransform, barX + barW + 8f, 51f, 84f, 18f);
            AvLay.Place(supValue.rectTransform, w - 54f, 51f, 42f, 18f);
            AvLay.Place(support.rectTransform, PadX, 71f, w - 178f, 16f);
            AvLay.Place(funds.rectTransform, w - 162f, 71f, 150f, 16f);
            if (mid)
            {
                phase.Place(PadX, 90f, 62f, 16f);
                AvLay.Place(buildNote.rectTransform, w - 116f, 89f, 104f, 18f);
            }
            if (full)
            {
                OpsParts.PlacePips(queuePips, 58f, 112f, 10f, 10f, 13f);
                AvLay.Place(queueLine.rectTransform, 146f, 109f, w - 158f, 16f);
                AvLay.Place(dirValue.rectTransform, 86f, 127f, w - 98f, 16f);
            }
            if (painted) Paint(shown); // the superiority bar depends on the width
        }

        private void StyleStatic()
        {
            back.Paint(AvInk.Surface.WithAlpha(0.62f), AvInk.Hairline);
            back.BracketColor = AvInk.Frame.WithAlpha(0.8f);
            supWell.color = AvInk.Ground.WithAlpha(0.92f);
            supNeedle.color = AvInk.Ink;
            Color dim = AvInk.Dim;
            supLabel.color = dim; codename.color = dim; icon.color = dim;
            name.color = AvInk.Ink;
            if (full) { queueLabel.color = dim; dirLabel.color = dim; }
        }

        public override void Restyle()
        {
            skin.Apply();
            if (painted) Paint(shown);
        }

        private static AvState StatusState(FrontStatus s) =>
            s == FrontStatus.Operational ? AvState.Ready : s == FrontStatus.Degraded ? AvState.Caution : s == FrontStatus.Offline ? AvState.Danger : AvState.Inert;

        private static string StatusWord(FrontStatus s) =>
            s == FrontStatus.Operational ? "OPERATIONAL" : s == FrontStatus.Degraded ? "DEGRADED" : s == FrontStatus.Offline ? "OFFLINE" : "WAITING";

        private static AvIcon StatusIcon(FrontStatus s) =>
            s == FrontStatus.Operational ? AvIcon.CircleCheck : s == FrontStatus.Degraded ? AvIcon.AlertTriangle : s == FrontStatus.Offline ? AvIcon.Unlink : AvIcon.Clock;

        public void Paint(in FrontCardView c)
        {
            shown = c;
            painted = true;
            AvState status = StatusState(c.Status);
            Color hue = AvInk.State(status);
            rail.color = c.Status == FrontStatus.Waiting ? AvInk.Frame : hue;
            if (lastStatus != c.Status) { lastStatus = c.Status; AvIcons.Set(stampIcon, StatusIcon(c.Status), 14f); }
            stampIcon.color = FrontKit.Tone(status);
            stamp.Tone(StatusWord(c.Status), status);

            FrontKit.Set(blocker, c.Alert ?? "", c.AlertTone == AvState.Inert ? AvInk.Dim : FrontKit.Tone(c.AlertTone));

            FrontKit.Set(readiness, c.Known ? ReadinessWords[Mathf.Clamp(c.Readiness, 0, FrontRules.MaxReadiness)] : "—", AvInk.Ink);
            OpsParts.Fill(pips, c.Known ? c.Readiness : 0, AvInk.Select, AvInk.Frame.WithAlpha(0.45f));
            Color tone = FrontKit.Tone(c.Tone);
            float half = barW * 0.5f, len = Mathf.Abs(c.Superiority) / 100f * half;
            supFill.color = AvInk.State(c.Tone).WithAlpha(c.Known ? 0.85f : 0f);
            AvLay.Place(supFill.rectTransform, barX + half + (c.Superiority >= 0 ? 0f : -len), 55f, len, 10f);
            AvLay.Place(supNeedle.rectTransform, barX + half + c.Superiority / 100f * half - 1.5f, 52f, 3f, 16f);
            supNeedle.gameObject.SetActive(c.Known);
            FrontKit.Set(supLabel, c.Known && !string.IsNullOrEmpty(c.Power) ? c.Power : "SUPERIORITY", AvInk.Ink); // the power the bar earns: what every perk of this front is scaled by
            FrontKit.Set(supWord, c.Word, tone);
            FrontKit.Set(supValue, c.Known ? FrontKit.Signed(c.Superiority) : "—", tone);

            FrontKit.Set(support, c.Known
                ? "YOUR SUPPORT " + c.Available + " AVAILABLE" + (c.ArmedPerks > 0 ? " · " + c.ArmedPerks + " ARMED" : "") + (c.PendingPerks > 0 ? " · " + c.PendingPerks + " PENDING" : "")
                : "YOUR SUPPORT —", AvInk.Ink);
            FrontKit.Set(funds, c.Known ? c.Budget + " FUNDS" : "", AvInk.Dim);

            if (mid)
            {
                AvState ps = c.Phase == ProgrammePhase.Building ? AvState.Ready : c.Phase == ProgrammePhase.Funding ? AvState.Info : AvState.Inert;
                phase.Tone(c.Phase == ProgrammePhase.Building ? "BUILDING" : c.Phase == ProgrammePhase.Funding ? "FUNDING" : "IDLE", ps);
                FrontKit.Set(buildName, c.Building, c.Phase != ProgrammePhase.None ? AvInk.Ink : AvInk.Dim);
                buildBar.Show(c.Phase != ProgrammePhase.None);
                buildBar.Set(c.BuildProgress, c.Phase == ProgrammePhase.Building ? AvInk.State(AvState.Ready) : AvInk.Select);
                FrontKit.Set(buildNote, c.BuildNote ?? "", AvInk.Dim);
            }
            if (full)
            {
                FrontKit.Set(queueCount, c.Known ? QueueWords[Mathf.Clamp(c.QueueCount, 0, FrontRules.MaxQueue)] : "—", AvInk.Ink);
                OpsParts.Fill(queuePips, c.Known ? c.QueueCount : 0, AvInk.Select, AvInk.Frame.WithAlpha(0.45f));
                FrontKit.Set(queueLine, !string.IsNullOrEmpty(c.Queue) ? "NEXT " + c.Queue : "", AvInk.Dim);
                FrontKit.Set(dirValue, c.Known ? c.Directive : "—", AvInk.Ink);
            }
        }
    }

    /// <summary>
    /// One perk of the PERKS tab as a call card: brevity code, name, rung pips, price in AP and the state words. There is no button in it:
    /// clicking the card arms the perk (the pinned strip then offers FIRE); arming spends nothing and a second click never fires.
    /// </summary>
    internal sealed class PerkRowPart : AvPart
    {
        /// <summary>21 px: eighteen perks (four of them protocol 41's) and three group headers still fit the 896 px page under the pinned strip without scrolling.</summary>
        public const float Height = 21f;
        private const float StateX = 314f;
        private static readonly string[] RungWords = { "R0", "R1", "R2", "R3", "R4", "R5" };

        private readonly AvFrame back;
        private readonly Image rail;
        private readonly OpsTag badge;
        private readonly TMP_Text name, rung, price, state;
        private readonly Image[] pips;
        private PerkRowView shown;
        private bool has, hover;
        private float width = 458f;

        public PerkRowPart(RectTransform parent, Action<SupportActionId> inspect, SupportActionId id)
        {
            Rect = AvLay.Child(parent, "Perk " + id);
            back = FrontKit.Panel(Rect, "Back", 0f, 0f, width, Height);
            rail = FrontKit.Solid(Rect, "Rail", 0f, 0f, 3f, Height, Color.clear);
            badge = new OpsTag(Rect, "Code", 10f);
            badge.Place(10f, 1.5f, 36f, 18f);
            name = FrontKit.Mono(Rect, "Name", 52f, 0f, 134f, Height, 12f, TextAlignmentOptions.MidlineLeft, true);
            pips = OpsParts.Pips(Rect, "RungPip", CallSheet.MaxRung);
            OpsParts.PlacePips(pips, 190f, 5.5f, 7f, 10f, 9f);
            rung = FrontKit.Mono(Rect, "Rung", 238f, 0f, 22f, Height, 10f, TextAlignmentOptions.MidlineLeft);
            price = FrontKit.Mono(Rect, "Price", 262f, 0f, 48f, Height, 11f, TextAlignmentOptions.MidlineLeft, true);
            state = FrontKit.Mono(Rect, "State", StateX, 0f, 136f, Height, 10f, TextAlignmentOptions.MidlineLeft, true); // one line: the view compacts a long gate (OpsPageViews.RowWord), the strip above has it whole
            AvHit hit = AvHit.On(back);
            hit.Hover = h => { hover = h; Look(); };
            hit.Click = e => { if (e.button == PointerEventData.InputButton.Left && has) inspect?.Invoke(shown.Id); };
            AvHelpTip.Attach(back.gameObject, "Arm this perk: the strip above shows its effect and price and offers FIRE. Nothing is spent until FIRE.");
            Layout(width);
            Restyle();
        }

        public override float Measure(float w) => Height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (Mathf.Abs(s.W - width) > 0.5f) Layout(s.W);
        }

        private void Layout(float w)
        {
            width = w;
            AvLay.Place(back.rectTransform, 0f, 0f, w, Height);
            AvLay.Place(state.rectTransform, StateX, 0f, Mathf.Max(90f, w - StateX - 8f), Height);
        }

        public override void Restyle()
        {
            if (has) Paint(shown);
            else { Look(); rail.color = AvInk.Frame; rung.color = AvInk.Dim; }
        }

        private void Look()
        {
            bool sel = has && shown.Inspected;
            float stroke = sel ? 1.4f : 0.65f;
            if (back.Stroke != stroke) { back.Stroke = stroke; back.SetVerticesDirty(); }
            back.Paint(sel ? AvInk.Raised.WithAlpha(0.85f) : AvInk.Surface.WithAlpha(hover ? 0.8f : 0.55f), sel ? AvInk.Select : hover ? AvInk.Frame : AvInk.Hairline);
            // Selected = a filled code plate; the outline alone would be colour only.
            badge.Set(has ? shown.Code : "", sel ? C2Kit.SlabInk : AvInk.Dim, sel ? AvInk.Select : AvInk.Ground.WithAlpha(0.6f), sel ? AvInk.Select : AvInk.Frame.WithAlpha(0.8f));
        }

        public void Paint(in PerkRowView r)
        {
            has = true; shown = r;
            bool live = r.Enabled || r.Armed;
            FrontKit.Set(name, r.Name, live ? AvInk.Ink : AvInk.Dim);
            FrontKit.Set(rung, RungWords[Mathf.Clamp(r.Rung, 0, RungWords.Length - 1)], AvInk.Dim);
            FrontKit.Set(price, r.Price, live ? AvInk.Ink : AvInk.Dim);
            FrontKit.Set(state, r.State, FrontKit.Tone(r.Tone));
            rail.color = AvInk.State(r.Tone == AvState.Inert ? AvState.Info : r.Tone).WithAlpha(r.Tone == AvState.Inert ? 0.5f : 1f);
            OpsParts.Fill(pips, r.Rung, AvInk.Select, AvInk.Frame.WithAlpha(0.45f));
            Look();
        }
    }

    /// <summary>
    /// One posted strike of the TASKED tab as a package card: brevity code, package name and a stamp (OPEN, ARMED, CLAIM SENT, LAUNCHING), the
    /// target grids, the time left and the origin line. Clicking it inspects it; ARM, FIRE and DISARM live in the pinned strip.
    /// </summary>
    internal sealed class TaskedCardPart : AvPart
    {
        public const float Height = 58f;

        private readonly AvFrame back;
        private readonly Image rail;
        private readonly OpsTag badge, stamp;
        private readonly TMP_Text name, grids, expiry, detail;
        private TaskedRowView shown;
        private bool has, hover;
        private float width = 458f;

        public TaskedCardPart(RectTransform parent, Action<int> inspect, string objectName)
        {
            Rect = AvLay.Child(parent, objectName);
            back = FrontKit.Panel(Rect, "Back", 0f, 0f, width, Height);
            rail = FrontKit.Solid(Rect, "Rail", 0f, 0f, 3f, Height, Color.clear);
            badge = new OpsTag(Rect, "Code", 10f);
            badge.Place(10f, 6f, 36f, 18f);
            name = FrontKit.Mono(Rect, "Name", 52f, 4f, 296f, 22f, 12f, TextAlignmentOptions.MidlineLeft, true);
            stamp = new OpsTag(Rect, "Stamp", 10f);
            grids = FrontKit.Mono(Rect, "Grids", 12f, 27f, 346f, 16f, 11f, TextAlignmentOptions.MidlineLeft, true);
            expiry = FrontKit.Mono(Rect, "Expiry", 0f, 27f, 88f, 16f, 11f, TextAlignmentOptions.MidlineRight, true);
            detail = FrontKit.Mono(Rect, "Detail", 12f, 43f, 434f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft);
            AvHit hit = AvHit.On(back);
            hit.Hover = h => { hover = h; Look(); };
            hit.Click = e => { if (e.button == PointerEventData.InputButton.Left && has) inspect?.Invoke(shown.PostId); };
            AvHelpTip.Attach(back.gameObject, "Arm this package: the strip above shows its targets, price and expiry and offers FIRE. Nothing is claimed until FIRE.");
            Layout(width);
            Restyle();
        }

        public override float Measure(float w) => Height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (Mathf.Abs(s.W - width) > 0.5f) Layout(s.W);
        }

        private void Layout(float w)
        {
            width = w;
            AvLay.Place(back.rectTransform, 0f, 0f, w, Height);
            stamp.Place(w - 106f, 6f, 96f, 18f);
            AvLay.Place(name.rectTransform, 52f, 4f, w - 164f, 22f);
            AvLay.Place(grids.rectTransform, 12f, 27f, w - 112f, 16f);
            AvLay.Place(expiry.rectTransform, w - 98f, 27f, 88f, 16f);
            AvLay.Place(detail.rectTransform, 12f, 43f, w - 24f, 14f);
        }

        public override void Restyle()
        {
            if (has) Paint(shown);
            else { Look(); rail.color = AvInk.Frame; }
        }

        private void Look()
        {
            bool sel = has && shown.Inspected;
            float stroke = sel ? 1.4f : 0.65f;
            if (back.Stroke != stroke) { back.Stroke = stroke; back.SetVerticesDirty(); }
            back.Paint(sel ? AvInk.Raised.WithAlpha(0.85f) : AvInk.Surface.WithAlpha(hover ? 0.8f : 0.55f), sel ? AvInk.Select : hover ? AvInk.Frame : AvInk.Hairline);
            badge.Set(has ? shown.Code : "", sel ? C2Kit.SlabInk : AvInk.Dim, sel ? AvInk.Select : AvInk.Ground.WithAlpha(0.6f), sel ? AvInk.Select : AvInk.Frame.WithAlpha(0.8f));
        }

        public void Paint(in TaskedRowView r)
        {
            has = true; shown = r;
            FrontKit.Set(name, r.Name, AvInk.Ink);
            stamp.Tone(r.Stamp, r.Tone);
            FrontKit.Set(grids, r.Grids, AvInk.Ink);
            FrontKit.Set(expiry, r.Expiry, r.Urgent ? FrontKit.Tone(AvState.Caution) : AvInk.Dim);
            FrontKit.Set(detail, r.Detail, AvInk.Dim);
            rail.color = AvInk.State(r.Tone);
            Look();
        }
    }

    /// <summary>
    /// The pinned order strip, docked under the tabs outside the page scroller: the inspected (or armed) perk or package with its code, stamp,
    /// effect or targets, price, and the exact gate or aim, and the ARM / FIRE and DISARM buttons that act on it. It is not part of a flow;
    /// the page leaves a spacer of <see cref="Height"/> under it so scrolled content passes beneath its opaque face.
    /// </summary>
    internal sealed class OrderStrip : AvPart
    {
        public const float Height = 92f;
        private const float RightW = 100f;

        private readonly Image under;
        private readonly AvFrame back;
        private readonly Image rail;
        private readonly OpsTag badge, stamp;
        private readonly TMP_Text name, meta, price, brief, gate;
        private readonly AvControl primary, disarm;
        private readonly Action<bool, int> press;
        private StripView shown;
        private bool has, coded = true;
        private float width = 458f;

        /// <param name="press">Called with (fire, id) when the primary button is pressed: ARM when fire is false, FIRE when true.</param>
        public OrderStrip(RectTransform parent, Action<bool, int> press, Action disarmClick)
        {
            this.press = press;
            Rect = AvLay.Child(parent, "OrderStrip");
            under = FrontKit.Solid(Rect, "Under", 0f, 0f, width, Height, Color.clear);
            under.raycastTarget = true; // clicks and wheel never reach the rows scrolled beneath
            back = FrontKit.Panel(Rect, "Back", 0f, 0f, width, Height, 6f, 7f);
            rail = FrontKit.Solid(Rect, "Rail", 0f, 0f, 3f, Height, Color.clear);
            badge = new OpsTag(Rect, "Code", 10f);
            name = FrontKit.Cond(Rect, "Name", 52f, 4f, 148f, 22f, 14f, TextAlignmentOptions.MidlineLeft);
            name.characterSpacing = 1.5f;
            stamp = new OpsTag(Rect, "Stamp", 10f);
            meta = FrontKit.Mono(Rect, "Meta", 286f, 4f, 70f, 22f, 10f, TextAlignmentOptions.MidlineLeft);
            price = FrontKit.Mono(Rect, "Price", 0f, 4f, 92f, 22f, 13f, TextAlignmentOptions.MidlineRight, true);
            brief = OpsParts.Wrapped(Rect, "Brief", 12f, 28f, 338f, 30f, 10.5f);
            gate = OpsParts.Wrapped(Rect, "Gate", 12f, 60f, 338f, 30f, 11f, true);
            primary = AvControl.Make(Rect, new AvControl.Spec("ARM", () => { if (has) this.press?.Invoke(shown.PrimaryFire, shown.Id); }, AvButtonStyle.Primary));
            primary.SingleLine();
            primary.Help = "ARM prepares the shown perk or package (nothing is spent). Once armed, FIRE sends it to the host, which charges and answers.";
            disarm = AvControl.Make(Rect, new AvControl.Spec("DISARM", disarmClick, AvButtonStyle.Quiet));
            disarm.SingleLine();
            disarm.Help = "Disarm whatever is armed. Nothing is spent or claimed.";
            Dock(AvGridTokens.Pad, width, 0f);
            Restyle();
        }

        /// <summary>Seats the strip at <paramref name="top"/> px under the console's header block, <paramref name="w"/> wide.</summary>
        public void Dock(float x, float w, float top)
        {
            width = w;
            AvLay.Place(Rect, x, top, w, Height);
            AvLay.Place(under.rectTransform, 0f, 0f, w, Height);
            AvLay.Place(back.rectTransform, 0f, 0f, w, Height);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, Height);
            badge.Place(10f, 6f, 36f, 18f);
            stamp.Place(204f, 6f, 78f, 18f);
            AvLay.Place(price.rectTransform, w - RightW, 4f, 92f, 22f);
            float zone = w - RightW - 20f;
            AvLay.Place(brief.rectTransform, 12f, 28f, zone, 30f);
            AvLay.Place(gate.rectTransform, 12f, 60f, zone, 30f);
            AvLay.Place(primary.Rect, w - RightW + 4f, 28f, RightW - 12f, 26f);
            AvLay.Place(disarm.Rect, w - RightW + 4f, 58f, RightW - 12f, 26f);
            PlaceName(true);
        }

        private void PlaceName(bool withBadge)
        {
            coded = withBadge;
            AvLay.Place(name.rectTransform, withBadge ? 52f : 12f, 4f, withBadge ? 148f : 190f, 22f);
        }

        public override void Restyle()
        {
            under.color = AvInk.Ground.WithAlpha(1f);
            back.Paint(AvInk.Surface.WithAlpha(0.92f), AvInk.Frame);
            back.BracketColor = AvInk.Frame.WithAlpha(0.8f);
            name.color = AvInk.Ink;
            if (has) Paint(shown);
        }

        public void Paint(in StripView s)
        {
            shown = s; has = true;
            bool withBadge = !string.IsNullOrEmpty(s.Code);
            if (withBadge != coded) PlaceName(withBadge);
            badge.Show(withBadge);
            stamp.Show(!string.IsNullOrEmpty(s.Stamp));
            rail.color = AvInk.State(s.StampTone == AvState.Inert ? AvState.Info : s.StampTone);
            badge.Set(s.Code ?? "", AvInk.Dim, AvInk.Ground.WithAlpha(0.6f), AvInk.Frame.WithAlpha(0.8f));
            stamp.Tone(s.Stamp ?? "", s.StampTone);
            FrontKit.Set(name, s.Name ?? "", AvInk.Ink);
            FrontKit.Set(meta, s.Meta ?? "", AvInk.Dim);
            FrontKit.Set(price, s.Price ?? "", AvInk.Ink);
            FrontKit.Set(brief, s.Brief ?? "", AvInk.Ink);
            FrontKit.Set(gate, s.Gate ?? "", FrontKit.Tone(s.GateTone == AvState.Inert ? AvState.Info : s.GateTone));
            OpsKit.Label(primary, s.Primary ?? "ARM");
            primary.SetStyle(s.PrimaryFire ? AvButtonStyle.Danger : s.PrimaryEnabled ? AvButtonStyle.Primary : AvButtonStyle.Quiet);
            primary.Armed = s.PrimaryFire;
            OpsKit.Enable(primary, s.PrimaryEnabled);
            OpsKit.Enable(disarm, s.DisarmEnabled);
        }
    }

    /// <summary>
    /// The TASKED page's cruise block, shown only while the pilot's cruise strike is in flight: its confirmed route as grids with the leg pips,
    /// and three distinct intents: ADD LEG (then a map right-click places it), CANCEL PICK (drops the pick, keeps the route) and CLEAR ROUTE.
    /// </summary>
    internal sealed class CruisePart : AvPart
    {
        public const float Height = 78f;
        private static readonly string[] LegWords = BuildLegWords();

        private static string[] BuildLegWords()
        {
            var words = new string[StrikeBallistics.MaxLegs + 1];
            for (int i = 0; i < words.Length; i++) words[i] = i + "/" + StrikeBallistics.MaxLegs + " LEGS";
            return words;
        }

        private readonly AvFrame back;
        private readonly Image rail;
        private readonly TMP_Text icon, name, legs, route, hint;
        private readonly OpsTag tag;
        private readonly Image[] pips;
        private readonly AvControl add, clear;
        private float width = 458f;

        public CruisePart(RectTransform parent, Action addLeg, Action cancelPick, Action clearRoute)
        {
            Rect = AvLay.Child(parent, "Perk Cruise");
            back = FrontKit.Panel(Rect, "Back", 0f, 0f, width, Height, 6f, 7f);
            rail = FrontKit.Solid(Rect, "Rail", 0f, 0f, 3f, Height, Color.clear);
            icon = AvIcons.Make(Rect, AvIcon.Bolt, 16f, AvInk.Dim);
            AvLay.Place(icon.rectTransform, 12f, 9f, 18f, 18f);
            name = FrontKit.Cond(Rect, "Name", 34f, 5f, 92f, 24f, 13f, TextAlignmentOptions.MidlineLeft);
            name.characterSpacing = 2f;
            name.text = "CRUISE SALVO";
            tag = new OpsTag(Rect, "Tag", 10f);
            tag.Place(130f, 8f, 92f, 20f);
            pips = OpsParts.Pips(Rect, "LegPip", StrikeBallistics.MaxLegs);
            OpsParts.PlacePips(pips, 286f, 12f, 9f, 12f, 12f);
            legs = FrontKit.Mono(Rect, "Legs", 366f, 5f, 80f, 24f, 11f, TextAlignmentOptions.MidlineRight, true);
            route = FrontKit.Mono(Rect, "Route", 12f, 31f, 434f, 16f, 11f, TextAlignmentOptions.MidlineLeft, true);
            hint = FrontKit.Mono(Rect, "Hint", 12f, 50f, 196f, 24f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            hint.enableWordWrapping = true;
            add = AvControl.Make(Rect, new AvControl.Spec("ADD LEG", null, AvButtonStyle.Primary));
            add.SingleLine();
            clear = AvControl.Make(Rect, new AvControl.Spec("CLEAR ROUTE", clearRoute, AvButtonStyle.Default));
            clear.SingleLine();
            add.Clicked += () => { if (steering) cancelPick?.Invoke(); else addLeg?.Invoke(); };
            add.Help = "ADD LEG: arm one map pick, then right-click the map for the next leg. While a pick is armed this reads CANCEL PICK: it drops the pick and keeps the confirmed route.";
            clear.Help = "CLEAR ROUTE: ask the host to drop every confirmed leg; the missiles fly direct again.";
            LayoutFor(width);
            Restyle();
        }

        private bool steering, painted;
        private AvState tone = AvState.Ready;

        public override float Measure(float w) => Height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (Mathf.Abs(s.W - width) > 0.5f) LayoutFor(s.W);
        }

        private void LayoutFor(float w)
        {
            width = w;
            AvLay.Place(back.rectTransform, 0f, 0f, w, Height);
            AvLay.Place(legs.rectTransform, w - 92f, 5f, 80f, 24f);
            AvLay.Place(route.rectTransform, 12f, 31f, w - 24f, 16f);
            AvLay.Place(add.Rect, w - 242f, 50f, 112f, 24f);
            AvLay.Place(clear.Rect, w - 124f, 50f, 112f, 24f);
            OpsParts.PlacePips(pips, w - 92f - pips.Length * 12f - 8f, 12f, 9f, 12f, 12f);
        }

        public override void Restyle()
        {
            back.Paint(AvInk.Surface.WithAlpha(0.62f), AvInk.Hairline);
            back.BracketColor = AvInk.Frame.WithAlpha(0.8f);
            name.color = AvInk.Ink;
            icon.color = AvInk.Dim;
            if (painted) rail.color = AvInk.State(tone);
        }

        public void Paint(OpsPageView v)
        {
            painted = true;
            steering = v.Steering;
            tone = v.Steering ? AvState.Caution : AvState.Ready;
            rail.color = AvInk.State(tone);
            tag.Tone(v.Steering ? "PICKING" : "IN FLIGHT", tone);
            FrontKit.Set(legs, LegWords[Mathf.Clamp(v.CruiseLegs, 0, StrikeBallistics.MaxLegs)], AvInk.Ink);
            OpsParts.Fill(pips, v.CruiseLegs, AvInk.Select, AvInk.Frame.WithAlpha(0.45f));
            bool routed = v.CruiseRoute.Length > 0;
            FrontKit.Set(route, routed ? v.CruiseRoute : "FLYING DIRECT", routed ? FrontKit.Tone(AvState.Info) : AvInk.Dim);
            FrontKit.Set(hint, v.Steering ? "RIGHT-CLICK THE MAP FOR LEG " + (v.CruiseLegs + 1) : "", FrontKit.Tone(AvState.Caution));
            OpsKit.Label(add, v.Steering ? "CANCEL PICK" : "ADD LEG");
            add.SetStyle(v.Steering ? AvButtonStyle.Default : AvButtonStyle.Primary);
            OpsKit.Enable(add, v.Steering || v.CruiseLegs < StrikeBallistics.MaxLegs);
            OpsKit.Enable(clear, routed || v.CruiseLegs > 0);
        }
    }
}
