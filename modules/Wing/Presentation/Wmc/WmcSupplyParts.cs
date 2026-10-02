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

    /// <summary>A block of a page's flow that takes no room while hidden (SUPPLY's INBOUND and ADOPT show only when they have
    /// something to say). It hosts a nested flow that lays out exactly as the page's; the outer flow follows its height. A hidden block
    /// still costs the flow's one line gap.</summary>
    internal sealed class WmcHidable : AvPart
    {
        private readonly AvFlow outer;
        private float seen = -1f;

        public WmcHidable(AvFlow outerFlow, AvTicker ticker, int pageIndex, string name)
        {
            outer = outerFlow;
            Rect = AvLay.Child(outerFlow.Content, name);
            Flow = new AvFlow(Rect, ticker, outerFlow.Width);
            Rect.gameObject.SetActive(false);
            ticker?.Add(pageIndex, AvTickRate.Fast, Watch);
        }

        public AvFlow Flow { get; }

        // Visibility is the base AvPart.Shown (GameObject active); the flow reads it
        // through the base type, so a second field here could only ever disagree.

        // Hides the base setter on purpose: showing also resets the height watch and
        // relayouts the outer flow. Callers use this static type; the flow never calls it.
        public new void SetShown(bool on)
        {
            if (on == Shown) return;
            Rect.gameObject.SetActive(on);
            seen = -1f;
            outer.RequestRelayout();
        }

        public override float Measure(float width)
        {
            if (!Shown) return 0f;
            Flow.Relayout();
            return Mathf.Max(0f, Flow.ContentHeight - 2f * AvGridTokens.Pad);
        }

        public override void Place(AvSlot slot) =>
            AvLay.Place(Rect, 0f, slot.Y - AvGridTokens.Pad, outer.Width, slot.H + 2f * AvGridTokens.Pad);

        /// <summary>The block grew or shrank on its own (a list changed): the page's flow follows.</summary>
        private void Watch()
        {
            if (!Shown) return;
            float h = Flow.ContentHeight;
            if (Mathf.Abs(h - seen) < 0.5f) return;
            seen = h;
            outer.RequestRelayout();
        }
    }

    /// <summary>Registers an <see cref="AvList"/>'s own pager (PREV / NEXT) under the 0.9 ids (<c>prefix + "prev"</c>, <c>"next"</c>).</summary>
    internal static class WmcListPaging
    {
        public static void Register(WmcControls ids, string prefix, AvList list, int pageSize)
        {
            ids.Add(prefix + "prev", () => list.Reveal(Mathf.Max(0, list.Page - 1) * pageSize));
            ids.Add(prefix + "next", () => list.Reveal((list.Page + 1) * pageSize));
        }
    }

    /// <summary>A page of airframe tiles (SUPPLY step 2 and LOADOUT's AIRFRAME): pooled tiles, each rebound only when its caller's key
    /// changes; a tile is an icon, a code, a name and, when the caller has one, a foot (the whole tile is the click target). A pager
    /// line under them, and one inert card that says why when the list is empty.</summary>
    internal sealed class WmcAirframeGrid : AvPart
    {
        private const float TileGap = BezelLayout.TileGap;

        private sealed class Tile
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail, Icon;
            public TMP_Text Code, Name, Foot;
            public int Key = int.MinValue;
            public bool Selected, Enabled = true, Hover, Footed, Shown;
            public AvState State = AvState.Inert;
            public string FootLevel = "";
        }

        private readonly Tile[] tiles;
        private readonly int columns, rows;
        private readonly float tileH;
        private readonly bool footed;
        private readonly AvControl prev, next;
        private readonly TMP_Text range, emptyText;
        private readonly AvFrame emptyFrame;
        private int page = -1, pages = -1;
        private bool empty;

        public WmcAirframeGrid(RectTransform parent, WmcControls ids, int cols, int rowCount, float tileHeight, bool withFoot, string tilePrefix,
            string pagerPrefix, Action<int> pick, Action<int> turn)
        {
            Rect = AvLay.Child(parent, "AirframeGrid");
            columns = Mathf.Max(1, cols);
            rows = Mathf.Max(1, rowCount);
            tileH = tileHeight;
            footed = withFoot;
            tiles = new Tile[columns * rows];
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = BuildTile(Rect, i, pick);
                ids.Add(tilePrefix + i, tiles[i].Root);
            }
            emptyFrame = AvFrame.Add(Rect, "Empty", AvChamfer.Diagonal(5f));
            emptyText = AvText.Make(Rect, "EmptyText", AvTextRole.ProseSmall, "", TextAlignmentOptions.Center, true);
            emptyFrame.gameObject.SetActive(false);
            emptyText.gameObject.SetActive(false);
            prev = AvControl.Make(Rect, new AvControl.Spec("PREV", () => turn(-1), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            next = AvControl.Make(Rect, new AvControl.Spec("NEXT", () => turn(1), AvButtonStyle.Quiet, AvIcon.ChevronRight, true));
            prev.Help = "Previous page";
            next.Help = "Next page";
            ids.Add(pagerPrefix + "prev", prev);
            ids.Add(pagerPrefix + "next", next);
            range = AvText.Make(Rect, "Range", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
            SetPage(0, 1);
            Restyle();
        }

        public int PerPage => tiles.Length;

        public float TilesHeight => rows * tileH + (rows - 1) * TileGap;

        private Tile BuildTile(RectTransform parent, int slot, Action<int> pick)
        {
            var t = new Tile { Root = AvLay.Child(parent, "Tile" + slot), Footed = footed };
            t.Frame = AvFrame.Add(t.Root, "Frame", default(AvChamfer));
            AvLay.Fill(t.Frame.rectTransform);
            t.Rail = AvLay.Solid(t.Root, "Rail", Color.clear);
            t.Icon = AvLay.Solid(t.Root, "Icon", Color.white);
            t.Icon.preserveAspect = true;
            t.Code = AvText.Make(t.Root, "Code", AvTextRole.Label);
            t.Name = AvText.Make(t.Root, "Name", AvTextRole.ProseSmall);
            t.Foot = AvText.Make(t.Root, "Foot", AvTextRole.DataSmall);
            AvText.Fit(t.Code, false);
            AvText.Fit(t.Name, false);
            AvText.Fit(t.Foot, false);
            AvHit hit = AvHit.On(t.Frame);
            hit.Hover = h => { t.Hover = h; PaintTile(t); };
            hit.Click = e => pick(slot);
            t.Root.gameObject.SetActive(false);
            return t;
        }

        /// <summary>True (and remembered) when the slot's content key changed: build strings only then.</summary>
        public bool NeedsBind(int slot, int key)
        {
            Tile t = tiles[slot];
            if (t.Key == key && t.Shown) return false;
            t.Key = key;
            return true;
        }

        /// <summary>A tile's content; a null foot hides the foot. <paramref name="footLevel"/> is "", "ok", "warn" or "bad";
        /// <paramref name="railClass"/> a rail state class.</summary>
        public void Bind(int slot, Sprite icon, string code, string name, string foot, string footLevel, string railClass, bool selected,
            bool enabled, string tip)
        {
            Tile t = tiles[slot];
            if (!t.Shown)
            {
                t.Shown = true;
                t.Root.gameObject.SetActive(true);
            }
            t.Icon.sprite = icon;
            t.Icon.enabled = icon != null;
            Set(t.Code, code);
            Set(t.Name, name);
            bool hasFoot = foot != null && footed;
            t.Foot.gameObject.SetActive(hasFoot);
            t.FootLevel = footLevel ?? "";
            if (hasFoot) Set(t.Foot, FootGlyph(t.FootLevel) + foot);
            t.State = WmcState.Of(railClass);
            t.Selected = selected;
            t.Enabled = enabled;
            AvHit hit = t.Frame.GetComponent<AvHit>();
            if (hit != null) hit.Interactable = enabled;
            AvHelpTip.Attach(t.Frame.gameObject, tip);
            PaintTile(t);
        }

        public void Hide(int slot)
        {
            Tile t = tiles[slot];
            t.Key = int.MinValue;
            t.Shown = false;
            if (t.Root.gameObject.activeSelf) t.Root.gameObject.SetActive(false);
        }

        /// <summary>One inert card over the whole grid saying why it is empty; null hides it.</summary>
        public void ShowEmpty(string text)
        {
            bool on = text != null;
            if (on) Set(emptyText, text);
            if (on == empty) return;
            empty = on;
            emptyFrame.gameObject.SetActive(on);
            emptyText.gameObject.SetActive(on);
            Restyle();
        }

        public void SetPage(int current, int count)
        {
            if (current == page && count == pages) return;
            page = current;
            pages = count;
            Set(range, Pages.Label(current, count));
            prev.Interactable = current > 0;
            next.Interactable = current < count - 1;
        }

        public override float Measure(float width) => TilesHeight + AvGridTokens.Gap / 2f + AvGridTokens.Row;

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            float w = AvFlowMath.ColumnWidth(slot.W, columns, TileGap);
            for (int i = 0; i < tiles.Length; i++)
            {
                AvLay.Place(tiles[i].Root, i % columns * (w + TileGap), i / columns * (tileH + TileGap), w, tileH);
                PlaceTile(tiles[i], w);
            }
            AvLay.Place(emptyFrame.rectTransform, 0f, 0f, slot.W, TilesHeight);
            AvLay.Place(emptyText.rectTransform, 12f, 12f, slot.W - 24f, Mathf.Max(0f, TilesHeight - 24f));
            float py = TilesHeight + AvGridTokens.Gap / 2f;
            AvLay.Place(prev.Rect, 0f, py, 96f, AvGridTokens.Row);
            AvLay.Place(next.Rect, slot.W - 96f, py, 96f, AvGridTokens.Row);
            AvLay.Place(range.rectTransform, 100f, py, slot.W - 200f, AvGridTokens.Row);
        }

        private void PlaceTile(Tile t, float w)
        {
            float textX = 32f, textW = Mathf.Max(0f, w - textX - 4f);
            AvLay.Place(t.Rail.rectTransform, 0f, 0f, 3f, tileH);
            AvLay.Place(t.Icon.rectTransform, 8f, (tileH - 20f) * 0.5f, 20f, 20f);
            float y = footed ? 4f : 7f, pitch = footed ? 16f : 17f;
            AvLay.Place(t.Code.rectTransform, textX, y, textW, 15f);
            AvLay.Place(t.Name.rectTransform, textX, y + pitch, textW, 15f);
            AvLay.Place(t.Foot.rectTransform, textX, y + 2f * pitch, textW, 15f);
        }

        private static string FootGlyph(string level) => level == "bad" ? AvStates.Glyph(AvState.Danger) : level == "warn" ? AvStates.Glyph(AvState.Caution) : "";

        private static void Set(TMP_Text t, string text)
        {
            text = text ?? "";
            if (t.text != text) t.text = text;
        }

        private void PaintTile(Tile t)
        {
            string st = !t.Enabled ? "disabled" : t.Selected ? "armed" : t.Hover ? "hover" : null;
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(t.State), st);
            t.Frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            t.Frame.Bracket = t.Selected ? 6f : 0f;
            t.Frame.BracketColor = AvStyleHost.FuiColor("select", AvTheme.Accent);
            t.Frame.SetVerticesDirty();
            t.Rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
            t.Code.color = !t.Enabled ? AvTheme.Disabled : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            t.Name.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            t.Foot.color = t.FootLevel.Length == 0 ? t.Name.color : WmcUi.LevelColor(t.FootLevel);
            t.Icon.color = t.Selected ? Color.white : t.Enabled ? AvTheme.Friendly : AvTheme.Dim;
        }

        public override void Restyle()
        {
            foreach (Tile t in tiles) PaintTile(t);
            AvStyle e = AvStyleHost.FuiStyle("card inert");
            emptyFrame.Paint(AvStyleHost.Resolve(e.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(e.Border, AvTheme.Hairline));
            emptyText.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            range.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            prev.Restyle();
            next.Restyle();
        }
    }

    /// <summary>SUPPLY's pilot card: portrait, callsign · name, rank · XP and status on a state rail (not a click target; the stepper
    /// under it acts).</summary>
    internal sealed class WmcPilotCard : AvPart
    {
        private const float Height = 48f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text nameText, rankText, statusText;
        private AvState state = AvState.Inert;

        public WmcPilotCard(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "PilotCard");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            Portrait = WmcPortrait.Build(Rect, new Rect(8f, -2f, 36f, 44f));
            nameText = AvText.Make(Rect, "Name", AvTextRole.Label);
            rankText = AvText.Make(Rect, "Rank", AvTextRole.ProseSmall);
            statusText = AvText.Make(Rect, "Status", AvTextRole.DataSmall);
            AvText.Fit(nameText, false);
            AvText.Fit(rankText, false);
            AvText.Fit(statusText, false);
            Restyle();
        }

        public WmcPortrait Portrait { get; }

        public void Set(string name, string rank, string status, AvState s)
        {
            if (nameText.text != (name ?? "")) nameText.text = name ?? "";
            if (rankText.text != (rank ?? "")) rankText.text = rank ?? "";
            if (statusText.text != (status ?? "")) statusText.text = status ?? "";
            if (s == state) return;
            state = s;
            Restyle();
        }

        public override float Measure(float width) => Height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            float w = Mathf.Max(0f, s.W - 60f);
            AvLay.Place(nameText.rectTransform, 52f, 4f, w, 15f);
            AvLay.Place(rankText.rectTransform, 52f, 18f, w, 15f);
            AvLay.Place(statusText.rectTransform, 52f, 32f, w, 15f);
        }

        public override void Restyle()
        {
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state));
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
            nameText.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            rankText.color = statusText.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
        }
    }

    /// <summary>SUPPLY's DISPATCH card: what REQUISITION would send. A state chip (its word), the airframe and price, the aircraft's
    /// icon, the dispatch line, and under it — always in words, always in view — why it cannot or what being over the limit does.
    /// Both lines wrap, so nothing is cut.</summary>
    internal sealed class WmcDispatchCard : AvPart
    {
        private const float Pad = 8f, ChipW = 96f, TopH = 22f;
        private readonly AvFrame frame;
        private readonly Image rail, icon;
        private readonly AvChip chip;
        private readonly TMP_Text title, line, blocker;
        private AvState state = AvState.Info;
        private string blockerLevel = "";

        public WmcDispatchCard(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "DispatchCard");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            chip = new AvChip(Rect);
            title = AvText.Make(Rect, "Title", AvTextRole.Label);
            AvText.Fit(title, false);
            icon = AvLay.Solid(Rect, "Icon", Color.white);
            icon.preserveAspect = true;
            icon.enabled = false;
            line = AvText.Make(Rect, "Line", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            blocker = AvText.Make(Rect, "Blocker", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public string Title => title.text;

        public string Line => line.text;

        public string Blocker => blocker.text;

        /// <summary><paramref name="blockerLevel"/> is "warn", "info", "ok" or "" (dim).</summary>
        public void Set(string word, AvState s, string titleText, Sprite sprite, string lineText, string blockerText, string level)
        {
            chip.Set(word, s);
            if (title.text != (titleText ?? "")) title.text = titleText ?? "";
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            if (line.text != (lineText ?? "")) line.text = lineText ?? "";
            if (blocker.text != (blockerText ?? "")) blocker.text = blockerText ?? "";
            blockerLevel = level ?? "";
            state = s;
            Restyle();
        }

        private float LineW(float width) => Mathf.Max(1f, width - 2f * Pad - 4f);

        public override float Measure(float width) =>
            Pad + TopH + 4f + AvText.Height(line, LineW(width)) + (blocker.text.Length > 0 ? 2f + AvText.Height(blocker, LineW(width)) : 0f) + Pad;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            chip.Place(new AvSlot(Pad, Pad, ChipW, AvGridTokens.ChipStrip));
            AvLay.Place(title.rectTransform, Pad + ChipW + 8f, Pad, Mathf.Max(0f, s.W - Pad - ChipW - 8f - Pad - 24f), TopH);
            AvLay.Place(icon.rectTransform, s.W - Pad - 20f, Pad + 1f, 20f, 20f);
            float y = Pad + TopH + 4f, lh = AvText.Height(line, LineW(s.W));
            AvLay.Place(line.rectTransform, Pad + 4f, y, LineW(s.W), lh);
            AvLay.Place(blocker.rectTransform, Pad + 4f, y + lh + 2f, LineW(s.W), AvText.Height(blocker, LineW(s.W)));
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card " + AvStates.Class(state));
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            rail.color = WmcState.Color(AvStates.Class(state));
            title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            line.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            blocker.color = blockerLevel == "info" ? AvStyleHost.FuiColor("info", AvTheme.RailInfo)
                : blockerLevel.Length > 0 ? WmcUi.LevelColor(blockerLevel) : line.color;
            chip.Restyle();
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
