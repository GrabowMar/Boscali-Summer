using NOAvionics;
using System;
using TMPro;
using UnityEngine;
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
    // Kit v2 parts SUPPLY and LOADOUT share and the toolkit has no part for (kit gaps): built from AvPart + AvFrame/AvText/AvLay.

    /// <summary>Registers an <see cref="AvList"/>'s own pager (PREV / NEXT) under the 0.9 ids (<c>prefix + "prev"</c>, <c>"next"</c>).</summary>
    internal static class WmcListPaging
    {
        public static void Register(WmcControls ids, string prefix, AvList list, int pageSize)
        {
            ids.Add(prefix + "prev", () => list.Reveal(Mathf.Max(0, list.Page - 1) * pageSize));
            ids.Add(prefix + "next", () => list.Reveal((list.Page + 1) * pageSize));
        }
    }

    /// <summary>SUPPLY's pilot line: a small portrait, callsign · name, rank · XP · status, and ◂ ▸ for the next pilot (the card is not a
    /// click target; only the arrows act).</summary>
    internal sealed class WmcPilotCard : AvPart
    {
        private const float Height = 40f, ArrowW = 34f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text nameText, infoText;
        private string nameFull = "", infoFull = "";
        private float width;
        private AvState state = AvState.Inert;

        public WmcPilotCard(RectTransform parent, Action<int> step)
        {
            Rect = AvLay.Child(parent, "PilotCard");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            Portrait = WmcPortrait.Build(Rect, new Rect(8f, -3f, 30f, 34f));
            nameText = WmcSheet.Text(Rect, "Name", AvTextRole.Label);
            infoText = WmcSheet.Text(Rect, "Info", AvTextRole.ProseSmall);
            Prev = WmcSheet.Chevron(Rect, () => step(-1), true, null);
            Next = WmcSheet.Chevron(Rect, () => step(1), false, null);
            Restyle();
        }

        public WmcPortrait Portrait { get; }

        public AvControl Prev { get; }

        public AvControl Next { get; }

        public string Name => nameFull;

        public string Info => infoFull;

        /// <summary>Callsign line; rank and status share the second line.</summary>
        public void Set(string name, string rank, string status, AvState s)
        {
            nameFull = name ?? "";
            infoFull = string.IsNullOrEmpty(rank) ? status ?? "" : string.IsNullOrEmpty(status) ? rank : rank + " · " + status;
            Fit();
            if (s == state) return;
            state = s;
            Restyle();
        }

        public override float Measure(float w) => Height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(Next.Rect, s.W - 6f - ArrowW, 6f, ArrowW, 28f);
            AvLay.Place(Prev.Rect, s.W - 8f - 2f * ArrowW, 6f, ArrowW, 28f);
            Fit();
        }

        private void Fit()
        {
            float w = Mathf.Max(0f, (width > 0f ? width : 400f) - 46f - 2f * ArrowW - 18f);
            WmcSheet.SetFit(nameText, nameFull, w);
            WmcSheet.SetFit(infoText, infoFull, w);
            AvLay.Place(nameText.rectTransform, 46f, 3f, w, 18f);
            AvLay.Place(infoText.rectTransform, 46f, 21f, w, 16f);
        }

        public override void Restyle()
        {
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state));
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
            nameText.color = WmcSheet.Ink;
            infoText.color = WmcSheet.Dim;
            if (Prev != null)
            {
                Prev.Restyle();
                Next.Restyle();
            }
        }
    }

    /// <summary>SUPPLY's dispatch card, the sheet's top: a state tag with what REQUISITION would send (airframe and price, then its
    /// pilot · fit · fuel · base line), the next-call figures beside the REQUISITION button, and under them — always in words — why it
    /// cannot or what being over the limit does. Both prose lines wrap, so nothing is cut.</summary>
    internal sealed class WmcDispatchCard : AvPart
    {
        private const float Pad = 8f, ChipW = 96f, TopH = 20f, ButtonH = 26f;
        private readonly AvFrame frame;
        private readonly Image rail, icon;
        private readonly AvChip chip;
        private readonly TMP_Text title, line, meta, blocker;
        private AvState state = AvState.Info;
        private string blockerLevel = "", titleFull = "";
        private float width;

        public WmcDispatchCard(RectTransform parent, string requisitionLabel, Action requisition)
        {
            Rect = AvLay.Child(parent, "DispatchCard");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            chip = new AvChip(Rect);
            title = WmcSheet.Text(Rect, "Title", AvTextRole.Label);
            icon = AvLay.Solid(Rect, "Icon", Color.white);
            icon.preserveAspect = true;
            icon.enabled = false;
            line = AvText.Make(Rect, "Line", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            meta = AvText.Make(Rect, "Meta", AvTextRole.ProseSmall, "", TextAlignmentOptions.MidlineLeft, true);
            blocker = AvText.Make(Rect, "Blocker", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            Button = AvControl.Make(Rect, new AvControl.Spec(requisitionLabel, requisition, AvButtonStyle.Primary));
            Restyle();
        }

        /// <summary>REQUISITION, on the card.</summary>
        public AvControl Button { get; }

        public string Title => title.text;

        public string Line => line.text;

        public string Blocker => blocker.text;

        public string Meta => meta.text;

        /// <summary>The next-call figures beside the button (price, funds after, crew).</summary>
        public void SetMeta(string text)
        {
            WmcSheet.Set(meta, text);
            Changed();
        }

        /// <summary><paramref name="level"/> is "warn", "info", "ok" or "" (dim).</summary>
        public void Set(string word, AvState s, string titleText, Sprite sprite, string lineText, string blockerText, string level)
        {
            chip.Set(word, s);
            titleFull = titleText ?? "";
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            WmcSheet.Set(line, lineText);
            WmcSheet.Set(blocker, blockerText);
            blockerLevel = level ?? "";
            state = s;
            Restyle();
            Fit();
            Changed();
        }

        private float LineW(float w) => Mathf.Max(1f, w - 2f * Pad - 4f);

        private float ButtonW(float w) => Mathf.Min(w * 0.5f, Mathf.Max(150f, WmcSheet.ChipW(Button.Label) + 8f));

        private float MetaW(float w) => Mathf.Max(1f, LineW(w) - ButtonW(w) - 8f);

        private float RowC(float w) => Mathf.Max(ButtonH, AvText.Height(meta, MetaW(w)));

        public override float Measure(float w) =>
            Pad + TopH + 4f + AvText.Height(line, LineW(w)) + 4f + RowC(w) + (blocker.text.Length > 0 ? 2f + AvText.Height(blocker, LineW(w)) : 0f) + Pad;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            chip.Place(new AvSlot(Pad, Pad + 1f, ChipW, AvGridTokens.ChipStrip));
            AvLay.Place(icon.rectTransform, s.W - Pad - 20f, Pad, 20f, 20f);
            Fit();
            float y = Pad + TopH + 4f, lh = AvText.Height(line, LineW(s.W));
            AvLay.Place(line.rectTransform, Pad + 4f, y, LineW(s.W), lh);
            y += lh + 4f;
            float rc = RowC(s.W), bw = ButtonW(s.W);
            AvLay.Place(meta.rectTransform, Pad + 4f, y, MetaW(s.W), rc);
            AvLay.Place(Button.Rect, s.W - Pad - bw, y + (rc - ButtonH) * 0.5f, bw, ButtonH);
            y += rc;
            AvLay.Place(blocker.rectTransform, Pad + 4f, y + 2f, LineW(s.W), AvText.Height(blocker, LineW(s.W)));
        }

        private void Fit()
        {
            float w = width > 0f ? width : 400f;
            float tw = Mathf.Max(0f, w - Pad - ChipW - 8f - Pad - (icon.enabled ? 26f : 0f));
            WmcSheet.SetFit(title, titleFull, tw);
            AvLay.Place(title.rectTransform, Pad + ChipW + 8f, Pad, tw, TopH);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card " + AvStates.Class(state));
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            rail.color = WmcState.Color(AvStates.Class(state));
            title.color = WmcSheet.Ink;
            line.color = WmcSheet.Dim;
            meta.color = WmcSheet.Dim;
            blocker.color = blockerLevel == "info" ? AvStyleHost.FuiColor("info", AvTheme.RailInfo)
                : blockerLevel.Length > 0 ? WmcUi.LevelColor(blockerLevel) : line.color;
            chip.Restyle();
            if (Button != null) Button.Restyle();
        }
    }

    /// <summary>LOADOUT's build card: the airframe's icon, the template it edits and its state chip, the build summary, and a tape of
    /// how many stations are fitted. Not a click target.</summary>
    internal sealed class WmcBuildCard : AvPart
    {
        private const float Pad = 8f, ChipW = 96f, TopH = 22f, TapeH = 6f;
        private readonly AvFrame frame;
        private readonly Image rail, icon, track, fill;
        private readonly AvChip chip;
        private readonly TMP_Text title, chain;
        private AvState state = AvState.Inert;
        private string tapeState = "inert", railState = "inert";
        private float tape;

        public WmcBuildCard(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "BuildCard");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            icon = AvLay.Solid(Rect, "Icon", Color.white);
            icon.preserveAspect = true;
            icon.enabled = false;
            chip = new AvChip(Rect);
            title = AvText.Make(Rect, "Title", AvTextRole.Label);
            AvText.Fit(title, false);
            chain = AvText.Make(Rect, "Chain", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            track = AvLay.Solid(Rect, "TapeTrack", Color.clear);
            fill = AvLay.Solid(Rect, "TapeFill", Color.clear);
            Restyle();
        }

        /// <summary>The title and chain as shown (automation reads them).</summary>
        public string Title => title.text;

        /// <summary><paramref name="stateClass"/> colours the chip and the tape, <paramref name="railClass"/> the rail (a pending delete
        /// asks in caution).</summary>
        public void Set(string titleText, string chainText, string word, string stateClass, string railClass, Sprite sprite, float tapeFraction)
        {
            if (title.text != (titleText ?? "")) title.text = titleText ?? "";
            if (chain.text != (chainText ?? "")) chain.text = chainText ?? "";
            chip.Set(word, WmcState.Of(stateClass));
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            tape = Mathf.Clamp01(tapeFraction);
            state = WmcState.Of(stateClass);
            tapeState = stateClass;
            railState = railClass;
            Restyle();
            PlaceTape();
        }

        private float TextX => Pad + 44f;

        private float ChainW(float width) => Mathf.Max(1f, width - TextX - Pad);

        public override float Measure(float width) => Pad + TopH + 2f + Mathf.Max(AvText.Height(chain, ChainW(width)), 15f) + 6f + TapeH + Pad;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(icon.rectTransform, Pad + 2f, Pad, 36f, 26f);
            chip.Place(new AvSlot(s.W - Pad - ChipW, Pad, ChipW, AvGridTokens.ChipStrip));
            AvLay.Place(title.rectTransform, TextX, Pad, Mathf.Max(0f, s.W - TextX - ChipW - 2f * Pad), TopH);
            float ch = Mathf.Max(AvText.Height(chain, ChainW(s.W)), 15f);
            AvLay.Place(chain.rectTransform, TextX, Pad + TopH + 2f, ChainW(s.W), ch);
            AvLay.Place(track.rectTransform, TextX, Pad + TopH + 2f + ch + 6f, ChainW(s.W), TapeH);
            PlaceTape();
        }

        private void PlaceTape()
        {
            Vector2 p = track.rectTransform.anchoredPosition, sz = track.rectTransform.sizeDelta;
            AvLay.Place(fill.rectTransform, p.x, -p.y, sz.x * tape, sz.y);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card " + AvStates.Class(state));
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            rail.color = WmcState.Color(railState);
            title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            chain.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            track.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-track").Background, AvTheme.Hairline);
            fill.color = WmcState.Color(tapeState);
            icon.color = AvTheme.Friendly;
            chip.Restyle();
        }
    }
}
