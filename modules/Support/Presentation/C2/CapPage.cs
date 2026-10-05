using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>What the shared C2 chrome paints on every surface (CAP, ORBIT, the full-screen station): identity, ledger, session line.</summary>
    internal class C2ChromeView
    {
        public int Credit;
        public C2Console Console;
        public string Faction = "", Callsign = "", Session = "", KeyRot = "", Uplinks = "", Space = "", Alert = "";
        public AvState UplinkTone = AvState.Inert;
        public bool Link = true;
        public int BoardCount;
    }

    /// <summary>Everything the CAP page and the C2 chrome paint, built each refresh from the manager and the controller (never from game objects).</summary>
    internal sealed class CapView : C2ChromeView
    {
        public const int FavouriteSlots = 4;

        public readonly List<CallTile> Tiles = new List<CallTile>(16);
        public string NextUnlock = "", Words = "";
        public bool Pending;
        public readonly SupportActionId?[] Favourites = new SupportActionId?[FavouriteSlots];
        public AimSource Aim;
        public string AimGrid = "";
        public int LastDelta;
    }

    /// <summary>
    /// The [1] CAP tab: the host console, the eleven capability rows and the EXECUTE ORDER box (tall pages) or strip (the 596 page).
    /// Fixed rows, no scrolling. The page owns no policy: every press goes through the <see cref="CallsController"/> exactly as the
    /// old CALLS page did. Coordinates are page coordinates: y = 0 is the bottom of the chrome.
    /// </summary>
    internal sealed class CapPage
    {
        private const float Gap = 6f, CellH = 34f, ButtonH = 30f, StripH = 30f, BoxBodyFull = 76f;
        private readonly CallsController calls;
        private readonly float width;
        private readonly bool full;
        private readonly Action<AvPart> register;
        private readonly C2ConsoleView console;
        private readonly C2Box caps;
        private readonly Dictionary<SupportActionId, C2Row> rows = new Dictionary<SupportActionId, C2Row>();
        private readonly Action[] pinActions;
        private readonly AvControl[] favourites = new AvControl[CapView.FavouriteSlots];
        private readonly AvControl execute, abort;
        private readonly C2Box execBox;
        private readonly AvFrame stripFrame;
        private readonly Image stripRail;
        private readonly TMP_Text stripText;
        private readonly TMP_Text[] cellKey = new TMP_Text[4], cellValue = new TMP_Text[4];
        private SupportActionId armedId;
        private bool armedNow;
        private AvState stripTone = AvState.Inert;

        public CapPage(RectTransform parent, float width, float height, CallsController calls, Action<AvPart> register)
        {
            this.calls = calls;
            this.width = width;
            this.register = register ?? (_ => { });
            full = height >= 560f;
            int n = CallSheet.Rows.Count;

            // ---- Vertical budget ----
            // Tall page: gap, console, gap, capabilities box, gap, EXECUTE ORDER box, gap.
            // 596 page: gap, capabilities box, gap, strip, gap, favourites, gap, console.
            float boxChrome = C2Box.HeaderH + 2f;
            float fixedH = full
                ? Gap + Gap + boxChrome + Gap + (C2Box.HeaderH + BoxBodyFull) + Gap
                : 4f + boxChrome + 4f + StripH + 4f + 26f + 4f + 4f;
            int consoleLines = full ? 4 : 2;
            float spare = height - fixedH - C2ConsoleView.HeightFor(consoleLines) - n * 2f;
            float rowH = Mathf.Clamp(Mathf.Floor(spare / n), full ? 30f : 24f, full ? 48f : 30f);
            spare -= rowH * n;
            consoleLines = Mathf.Min(7, consoleLines + Mathf.Max(0, Mathf.FloorToInt(spare / C2ConsoleView.LineH)));
            float consoleH = C2ConsoleView.HeightFor(consoleLines);

            console = Make(new C2ConsoleView(parent, consoleLines));
            float y = full ? Gap : 4f;
            if (full)
            {
                console.Place(new AvSlot(0f, y, width, consoleH));
                y += consoleH + Gap;
            }

            caps = Make(new C2Box(parent, "AUTHORIZED CAPABILITIES · " + n));
            caps.BodyHeight = n * (rowH + 2f) + 2f;
            caps.Place(new AvSlot(0f, y, width, caps.Measure(width)));
            y += caps.Measure(width) + (full ? Gap : 4f);

            pinActions = new Action[n];
            for (int i = 0; i < n; i++)
            {
                SupportActionId id = CallSheet.Rows[i].Id;
                C2Row row = Make(new C2Row(caps.Body, rowH, full));
                row.Place(new AvSlot(1f, 1f + i * (rowH + 2f), width - 4f, rowH));
                pinActions[i] = Bind(row, this.calls, id);
                rows[id] = row;
            }

            RectTransform buttonsParent, favParent;
            float buttonsX, buttonsY, favY, favH, favW;
            if (full)
            {
                execBox = Make(new C2Box(parent, "STANDING BY"));
                execBox.BodyHeight = BoxBodyFull;
                execBox.Place(new AvSlot(0f, y, width, execBox.Measure(width)));
                buttonsParent = favParent = execBox.Body;
                for (int i = 0; i < 4; i++)
                {
                    cellKey[i] = C2Kit.Mono(buttonsParent, "CellKey" + i, 10f, TextAlignmentOptions.MidlineLeft, false, 2f);
                    cellValue[i] = C2Kit.Mono(buttonsParent, "CellValue" + i, 12f, TextAlignmentOptions.MidlineLeft, true);
                    C2Kit.Place(cellKey[i], CellX(i), 2f, CellW(i) - 4f, 12f);
                    C2Kit.Place(cellValue[i], CellX(i), 14f, CellW(i) - 4f, 18f);
                }
                buttonsX = 6f;
                buttonsY = favY = 2f + CellH + 4f;
                favH = ButtonH;
                favW = (width - 2f - 12f - 3f * 4f) / 4f;
            }
            else
            {
                RectTransform strip = AvLay.Child(parent, "Strip");
                AvLay.Place(strip, 0f, y, width, StripH);
                stripFrame = AvFrame.Add(strip, "Frame", default(AvChamfer));
                AvLay.Fill(stripFrame.rectTransform);
                stripRail = AvLay.Solid(strip, "Rail", Color.clear);
                AvLay.Place(stripRail.rectTransform, 0f, 0f, 3f, StripH);
                stripText = C2Kit.Mono(strip, "StripText", 10.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
                buttonsParent = strip;
                buttonsX = 0f;
                buttonsY = 4f;
                y += StripH + 4f;
                favParent = parent;
                favY = y;
                favH = 26f;
                favW = (width - 12f - 3f * 4f) / 4f;
                y += favH + 4f;
                console.Place(new AvSlot(0f, y, width, consoleH));
            }

            execute = AvControl.Make(buttonsParent, new AvControl.Spec("EXECUTE", () => { if (armedNow) this.calls?.Press(armedId); }, AvButtonStyle.Danger));
            abort = AvControl.Make(buttonsParent, new AvControl.Spec("ABORT", () => this.calls?.Disarm(), AvButtonStyle.Quiet));
            execute.SingleLine();
            abort.SingleLine();
            execute.Help = "Fire the armed CALL at the aim shown. The host spends and answers.";
            abort.Help = "Disarm the armed CALL. Nothing is spent.";
            if (full)
            {
                float all = width - 2f - 12f, exW = Mathf.Floor(all * 0.62f);
                AvLay.Place(execute.Rect, buttonsX, buttonsY, exW, ButtonH);
                AvLay.Place(abort.Rect, buttonsX + exW + 6f, buttonsY, all - exW - 6f, ButtonH);
            }
            else
            {
                AvLay.Place(abort.Rect, width - 6f - 64f, buttonsY, 64f, 22f);
                AvLay.Place(execute.Rect, width - 6f - 64f - 4f - 84f, buttonsY, 84f, 22f);
            }

            for (int i = 0; i < favourites.Length; i++)
            {
                int slot = i;
                favourites[i] = AvControl.Make(favParent, new AvControl.Spec((i + 1) + " · EMPTY", () => PressFavourite(slot), AvButtonStyle.Primary));
                favourites[i].SingleLine();
                AvLay.Place(favourites[i].Rect, 6f + i * (favW + 4f), favY, favW, favH);
            }
            Restyle();
        }

        private T Make<T>(T part) where T : AvPart
        {
            register(part);
            return part;
        }

        // ---- Paint -------------------------------------------------------------------------------------------

        public void Paint(CapView v)
        {
            console.Show(v.Console);
            armedNow = false;
            int open = 0;
            for (int i = 0; i < v.Tiles.Count; i++)
            {
                CallTile t = v.Tiles[i];
                if (!rows.TryGetValue(t.Id, out C2Row row)) continue;
                int index = IndexOf(t.Id);
                bool armed = t.State == CallState.Armed;
                if (armed) { armedNow = true; armedId = t.Id; }
                if (t.State != CallState.Locked && t.State != CallState.Offline) open++;
                PaintRow(row, t, index, IsPinned(v, t.Id), pinActions[index]);
            }

            string pinned = "";
            for (int s = 0; s < v.Favourites.Length; s++)
            {
                if (!v.Favourites[s].HasValue || !CallSheet.TryGet(v.Favourites[s].Value, out CallRow fr)) continue;
                int space = fr.Label.IndexOf(' ');
                pinned += (pinned.Length > 0 ? " · " : "") + (s + 1) + " " + (space > 0 ? fr.Label.Substring(0, space) : fr.Label);
            }
            caps.SetTitle("AUTHORIZED CAPABILITIES · " + open);
            caps.SetMeta(pinned.Length > 0 ? "PINNED " + pinned : "NOTHING PINNED");

            for (int i = 0; i < favourites.Length; i++) PaintFavourite(i, v);
            PaintExecute(v);
        }

        /// <summary>How many capabilities the header counts as authorized (everything not locked or offline).</summary>
        public static int Authorized(IReadOnlyList<CallTile> tiles)
        {
            int n = 0;
            for (int i = 0; i < tiles.Count; i++) if (tiles[i].State != CallState.Locked && tiles[i].State != CallState.Offline) n++;
            return n;
        }

        private void PaintFavourite(int slot, CapView v)
        {
            AvControl button = favourites[slot];
            SupportActionId? id = slot < v.Favourites.Length ? v.Favourites[slot] : null;
            if (id.HasValue)
            {
                for (int i = 0; i < v.Tiles.Count; i++)
                {
                    CallTile t = v.Tiles[i];
                    if (t.Id != id.Value) continue;
                    button.Label = (slot + 1) + " · " + t.Label;
                    string tip = t.Label + " · " + C2Cap.StateWord(t) + " · " + t.CostText;
                    if (button.Help != tip) button.Help = tip;
                    button.Interactable = t.Enabled;
                    button.Armed = t.State == CallState.Armed;
                    return;
                }
            }
            button.Label = (slot + 1) + " · EMPTY";
            if (button.Help == null || !button.Help.StartsWith("Empty", StringComparison.Ordinal)) button.Help = "Empty slot: press the star on a call to pin it here.";
            button.Interactable = false;
            button.Armed = false;
        }

        private void PaintExecute(CapView v)
        {
            CallTile armedTile = default;
            foreach (CallTile t in v.Tiles) if (t.State == CallState.Armed) { armedTile = t; break; }
            string aimSrc = v.Aim == AimSource.Pod ? "POD" : v.Aim == AimSource.Map ? "MAP" : "NONE";
            string target = v.AimGrid.Length > 0 ? v.AimGrid : v.Aim == AimSource.Pod ? "POD" : "R-CLICK MAP";
            int pinnedCount = 0;
            foreach (SupportActionId? f in v.Favourites) if (f.HasValue) pinnedCount++;

            execute.Rect.gameObject.SetActive(armedNow);
            abort.Rect.gameObject.SetActive(armedNow);
            if (armedNow) execute.Label = full ? "EXECUTE · " + armedTile.CostText : "EXECUTE";
            if (full)
            {
                foreach (AvControl f in favourites) f.Rect.gameObject.SetActive(!armedNow);
                if (armedNow)
                {
                    execBox.SetTitle("EXECUTE ORDER · " + armedTile.Label);
                    execBox.SetMeta("READBACK " + C2Words.AuthCode((int)armedTile.Id, 'R')); // cosmetic
                    Cells(("TGT", target), ("AIM SRC", aimSrc), ("FRIENDLY", "HOST CHECK"), ("COST", armedTile.CostText));
                }
                else
                {
                    execBox.SetTitle(v.Pending ? "ORDER PENDING" : "STANDING BY");
                    execBox.SetMeta(v.Pending ? "AWAITING HOST" : "R-CLICK MAP TO AIM");
                    Cells(("NEXT UNLOCK", string.IsNullOrEmpty(v.NextUnlock) ? "ALL OPEN" : v.NextUnlock),
                        ("LEDGER", C2Cap.Delta(v.LastDelta)), ("PINNED", pinnedCount + "/" + CapView.FavouriteSlots),
                        ("AIM", v.Aim == AimSource.None ? "R-CLICK MAP" : aimSrc));
                }
                return;
            }

            stripTone = armedNow ? AvState.Caution : v.Pending ? AvState.Info : AvState.Ready;
            string line = armedNow ? "ARMED · " + armedTile.Label + " · AIM " + aimSrc + " · " + armedTile.CostText
                : v.Pending ? "ORDER PENDING · AWAITING HOST" : "STANDING BY · R-CLICK MAP TO AIM";
            float room = width - 14f - (armedNow ? 64f + 4f + 84f + 8f : 0f);
            OpsText.Set(stripText, C2Kit.FitTo(stripText, line, room));
            C2Kit.Place(stripText, 10f, 0f, room, StripH);
            RestyleStrip();
        }

        private void Cells(params (string key, string value)[] cells)
        {
            for (int i = 0; i < 4 && i < cells.Length; i++)
            {
                OpsText.Set(cellKey[i], C2Kit.FitTo(cellKey[i], cells[i].key, CellW(i) - 4f));
                OpsText.Set(cellValue[i], C2Kit.FitTo(cellValue[i], cells[i].value, CellW(i) - 4f));
            }
        }

        // The first cell (the next-unlock goal) is the longest text, so it gets the widest column.
        private static readonly float[] CellShare = { 0.36f, 0.20f, 0.16f, 0.28f };

        private float CellW(int i) => (width - 2f - 12f) * CellShare[i];

        private float CellX(int i)
        {
            float x = 6f;
            for (int k = 0; k < i; k++) x += CellW(k);
            return x;
        }

        /// <summary>
        /// Wires one capability row to the controller: the primary button presses the call (arm, then fire), the JTAC row gets UNLASE.
        /// Shared by the CAP page and the NET / SOF pages so every call row behaves identically. Returns the pin action for the row.
        /// </summary>
        internal static Action Bind(C2Row row, CallsController calls, SupportActionId id)
        {
            row.Primary.Clicked += () => calls?.Press(id);
            if (id == SupportActionId.JtacMark)
            {
                row.SetExtra("UNLASE", () => calls?.Unlase());
                row.Extra.Help = "Clear the lase at the current POD or map aim. Free.";
            }
            return () => calls?.Pin(id);
        }

        /// <summary>Paints one capability row from its tile (index is the zero-based position in the call sheet). Shared with the NET / SOF pages.</summary>
        internal static void PaintRow(C2Row row, CallTile t, int index, bool isPinned, Action pinAction)
        {
            string tip = C2Cap.RowTip(t);
            C2Chip chip = C2Cap.ChipKind(t.Reason);
            bool armed = t.State == CallState.Armed;
            row.Set((index + 1).ToString("00"), t.Label, t.Reason,
                chip == C2Chip.Discount ? AvState.Ready : chip == C2Chip.Surcharge ? AvState.Danger : AvState.Info,
                C2Cap.Sub(t, t.Id == SupportActionId.JtacMark), t.CostText, C2Cap.StateWord(t), StateOf(t.State),
                armed ? "EXECUTE" : t.State == CallState.Ready ? "AUTHORIZE" : t.State == CallState.Pending ? "WAIT" : "DENIED",
                armed ? AvButtonStyle.Danger : t.State == CallState.Ready ? AvButtonStyle.Primary : AvButtonStyle.Default, t.Enabled);
            row.Armed = armed;
            row.SetPin(isPinned, pinAction);
            row.SetHelp(tip, isPinned ? "Pinned to a favourite slot. Press to pin again." : "Pin to a favourite slot; a favourite fires from its key.");
            if (row.Primary.Help != tip) row.Primary.Help = tip;
        }

        internal static int IndexOf(SupportActionId id)
        {
            for (int i = 0; i < CallSheet.Rows.Count; i++) if (CallSheet.Rows[i].Id == id) return i;
            return 0;
        }

        internal static bool IsPinned(CapView v, SupportActionId id)
        {
            foreach (SupportActionId? f in v.Favourites) if (f.HasValue && f.Value == id) return true;
            return false;
        }

        private void PressFavourite(int slot)
        {
            if (calls == null || slot < 0 || slot >= calls.Favourites.Length) return;
            SupportActionId? id = calls.Favourites[slot];
            if (id.HasValue) calls.Press(id.Value);
        }

        internal static AvState StateOf(CallState s) =>
            s == CallState.Ready ? AvState.Ready : s == CallState.Armed ? AvState.Caution : s == CallState.Pending ? AvState.Info
            : s == CallState.Offline || s == CallState.LowCredit ? AvState.Danger : AvState.Inert;

        // ---- Theme -------------------------------------------------------------------------------------------

        public void Restyle()
        {
            foreach (TMP_Text t in cellKey) if (t != null) t.color = OpsInk.Muted;
            foreach (TMP_Text t in cellValue) if (t != null) t.color = OpsInk.Ink;
            foreach (AvControl f in favourites) if (f != null) f.Restyle();
            execute?.Restyle();
            abort?.Restyle();
            RestyleStrip();
        }

        private void RestyleStrip()
        {
            if (stripFrame == null) return;
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(stripTone), armedNow ? "armed" : null);
            stripFrame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            stripRail.color = armedNow ? OpsInk.Select : OpsInk.Rail(stripTone);
            stripText.color = OpsInk.Word(stripTone);
        }
    }
}
