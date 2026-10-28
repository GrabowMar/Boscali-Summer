using NOAvionics;
using System;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>SET > THIS PILOT > DISPLAY: the colour scheme board and the three knobs of the Custom scheme.</summary>
    internal sealed partial class SettingsMfdPanel
    {
        private static readonly string[] SchemeBriefs =
        {
            "PORTAL: the house scheme. Cyan keys on blue-grey glass, amber for caution.",
            "STEEL: cool steel glass; keys and warnings follow the game's own colour theme.",
            "ACE: deep blue glass with amber selection, after the arcade flight classics.",
            "PHOSPHOR: green phosphor with magenta selection, after a strike fighter's panoramic display.",
            "FIELD OPS: olive drab and brass with cream ink, like a ground terminal in a tent.",
            "AMBER CRT: one amber phosphor for everything; red is kept for danger alone.",
            "GLASS MFD: black glass, white ink, cyan selection and magenta keys.",
            "NIGHT OPS: dim teal with dimmed whites, easy on dark-adapted eyes.",
            "CUSTOM: your own scheme from the three dials below. Caution stays amber and danger red.",
        };

        private void BuildSchemeSection(AvFlow flow, int page)
        {
            flow.Section(AvIcon.AdjustmentsHorizontal, "COLOUR SCHEME", "EVERY PANEL · SAVED");
            SetSchemeBoard board = flow.Add(new SetSchemeBoard(flow.Content, () => (int)settings.AvionicsTheme.Value, i =>
            {
                settings.AvionicsTheme.Value = (AvThemeId)i;
                Echo("SCHEME — " + AvThemes.Names[i]);
                Changed();
            }));
            for (int i = 0; i < SchemeBriefs.Length; i++) board.SetHelp(i, SchemeBriefs[i]);

            Func<bool> custom = () => settings.AvionicsTheme.Value == AvThemeId.Custom;
            Func<string> pickCustom = () => "Choose the CUSTOM scheme first.";
            SetRingRow dials = flow.Add(new SetRingRow(flow.Content));
            HueRing(flow, dials, page, "KEY HUE", settings.AvionicsCustomKeyHue,
                "Hue of keys, selection and ready states on every panel, in 15 degree steps.", custom, pickCustom);
            HueRing(flow, dials, page, "GLASS HUE", settings.AvionicsCustomGlassHue,
                "Hue of the panel glass, frames and quiet text, in 15 degree steps.", custom, pickCustom);
            PercentRing(flow, dials, page, "GLASS TINT", settings.AvionicsCustomGlassTint, 0f, 1f, .1f,
                "How much of its hue the glass carries. 0% is neutral grey.", custom, pickCustom);

            void Refresh()
            {
                board.Refresh();
                dials.SetShown(custom());
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
        }

        /// <summary>Standing orders for every SET page; they print on the bay cover under a short page.</summary>
        private static void ApplyOrders(AvConsole c, bool server)
        {
            if (server)
            {
                string[] rules =
                {
                    "The host sets these rules for every pilot on the net.",
                    "A client reads them locked; ask the host to change one.",
                    "Each change is logged under SESSION CHANGES with its age.",
                };
                c.Orders(SWorld, rules);
                c.Orders(SForces, rules);
                c.Orders(SEffects, rules);
                c.Orders(STasking,
                    "The host issues faction tasking from this board.",
                    "Pilots take and fly the tasks from the MIS console.");
                return;
            }
            c.Orders(CPanels,
                "Pick a scheme, then a finish: every console repaints at once.",
                "CUSTOM opens the four finish dials; GLASS puts them back.",
                "Caution stays amber and danger red in every scheme.");
            c.Orders(CMap,
                "EXPANDED LAYOUT off returns the game's own map screen.",
                "Map layers (industry, supply, roads) live on the MAP bezel.",
                "Custom backdrops come from the wallpapers folder; RESCAN after adding one.");
            c.Orders(CHud,
                "The TAC BOARD takes a change on its next tick, in flight.",
                "Every key the mod reads is at the foot of this page.");
            c.Orders(CCockpit,
                "Every cue on this page is felt by this pilot only.",
                "Turn a single cue down before you switch the whole set off.");
            c.Orders(CWorld,
                "World looks are drawn on this machine only; the host's weather is the same.",
                "PERF presets also set the costly looks on this page.");
            c.Orders(CPerf,
                "Watch FRAME TIME while you change a budget.",
                "A preset is a starting point: tune single switches after it.",
                "Changes apply in this mission; no restart.");
        }

        private SetRingCell HueRing(AvFlow flow, SetRingRow row, int page, string title, ConfigEntry<float> entry,
            string help, Func<bool> enabled, Func<string> reason) =>
            Ring(flow, row, page, title, () => AvNum.Fixed(entry.Value, 0) + " DEG", () => Mathf.Clamp01(entry.Value / 360f),
                d => entry.Value = Mathf.Repeat(Mathf.Round(entry.Value / 15f) * 15f + d * 15f, 360f),
                () => true, () => true, help, enabled, reason);
    }

    /// <summary>
    /// Nine scheme tiles, three to a line. Each tile is painted in its own scheme (surface, ink and four signal
    /// chips), so the board is the preview; the active one carries the lit corner and the word ON.
    /// </summary>
    internal sealed class SetSchemeBoard : AvPart
    {
        private const int Columns = 3;
        private const float TileH = 40f, Gap = AvGridTokens.Gap;
        private static readonly string[] ChipRoles = { "key", "select", "caution", "danger" };

        private sealed class Tile
        {
            public RectTransform Rect;
            public AvFrame Frame;
            public TMP_Text Name, State;
            public Image[] Chips;
            public bool Hover;
        }

        private readonly Tile[] tiles = new Tile[AvThemes.Names.Length];
        private readonly Func<int> get;
        private string customSeen;
        private int generation = -1, selected = -1;

        public SetSchemeBoard(RectTransform parent, Func<int> getter, Action<int> set)
        {
            Rect = AvLay.Child(parent, "SchemeBoard");
            get = getter;
            for (int i = 0; i < tiles.Length; i++)
            {
                int index = i;
                var t = new Tile { Rect = AvLay.Child(Rect, "Scheme " + AvThemes.Names[i]) };
                t.Frame = AvFrame.Add(t.Rect, "Frame", AvChamfer.Diagonal(5f));
                AvLay.Fill(t.Frame.rectTransform);
                t.Name = AvText.Make(t.Rect, "Name", AvTextRole.Label, AvThemes.Names[i]);
                AvText.Fit(t.Name, false);
                t.State = AvText.Make(t.Rect, "State", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                t.Chips = new Image[ChipRoles.Length];
                for (int c = 0; c < t.Chips.Length; c++) t.Chips[c] = AvLay.Solid(t.Rect, "Chip " + ChipRoles[c], Color.clear);
                AvHit hit = AvHit.On(t.Frame);
                hit.Hover = h => { t.Hover = h; Paint(index); };
                hit.Click = e => { if (e.button == PointerEventData.InputButton.Left) { set?.Invoke(index); Refresh(); } };
                tiles[i] = t;
            }
            Refresh();
        }

        public void SetHelp(int index, string help)
        {
            if (index >= 0 && index < tiles.Length) AvHelpTip.Attach(tiles[index].Frame.gameObject, help);
        }

        /// <summary>Repaints when the selection, the live sheet or the Custom palette changed.</summary>
        public void Refresh()
        {
            int now = get != null ? get() : -1;
            if (now == selected && generation == AvStyleHost.FuiGeneration && ReferenceEquals(customSeen, AvStyleHost.CustomPalette)) return;
            selected = now; generation = AvStyleHost.FuiGeneration; customSeen = AvStyleHost.CustomPalette;
            for (int i = 0; i < tiles.Length; i++) Paint(i);
        }

        private void Paint(int index)
        {
            Tile t = tiles[index];
            AvStyleSheet sheet = AvStyleSheet.Parse(AvStyleHost.PaletteText((AvThemeId)index) ?? "");
            Color surface = Role(sheet, "surface", AvTheme.Surface), raised = Role(sheet, "surface-raised", AvTheme.SurfaceRaised);
            bool on = index == selected;
            Color edge = on ? Role(sheet, "select", AvTheme.Accent) : t.Hover ? Role(sheet, "ink", AvTheme.TextPrimary) : Role(sheet, "frame", AvTheme.Frame);
            t.Frame.Paint((t.Hover || on ? raised : surface).WithAlpha(1f), edge);
            t.Name.color = Role(sheet, "ink", AvTheme.TextPrimary);
            t.State.text = on ? "ON" : "";
            t.State.color = Role(sheet, "select", AvTheme.Accent);
            for (int c = 0; c < t.Chips.Length; c++) t.Chips[c].color = Role(sheet, ChipRoles[c], AvTheme.Accent).WithAlpha(1f);
        }

        private static Color Role(AvStyleSheet sheet, string role, Color fallback) =>
            AvStyleHost.Resolve(sheet.Paint(role, fallback.ToRgba()), fallback);

        public override float Measure(float width)
        {
            int rows = (tiles.Length + Columns - 1) / Columns;
            return rows * TileH + (rows - 1) * Gap;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = AvFlowMath.ColumnWidth(s.W, Columns, Gap);
            for (int i = 0; i < tiles.Length; i++)
            {
                Tile t = tiles[i];
                AvLay.Place(t.Rect, i % Columns * (w + Gap), i / Columns * (TileH + Gap), w, TileH);
                AvLay.Place(t.Name.rectTransform, 9f, 4f, w - 18f - 26f, 18f);
                AvLay.Place(t.State.rectTransform, w - 9f - 26f, 4f, 26f, 18f);
                float chip = (w - 18f - 3f * 3f) / 4f;
                for (int c = 0; c < t.Chips.Length; c++) AvLay.Place(t.Chips[c].rectTransform, 9f + c * (chip + 3f), 26f, chip, 6f);
            }
        }

        public override void Restyle() { generation = -1; Refresh(); }
    }
}
